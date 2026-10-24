using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountGates = new ConcurrentDictionary<string, SemaphoreSlim>();
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
            ValidateClassicAddress(destination, nameof(payment.Destination));
            string amountDrops = XrpToDrops(amount, nameof(payment.AmountXrp));
            byte[] memoBytes = memo == null ? null : new UTF8Encoding(false, true).GetBytes(memo);
            if (memoBytes != null && memoBytes.Length > 1000) throw new ArgumentException("Memo exceeds 1000 UTF-8 bytes.", nameof(payment));
            string sender;
            try { sender = Seed.FromBase58(seed).KeyPair().Id(); }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            { throw new ArgumentException("Invalid XRPL seed.", nameof(seed)); }
            if (sender == destination) throw new ArgumentException("A direct XRP payment requires different sender and destination.", nameof(payment));
            var gate = _accountGates.GetOrAdd(sender, _ => new SemaphoreSlim(1, 1));
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                await gate.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    PaymentReceipt previous;
                    while ((previous = FindPendingPayment(sender)) != null)
                    {
                        var state = await GetTransactionAsync(previous, operation.Token).ConfigureAwait(false);
                        if (!IsFinal(state.Status)) throw new PendingPaymentException(previous);
                    }
                    var ledger = await GetLedgerAsync(operation.Token).ConfigureAwait(false);
                    var source = await ReadAccountAsync(sender, ledger, operation.Token).ConfigureAwait(false);
                    if (!source.Exists) throw new InvalidOperationException("Sender account is not funded.");
                    if (source.MasterKeyDisabled) throw new InvalidOperationException("Sender master key is disabled. The high-level payment method requires the master seed.");
                    var target = await ReadAccountAsync(destination, ledger, operation.Token).ConfigureAwait(false);
                    if (target.RequiresDestinationTag && !tag.HasValue) throw new ArgumentException("Destination requires a DestinationTag.", nameof(payment));
                    if (!target.Exists && amount < ledger.BaseReserve) throw new ArgumentException("Payment is below the destination account activation reserve.", nameof(payment));
                    if (target.DepositAuthorization && !(target.BalanceXrp <= ledger.BaseReserve && amount <= ledger.BaseReserve))
                    {
                        var authorization = await RpcAsync("deposit_authorized", new JObject { ["source_account"] = sender,
                            ["destination_account"] = destination, ["ledger_index"] = ledger.Index }, operation.Token).ConfigureAwait(false);
                        if ((bool?)authorization["deposit_authorized"] != true)
                            throw new InvalidOperationException("Destination has not authorized this sender.");
                    }
                    var feeResult = await RpcAsync("fee", null, operation.Token).ConfigureAwait(false);
                    var fees = feeResult["drops"] as JObject ?? throw new InvalidDataException("Missing fee data.");
                    decimal fee = Math.Max(DropsToXrp(fees["open_ledger_fee"]), DropsToXrp(fees["base_fee"]));
                    if (fee <= 0 || fee > _options.MaxFeeXrp) throw new InvalidOperationException("Network fee exceeds the configured limit or is invalid.");
                    if (amount + fee > source.EstimatedSpendableXrp) throw new InvalidOperationException("Insufficient spendable XRP after reserve and fee.");
                    // Refuse to guess around payments submitted by another client/process.
                    var current = await RpcAsync("account_info", new JObject { ["account"] = sender, ["ledger_index"] = "current", ["queue"] = true }, operation.Token).ConfigureAwait(false);
                    var currentData = current["account_data"] as JObject;
                    if (currentData == null || (string)currentData["Account"] != sender)
                        throw new InvalidDataException("Server returned different current account data.");
                    if (RequiredUInt(currentData, "Sequence") != source.Sequence || (uint?)current["queue_data"]?["txn_count"] > 0)
                        throw new InvalidOperationException("Sender has pending/external transactions (validated Sequence=" + source.Sequence +
                            ", current Sequence=" + RequiredUInt(currentData, "Sequence") + ", queued=" + ((uint?)current["queue_data"]?["txn_count"] ?? 0) +
                            "). Wait for validation before sending.");
                    // Cluster members can have different tips. Do not base expiry on an older
                    // validated snapshot after a newer current/fee ledger was already observed.
                    uint observedLedger = Math.Max(ledger.Index, (uint?)current["ledger_current_index"] ?? ledger.Index);
                    observedLedger = Math.Max(observedLedger, (uint?)feeResult["ledger_current_index"] ?? ledger.Index);
                    uint lastLedger = checked(observedLedger + _options.LastLedgerOffset);
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
                finally { gate.Release(); }
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
