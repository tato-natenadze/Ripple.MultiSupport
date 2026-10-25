using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;

namespace Ripple
{
    public sealed class AccountDeleteRequest
    {
        public string Destination { get; set; }
        public uint? DestinationTag { get; set; }
        /// <summary>Separate from the normal payment fee limit. XRP, with at most six decimal places.</summary>
        public decimal MaxFeeXrp { get; set; } = 0.2m;
        /// <summary>Persist this seed-free receipt before submission. Failure prevents submission.</summary>
        public Func<AccountDeleteReceipt, CancellationToken, Task> OnPreparedAsync { get; set; }
    }

    /// <summary>A read-only snapshot. Conditions and balances are checked again before deletion.</summary>
    public sealed class AccountDeleteQuote
    {
        public string Sender { get; internal set; }
        public string Destination { get; internal set; }
        public uint? DestinationTag { get; internal set; }
        public uint LedgerIndex { get; internal set; }
        public uint ObservedLedgerIndex { get; internal set; }
        public uint Sequence { get; internal set; }
        public ulong EarliestLedgerIndex { get; internal set; }
        public ulong LedgersUntilEligible { get; internal set; }
        public decimal BalanceXrp { get; internal set; }
        public decimal EstimatedReserveXrp { get; internal set; }
        public decimal EstimatedFeeXrp { get; internal set; }
        /// <summary>The whole balance minus the deletion fee, including releasable reserve.</summary>
        public decimal EstimatedReturnedXrp { get; internal set; }
        public decimal MaxFeeXrp { get; internal set; }
        public IReadOnlyList<string> BlockingReasons { get; internal set; } = Array.Empty<string>();
        public bool CanDelete => BlockingReasons.Count == 0;
    }

    /// <summary>Seed-free recovery data for AccountDelete. The signed blob is bound to its hash, account and ledger range.</summary>
    public sealed class AccountDeleteReceipt
    {
        public string Hash { get; }
        public string Sender { get; }
        public string Destination { get; }
        public uint? DestinationTag { get; }
        public decimal FeeXrp { get; }
        public uint Sequence { get; }
        public uint FirstLedger { get; }
        public uint LastLedger { get; }
        public string TxBlob { get; }
        public Uri RpcEndpoint { get; }

        [JsonConstructor]
        public AccountDeleteReceipt(string hash, string sender, uint sequence, uint firstLedger, uint lastLedger,
            string txBlob, Uri rpcEndpoint)
        {
            if (rpcEndpoint == null || !rpcEndpoint.IsAbsoluteUri || (rpcEndpoint.Scheme != "https" && rpcEndpoint.Scheme != "http"))
                throw new ArgumentException("A valid RPC endpoint is required.", nameof(rpcEndpoint));
            if (firstLedger == 0 || firstLedger > lastLedger || lastLedger - firstLedger > 1000)
                throw new ArgumentException("Invalid receipt ledger range.");
            if (!RippleClient.ValidateAddress(sender)) throw new ArgumentException("Invalid receipt sender.", nameof(sender));
            if (string.IsNullOrEmpty(txBlob) || txBlob.Length > 65536)
                throw new ArgumentException("Invalid signed transaction.", nameof(txBlob));
            try
            {
                var parsed = StObject.FromHex(txBlob);
                TxFormat.Validate(parsed);
                string actualHash = Utils.TransactionId(B16.Decode(txBlob));
                if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase) ||
                    parsed[Field.TransactionType] != TransactionType.AccountDelete ||
                    parsed[Field.Account].ToString() != sender || (uint)parsed[Field.Sequence] != sequence ||
                    (uint)parsed[Field.LastLedgerSequence] != lastLedger ||
                    !parsed.Has(Field.TxnSignature) || parsed[Field.TxnSignature] == null ||
                    string.IsNullOrEmpty((string)parsed.ToJsonObject()["TxnSignature"]))
                    throw new ArgumentException("Receipt does not match its signed AccountDelete transaction.");
                var fee = parsed[Field.Fee];
                if (!fee.IsNative || fee.DecimalValue() < 0)
                    throw new ArgumentException("Invalid AccountDelete fee.");
                Hash = actualHash;
                Destination = parsed[Field.Destination].ToString();
                DestinationTag = parsed.Has(Field.DestinationTag) ? (uint?)parsed[Field.DestinationTag] : null;
                FeeXrp = fee.DecimalValue() / 1000000m;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is KeyNotFoundException ||
                ex is IndexOutOfRangeException || ex is InvalidOperationException || ex is InvalidCastException || ex is OverflowException)
            { throw new ArgumentException("Receipt does not contain a matching signed AccountDelete transaction.", nameof(txBlob)); }
            Sender = sender; Sequence = sequence; FirstLedger = firstLedger; LastLedger = lastLedger;
            TxBlob = txBlob; RpcEndpoint = rpcEndpoint;
        }
    }

    public sealed class AccountDeleteResult
    {
        public AccountDeleteReceipt Receipt { get; internal set; }
        public TransactionInfo Transaction { get; internal set; } = new TransactionInfo { Status = TransactionStatus.Unknown };
        public TransactionStatus Status => Transaction.Status;
        /// <summary>Actual validated XRP credit to the destination. Null until a successful deletion is proven.</summary>
        public decimal? ReturnedAmountXrp { get; internal set; }
        public string SubmissionResultCode { get; internal set; }
        public string Diagnostic { get; internal set; }
        public PaymentError Error { get; internal set; }
    }

    public sealed class PendingAccountDeleteException : InvalidOperationException
    {
        public AccountDeleteReceipt Receipt { get; }
        internal PendingAccountDeleteException(AccountDeleteReceipt receipt)
            : base("Resolve the previous AccountDelete before sending another transaction from this account: " + receipt.Hash)
        { Receipt = receipt; }
    }
}
