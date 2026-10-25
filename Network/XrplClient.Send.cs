using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private readonly Dictionary<string, AccountGate> _accountGates = new Dictionary<string, AccountGate>(StringComparer.Ordinal);
        private sealed class AccountGate
        {
            internal readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            internal int Users;
        }

        private AccountGate RentAccountGate(string account)
        {
            lock (_accountGates)
            {
                AccountGate gate;
                if (!_accountGates.TryGetValue(account, out gate)) _accountGates.Add(account, gate = new AccountGate());
                gate.Users++;
                return gate;
            }
        }

        private void ReturnAccountGate(string account, AccountGate gate)
        {
            lock (_accountGates)
            {
                if (--gate.Users != 0) return;
                _accountGates.Remove(account);
                gate.Semaphore.Dispose();
            }
        }
        // The receipt lock protects both levels. Keep sender lookup independent
        // of the number of other accounts being recovered.
        private readonly Dictionary<string, Dictionary<string, PaymentReceipt>> _pendingPayments =
            new Dictionary<string, Dictionary<string, PaymentReceipt>>(StringComparer.Ordinal);
        private readonly object _receiptLookupLock = new object();
        private readonly Dictionary<string, ReceiptLookup> _receiptLookups = new Dictionary<string, ReceiptLookup>(StringComparer.Ordinal);
        private sealed class ReceiptLookup
        {
            internal int Readers;
            internal bool Finalized;
        }

        public async Task<PaymentResult> SendXrpAsync(string seed, XrpPaymentRequest payment,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            if (payment == null) throw new ArgumentNullException(nameof(payment));
            // Snapshot caller-owned input before the first await.
            string destination = payment.Destination;
            decimal amount = payment.AmountXrp;
            uint? tag = payment.DestinationTag;
            string memo = payment.Memo;
            var prepared = payment.OnPreparedAsync;
            bool? destinationTestNetwork;
            destination = NormalizePaymentDestination(destination, ref tag, out destinationTestNetwork);
            ValidateClassicAddress(destination, nameof(payment.Destination));
            string amountDrops = XrpToDrops(amount, nameof(payment.AmountXrp));
            var utf8 = new UTF8Encoding(false, true);
            if (memo != null && (memo.Length > 1000 || utf8.GetByteCount(memo) > 1000))
                throw new ArgumentException("Memo exceeds 1000 UTF-8 bytes.", nameof(payment));
            byte[] memoBytes = memo == null ? null : utf8.GetBytes(memo);
            string sender;
            try { sender = Seed.FromBase58(seed).KeyPair().Id(); }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            { throw new ArgumentException("Invalid XRPL seed.", nameof(seed)); }
            if (sender == destination) throw new ArgumentException("A direct XRP payment requires different sender and destination.", nameof(payment));
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                var gate = RentAccountGate(sender);
                bool entered = false;
                try
                {
                    await gate.Semaphore.WaitAsync(operation.Token).ConfigureAwait(false);
                    entered = true;
                    PaymentReceipt previous;
                    await ResolvePendingPaymentsAsync(sender, operation.Token).ConfigureAwait(false);
                    var preflight = await ReadPaymentPreflightAsync(sender, destination, amount, tag, operation.Token, destinationTestNetwork).ConfigureAwait(false);
                    var ledger = preflight.Ledger;
                    var source = preflight.Source;
                    decimal fee = preflight.Fee;
                    uint lastLedger = checked(preflight.ObservedLedger + _options.LastLedgerOffset);
                    var tx = new JObject { ["TransactionType"] = "Payment", ["Account"] = sender,
                        ["Destination"] = destination, ["Amount"] = amountDrops, ["Fee"] = XrpToDrops(fee, "fee"),
                        ["Sequence"] = source.Sequence, ["LastLedgerSequence"] = lastLedger };
                    if (tag.HasValue) tx["DestinationTag"] = tag.Value;
                    if (memoBytes != null) tx["Memos"] = new JArray(new JObject { ["Memo"] = new JObject { ["MemoData"] = B16.Encode(memoBytes) } });
                    var signed = TxSigner.SignJson(tx, seed);
                    var receipt = new PaymentReceipt(signed.Hash, sender, source.Sequence, checked(ledger.Index + 1), lastLedger, signed.TxBlob, _options.RpcEndpoint);
                    if (prepared != null) await prepared(receipt, operation.Token).ConfigureAwait(false);
                    operation.Token.ThrowIfCancellationRequested();
                    lock (_receiptLookupLock)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        previous = FindPendingPayment(sender);
                        if (previous != null) throw new PendingPaymentException(previous);
                        TrackPendingPayment(receipt);
                    }
                    var result = new PaymentResult { Receipt = receipt, Transaction = Unknown(receipt.Hash) };
                    string activeMethod = "submit";
                    using (var wait = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                    {
                    wait.CancelAfter(_options.ConfirmationTimeout);
                    try
                    {
                        var submission = await RpcAsync("submit", new JObject { ["tx_blob"] = signed.TxBlob }, wait.Token).ConfigureAwait(false);
                        result.SubmissionResultCode = (string)submission["engine_result"];
                        // Even tesSUCCESS here is provisional. Only validated metadata completes a payment.
                        var timer = Stopwatch.StartNew();
                        while (timer.Elapsed < _options.ConfirmationTimeout)
                        {
                            wait.Token.ThrowIfCancellationRequested();
                            activeMethod = "tx";
                            result.Transaction = await GetTransactionAsync(receipt, wait.Token).ConfigureAwait(false);
                            if (IsFinal(result.Status)) return result;
                            var remaining = _options.ConfirmationTimeout - timer.Elapsed;
                            if (remaining <= TimeSpan.Zero) break;
                            await Task.Delay(remaining < _options.PollInterval ? remaining : _options.PollInterval, wait.Token).ConfigureAwait(false);
                        }
                        result.Diagnostic = "Confirmation wait expired. Check the receipt before creating any replacement payment.";
                        result.Error = PaymentFailure(new OperationCanceledException(), activeMethod, seed, operation.IsCancellationRequested);
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException || ex is InvalidDataException ||
                        ex is XrplRpcException || ex is ArgumentException || ex is FormatException || ex is OverflowException ||
                        (ex is ObjectDisposedException && _lifetime.IsCancellationRequested))
                    {
                        result.Diagnostic = "Submission/confirmation is indeterminate (" + ex.GetType().Name + "). Query the saved receipt; do not create a replacement payment.";
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

        /// <summary>Recover a saved payment's status, including proven expiry. Does not submit anything.</summary>
        public async Task<TransactionInfo> GetTransactionAsync(PaymentReceipt receipt, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            if (receipt.RpcEndpoint != _options.RpcEndpoint) throw new ArgumentException("Receipt belongs to a different RPC endpoint.", nameof(receipt));
            ReceiptLookup lookup;
            lock (_receiptLookupLock)
            {
                if (!_receiptLookups.TryGetValue(receipt.Hash, out lookup)) _receiptLookups.Add(receipt.Hash, lookup = new ReceiptLookup());
                lookup.Readers++;
            }
            try
            {
                var tx = await ReadTransactionAsync(receipt.Hash, receipt.FirstLedger, receipt.LastLedger, cancellationToken, receipt).ConfigureAwait(false);
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
                        Dictionary<string, PaymentReceipt> pending;
                        if (_pendingPayments.TryGetValue(receipt.Sender, out pending))
                        {
                            pending.Remove(receipt.Hash);
                            if (pending.Count == 0) _pendingPayments.Remove(receipt.Sender);
                        }
                    }
                    else if (!lookup.Finalized) TrackPendingPayment(receipt);
                }
                // A hash-only lookup cannot prove expiry. Keep uncertain transactions blocking the sender lane.
                return tx;
            }
            finally
            {
                lock (_receiptLookupLock)
                {
                    // Keep only the hash/finality flag for the client lifetime.
                    // Later missing history must not reopen a finalized payment.
                    if (--lookup.Readers == 0 && !lookup.Finalized) _receiptLookups.Remove(receipt.Hash);
                }
            }
        }

        private PaymentReceipt FindPendingPayment(string sender)
        {
            lock (_receiptLookupLock)
            {
                Dictionary<string, PaymentReceipt> pending;
                if (_pendingPayments.TryGetValue(sender, out pending))
                    foreach (var receipt in pending.Values) return receipt;
            }
            return null;
        }

        // Caller holds _receiptLookupLock, including the reservation check.
        private void TrackPendingPayment(PaymentReceipt receipt)
        {
            Dictionary<string, PaymentReceipt> pending;
            if (!_pendingPayments.TryGetValue(receipt.Sender, out pending))
                _pendingPayments.Add(receipt.Sender, pending = new Dictionary<string, PaymentReceipt>(StringComparer.Ordinal));
            if (!pending.ContainsKey(receipt.Hash)) pending.Add(receipt.Hash, receipt);
        }

        private static bool IsFinal(TransactionStatus state) => state == TransactionStatus.Succeeded || state == TransactionStatus.Failed ||
            state == TransactionStatus.Expired || state == TransactionStatus.Rejected;
        private static TransactionInfo Unknown(string hash) => new TransactionInfo { Hash = hash, Status = TransactionStatus.Unknown };
        private static string XrpToDrops(decimal value, string name)
        {
            if (value <= 0 || value > 100000000000m || value * 1000000m != decimal.Truncate(value * 1000000m))
                throw new ArgumentOutOfRangeException(name, "XRP must be positive, in range and have at most six decimal places.");
            return (value * 1000000m).ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
