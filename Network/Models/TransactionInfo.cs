namespace Ripple
{
    public enum TransactionStatus { Unknown, Pending, Succeeded, Failed, Expired, Rejected }

    public sealed class TransactionInfo
    {
        public string Hash { get; internal set; }
        public bool Found { get; internal set; }
        public bool Validated { get; internal set; }
        public TransactionStatus Status { get; internal set; }
        public string ResultCode { get; internal set; }
        public string TransactionType { get; internal set; }
        public string Sender { get; internal set; }
        public string Destination { get; internal set; }
        public uint? DestinationTag { get; internal set; }
        public uint? LedgerIndex { get; internal set; }
        public uint? TransactionIndex { get; internal set; }
        public decimal? AmountXrp { get; internal set; }
        public decimal? DeliveredAmountXrp { get; internal set; }
        public decimal? FeeXrp { get; internal set; }
        public bool IsXrpPayment { get; internal set; }
        internal bool SearchedAll { get; set; }
    }
}
