using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private readonly Dictionary<string, Dictionary<string, AccountDeleteReceipt>> _pendingAccountDeletes =
            new Dictionary<string, Dictionary<string, AccountDeleteReceipt>>(StringComparer.Ordinal);

        /// <summary>Estimates the fee, remaining XRP transfer and deletion restrictions without signing or submitting.</summary>
        public async Task<AccountDeleteQuote> GetAccountDeleteQuoteAsync(string senderAddress, AccountDeleteRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            var input = SnapshotAccountDeleteRequest(request);
            ValidateClassicAddress(senderAddress, nameof(senderAddress));
            if (senderAddress == input.Destination)
                throw new ArgumentException("AccountDelete requires a different destination account.", nameof(request));
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var gate = RentAccountGate(senderAddress);
                bool entered = false;
                try
                {
                    await gate.Semaphore.WaitAsync(operation.Token).ConfigureAwait(false);
                    entered = true;
                    await ResolvePendingPaymentsAsync(senderAddress, operation.Token).ConfigureAwait(false);
                    return await ReadAccountDeleteQuoteAsync(senderAddress, input, operation.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (entered) gate.Semaphore.Release();
                    ReturnAccountGate(senderAddress, gate);
                }
            }
        }

        /// <summary>Deletes an eligible account and transfers its remaining XRP. Requires its master seed.</summary>
        public async Task<AccountDeleteResult> DeleteAccountAsync(string seed, AccountDeleteRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            var input = SnapshotAccountDeleteRequest(request);
            string sender;
            try { sender = Seed.FromBase58(seed).KeyPair().Id(); }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            { throw new ArgumentException("Invalid XRPL seed.", nameof(seed)); }
            if (sender == input.Destination)
                throw new ArgumentException("AccountDelete requires a different destination account.", nameof(request));
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var gate = RentAccountGate(sender);
                bool entered = false;
                try
                {
                    await gate.Semaphore.WaitAsync(operation.Token).ConfigureAwait(false);
                    entered = true;
                    await ResolvePendingPaymentsAsync(sender, operation.Token).ConfigureAwait(false);
                    var quote = await ReadAccountDeleteQuoteAsync(sender, input, operation.Token).ConfigureAwait(false);
                    if (!quote.CanDelete) throw new InvalidOperationException(string.Join(" ", quote.BlockingReasons));
                    uint firstLedger = Math.Max(checked(quote.LedgerIndex + 1), quote.ObservedLedgerIndex);
                    uint lastLedger = checked(quote.ObservedLedgerIndex + _options.LastLedgerOffset);
                    var tx = new JObject { ["TransactionType"] = "AccountDelete", ["Account"] = sender,
                        ["Destination"] = input.Destination, ["Sequence"] = quote.Sequence,
                        ["Fee"] = XrpToDrops(quote.EstimatedFeeXrp, "fee"), ["LastLedgerSequence"] = lastLedger };
                    if (input.Tag.HasValue) tx["DestinationTag"] = input.Tag.Value;
                    var signed = TxSigner.SignJson(tx, seed);
                    var receipt = new AccountDeleteReceipt(signed.Hash, sender, quote.Sequence, firstLedger,
                        lastLedger, signed.TxBlob, _options.RpcEndpoint);
                    if (input.Prepared != null) await input.Prepared(receipt, operation.Token).ConfigureAwait(false);
                    operation.Token.ThrowIfCancellationRequested();
                    lock (_receiptLookupLock)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        var payment = FindPendingPayment(sender);
                        if (payment != null) throw new PendingPaymentException(payment);
                        var deletion = FindPendingAccountDelete(sender);
                        if (deletion != null) throw new PendingAccountDeleteException(deletion);
                        ReceiptLookup known;
                        if (_receiptLookups.TryGetValue(receipt.Hash, out known) && known.Finalized)
                            throw new InvalidOperationException("This AccountDelete has already reached a final state.");
                        TrackPendingAccountDelete(receipt);
                    }
                    var result = new AccountDeleteResult { Receipt = receipt, Transaction = Unknown(receipt.Hash) };
                    string activeMethod = "submit";
                    using (var wait = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                    {
                        wait.CancelAfter(_options.ConfirmationTimeout);
                        try
                        {
                            var submission = await RpcAsync("submit", new JObject {
                                ["tx_blob"] = signed.TxBlob, ["fail_hard"] = true }, wait.Token).ConfigureAwait(false);
                            result.SubmissionResultCode = (string)submission["engine_result"];
                            var timer = Stopwatch.StartNew();
                            while (timer.Elapsed < _options.ConfirmationTimeout)
                            {
                                wait.Token.ThrowIfCancellationRequested();
                                activeMethod = "tx";
                                var recovered = await GetAccountDeleteAsync(receipt, wait.Token).ConfigureAwait(false);
                                result.Transaction = recovered.Transaction;
                                result.ReturnedAmountXrp = recovered.ReturnedAmountXrp;
                                if (IsFinal(result.Status)) return result;
                                var remaining = _options.ConfirmationTimeout - timer.Elapsed;
                                if (remaining <= TimeSpan.Zero) break;
                                await Task.Delay(remaining < _options.PollInterval ? remaining : _options.PollInterval, wait.Token).ConfigureAwait(false);
                            }
                            result.Diagnostic = "Confirmation wait expired. Query the saved AccountDelete receipt before creating another transaction.";
                            result.Error = PaymentFailure(new OperationCanceledException(), activeMethod, seed, operation.IsCancellationRequested);
                        }
                        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException ||
                            ex is InvalidDataException || ex is XrplRpcException || ex is ArgumentException || ex is FormatException ||
                            ex is OverflowException || (ex is ObjectDisposedException && _lifetime.IsCancellationRequested))
                        {
                            result.Diagnostic = "AccountDelete submission/confirmation is indeterminate (" + ex.GetType().Name +
                                "). Query the saved receipt; do not create a replacement transaction.";
                            result.Error = PaymentFailure(ex, activeMethod, seed, operation.IsCancellationRequested);
                        }
                        return result;
                    }
                }
                finally
                {
                    if (entered) gate.Semaphore.Release();
                    ReturnAccountGate(sender, gate);
                }
            }
        }

        /// <summary>Recovers a saved AccountDelete, including complete-history expiry proof. Never submits it.</summary>
        public async Task<AccountDeleteResult> GetAccountDeleteAsync(AccountDeleteReceipt receipt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            if (receipt.RpcEndpoint != _options.RpcEndpoint)
                throw new ArgumentException("Receipt belongs to a different RPC endpoint.", nameof(receipt));
            ReceiptLookup lookup;
            lock (_receiptLookupLock)
            {
                if (!_receiptLookups.TryGetValue(receipt.Hash, out lookup))
                    _receiptLookups.Add(receipt.Hash, lookup = new ReceiptLookup());
                lookup.Readers++;
            }
            try
            {
                decimal? returned = null;
                var tx = await ReadTransactionAsync(receipt.Hash, receipt.FirstLedger, receipt.LastLedger,
                    cancellationToken, inspect: (envelope, info) => {
                        ValidateAccountDeleteReceiptTransaction(envelope, info, receipt);
                        if (info.Status == TransactionStatus.Succeeded)
                            returned = ReadAccountDeleteReturnedXrp(envelope, receipt);
                    }).ConfigureAwait(false);
                if (!tx.Found)
                {
                    var ledger = await GetLedgerAsync(cancellationToken).ConfigureAwait(false);
                    if (ledger.Index >= receipt.LastLedger && tx.SearchedAll) tx.Status = TransactionStatus.Expired;
                }
                lock (_receiptLookupLock)
                {
                    if (IsFinal(tx.Status))
                    {
                        lookup.Finalized = true;
                        Dictionary<string, AccountDeleteReceipt> pending;
                        if (_pendingAccountDeletes.TryGetValue(receipt.Sender, out pending))
                        {
                            pending.Remove(receipt.Hash);
                            if (pending.Count == 0) _pendingAccountDeletes.Remove(receipt.Sender);
                        }
                    }
                    else if (!lookup.Finalized) TrackPendingAccountDelete(receipt);
                }
                return new AccountDeleteResult { Receipt = receipt, Transaction = tx, ReturnedAmountXrp = returned };
            }
            finally
            {
                lock (_receiptLookupLock)
                    if (--lookup.Readers == 0 && !lookup.Finalized) _receiptLookups.Remove(receipt.Hash);
            }
        }

        private AccountDeleteReceipt FindPendingAccountDelete(string sender)
        {
            lock (_receiptLookupLock)
            {
                Dictionary<string, AccountDeleteReceipt> pending;
                if (_pendingAccountDeletes.TryGetValue(sender, out pending))
                    foreach (var receipt in pending.Values) return receipt;
            }
            return null;
        }

        private void TrackPendingAccountDelete(AccountDeleteReceipt receipt)
        {
            Dictionary<string, AccountDeleteReceipt> pending;
            if (!_pendingAccountDeletes.TryGetValue(receipt.Sender, out pending))
                _pendingAccountDeletes.Add(receipt.Sender, pending = new Dictionary<string, AccountDeleteReceipt>(StringComparer.Ordinal));
            if (!pending.ContainsKey(receipt.Hash)) pending.Add(receipt.Hash, receipt);
        }

        private async Task ResolvePendingAccountDeletesAsync(string sender, CancellationToken token)
        {
            AccountDeleteReceipt previous;
            while ((previous = FindPendingAccountDelete(sender)) != null)
            {
                var state = await GetAccountDeleteAsync(previous, token).ConfigureAwait(false);
                if (!IsFinal(state.Status))
                {
                    lock (_receiptLookupLock)
                    {
                        Dictionary<string, AccountDeleteReceipt> pending;
                        if (_pendingAccountDeletes.TryGetValue(sender, out pending) && pending.ContainsKey(previous.Hash))
                            throw new PendingAccountDeleteException(previous);
                    }
                }
            }
        }
    }
}
