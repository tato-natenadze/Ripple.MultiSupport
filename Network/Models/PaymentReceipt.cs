using System;
using Newtonsoft.Json;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;

namespace Ripple
{
    /// <summary>Secret-free recovery data. Persist before broadcast; JSON serialization is supported.</summary>
    public sealed class PaymentReceipt
    {
        public string Hash { get; }
        public string Sender { get; }
        public uint Sequence { get; }
        public uint FirstLedger { get; }
        public uint LastLedger { get; }
        public string TxBlob { get; }
        public Uri RpcEndpoint { get; }

        [JsonConstructor]
        public PaymentReceipt(string hash, string sender, uint sequence, uint firstLedger, uint lastLedger, string txBlob, Uri rpcEndpoint)
        {
            if (rpcEndpoint == null || !rpcEndpoint.IsAbsoluteUri || (rpcEndpoint.Scheme != "https" && rpcEndpoint.Scheme != "http"))
                throw new ArgumentException("A valid RPC endpoint is required.", nameof(rpcEndpoint));
            if (firstLedger == 0 || firstLedger > lastLedger || lastLedger - firstLedger > 1000)
                throw new ArgumentException("Invalid receipt ledger range.");
            if (!RippleClient.ValidateAddress(sender)) throw new ArgumentException("Invalid receipt sender.", nameof(sender));
            if (string.IsNullOrEmpty(txBlob) || txBlob.Length > 65536) throw new ArgumentException("Invalid signed transaction.", nameof(txBlob));
            try
            {
                var parsed = StObject.FromHex(txBlob);
                string actualHash = Utils.TransactionId(B16.Decode(txBlob));
                if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase) ||
                    (uint)parsed[Field.Sequence] != sequence || (uint)parsed[Field.LastLedgerSequence] != lastLedger ||
                    parsed[Field.Account].ToString() != sender || parsed[Field.TransactionType] != TransactionType.Payment)
                    throw new ArgumentException("Receipt does not match its signed payment.");
                Hash = actualHash;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is System.Collections.Generic.KeyNotFoundException || ex is IndexOutOfRangeException)
            { throw new ArgumentException("Receipt does not contain a matching signed payment.", nameof(txBlob)); }
            Sender = sender; Sequence = sequence; FirstLedger = firstLedger; LastLedger = lastLedger;
            TxBlob = txBlob; RpcEndpoint = rpcEndpoint;
        }
    }

    public sealed class PaymentResult
    {
        public PaymentReceipt Receipt { get; internal set; }
        public TransactionInfo Transaction { get; internal set; }
        public TransactionStatus Status => Transaction.Status;
        public string SubmissionResultCode { get; internal set; }
        /// <summary>Explains an indeterminate submission/wait without claiming the payment failed.</summary>
        public string Diagnostic { get; internal set; }
    }

    public sealed class PendingPaymentException : InvalidOperationException
    {
        public PaymentReceipt Receipt { get; }
        internal PendingPaymentException(PaymentReceipt receipt)
            : base("Resolve the previous payment before sending another from this account: " + receipt.Hash) { Receipt = receipt; }
    }
}
