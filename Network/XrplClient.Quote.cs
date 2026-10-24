using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        /// <summary>Checks a direct XRP payment and estimates its debit and remaining balance without a seed, signing or submission.</summary>
        public async Task<PaymentQuote> GetPaymentQuoteAsync(string senderAddress, XrpPaymentRequest payment,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            if (payment == null) throw new ArgumentNullException(nameof(payment));
            string destination = payment.Destination;
            decimal amount = payment.AmountXrp;
            uint? tag = payment.DestinationTag;
            string memo = payment.Memo;
            bool? destinationTestNetwork;
            destination = NormalizePaymentDestination(destination, ref tag, out destinationTestNetwork);
            ValidateClassicAddress(senderAddress, nameof(senderAddress));
            ValidateClassicAddress(destination, nameof(payment.Destination));
            XrpToDrops(amount, nameof(payment.AmountXrp));
            if (memo != null && new UTF8Encoding(false, true).GetByteCount(memo) > 1000)
                throw new ArgumentException("Memo exceeds 1000 UTF-8 bytes.", nameof(payment));
            if (senderAddress == destination)
                throw new ArgumentException("A direct XRP payment requires different sender and destination.", nameof(payment));
            var gate = _accountGates.GetOrAdd(senderAddress, _ => new SemaphoreSlim(1, 1));
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                await gate.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    await ResolvePendingPaymentsAsync(senderAddress, operation.Token).ConfigureAwait(false);
                    var state = await ReadPaymentPreflightAsync(senderAddress, destination, amount, tag, operation.Token, destinationTestNetwork).ConfigureAwait(false);
                    decimal debit = amount + state.Fee;
                    return new PaymentQuote { Sender = senderAddress, Destination = destination, DestinationTag = tag,
                        LedgerIndex = state.Ledger.Index, AmountXrp = amount, EstimatedFeeXrp = state.Fee,
                        EstimatedTotalDebitXrp = debit, SenderBalanceXrp = state.Source.BalanceXrp,
                        SenderReserveXrp = state.Source.EstimatedReserveXrp, SenderSpendableXrp = state.Source.EstimatedSpendableXrp,
                        EstimatedRemainingBalanceXrp = state.Source.BalanceXrp - debit,
                        EstimatedRemainingSpendableXrp = Math.Max(0m, state.Source.EstimatedSpendableXrp - debit),
                        EstimatedMaximumAmountXrp = decimal.Floor(Math.Max(0m, state.Source.EstimatedSpendableXrp - state.Fee) * 1000000m) / 1000000m,
                        DestinationExists = state.Target.Exists, DestinationActivationReserveXrp = state.Ledger.BaseReserve };
                }
                finally { gate.Release(); }
            }
        }

        private sealed class PaymentPreflight
        {
            internal LedgerSnapshot Ledger;
            internal AccountInfo Source, Target;
            internal decimal Fee;
            internal uint ObservedLedger;
        }

        private async Task ResolvePendingPaymentsAsync(string sender, CancellationToken token)
        {
            PaymentReceipt previous;
            while ((previous = FindPendingPayment(sender)) != null)
            {
                var state = await GetTransactionAsync(previous, token).ConfigureAwait(false);
                if (!IsFinal(state.Status))
                {
                    lock (_receiptLookupLock)
                    {
                        System.Collections.Generic.Dictionary<string, PaymentReceipt> pending;
                        if (_pendingPayments.TryGetValue(sender, out pending) && pending.ContainsKey(previous.Hash))
                            throw new PendingPaymentException(previous);
                    }
                }
            }
        }

        private async Task<PaymentPreflight> ReadPaymentPreflightAsync(string sender, string destination, decimal amount,
            uint? tag, CancellationToken token, bool? destinationTestNetwork = null)
        {
            var ledger = await GetPaymentLedgerAsync(destinationTestNetwork, token).ConfigureAwait(false);
            var source = await ReadAccountAsync(sender, ledger, token).ConfigureAwait(false);
            if (!source.Exists) throw new InvalidOperationException("Sender account is not funded.");
            if (source.MasterKeyDisabled) throw new InvalidOperationException("Sender master key is disabled. The high-level payment method requires the master seed.");
            var target = await ReadAccountAsync(destination, ledger, token).ConfigureAwait(false);
            if (target.RequiresDestinationTag && !tag.HasValue) throw new ArgumentException("Destination requires a DestinationTag.", "payment");
            if (!target.Exists && amount < ledger.BaseReserve) throw new ArgumentException("Payment is below the destination account activation reserve.", "payment");
            if (target.DepositAuthorization && !(target.BalanceXrp <= ledger.BaseReserve && amount <= ledger.BaseReserve))
            {
                var authorization = await RpcAsync("deposit_authorized", new JObject { ["source_account"] = sender,
                    ["destination_account"] = destination, ["ledger_index"] = ledger.Index }, token).ConfigureAwait(false);
                var authorized = authorization["deposit_authorized"];
                if (authorized?.Type != JTokenType.Boolean)
                    throw new InvalidDataException("Invalid deposit authorization response.");
                if (!(bool)authorized) throw new InvalidOperationException("Destination has not authorized this sender.");
            }
            var feeResult = await RpcAsync("fee", null, token).ConfigureAwait(false);
            decimal fee = ReadPaymentFee(feeResult);
            if (fee <= 0 || fee > _options.MaxFeeXrp) throw new InvalidOperationException("Network fee exceeds the configured limit or is invalid.");
            if (amount + fee > source.EstimatedSpendableXrp) throw new InvalidOperationException("Insufficient spendable XRP after reserve and fee.");
            var current = await RpcAsync("account_info", new JObject { ["account"] = sender, ["ledger_index"] = "current", ["queue"] = true }, token).ConfigureAwait(false);
            var currentData = current["account_data"] as JObject;
            if (currentData == null || (string)currentData["Account"] != sender)
                throw new InvalidDataException("Server returned different current account data.");
            var queue = current["queue_data"];
            if (queue != null && !(queue is JObject)) throw new InvalidDataException("Invalid account queue data.");
            uint queued = ResponseUInt(queue?["txn_count"], "queue_data.txn_count") ?? 0;
            uint currentSequence = RequiredUInt(currentData, "Sequence");
            if (currentSequence != source.Sequence || queued > 0)
                throw new InvalidOperationException("Sender has pending/external transactions (validated Sequence=" + source.Sequence +
                    ", current Sequence=" + currentSequence + ", queued=" + queued + "). Wait for validation before sending.");
            uint observedLedger = Math.Max(ledger.Index, ResponseUInt(current["ledger_current_index"], "ledger_current_index") ?? ledger.Index);
            observedLedger = Math.Max(observedLedger, ResponseUInt(feeResult["ledger_current_index"], "ledger_current_index") ?? ledger.Index);
            return new PaymentPreflight { Ledger = ledger, Source = source, Target = target, Fee = fee, ObservedLedger = observedLedger };
        }
    }
}
