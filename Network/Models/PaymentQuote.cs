namespace Ripple
{
    /// <summary>A read-only estimate. It reserves no funds or sequence; SendXrpAsync checks the state again.</summary>
    public sealed class PaymentQuote
    {
        public string Sender { get; internal set; }
        public string Destination { get; internal set; }
        public uint? DestinationTag { get; internal set; }
        public uint LedgerIndex { get; internal set; }
        public decimal AmountXrp { get; internal set; }
        public decimal EstimatedFeeXrp { get; internal set; }
        public decimal EstimatedTotalDebitXrp { get; internal set; }
        public decimal SenderBalanceXrp { get; internal set; }
        public decimal SenderReserveXrp { get; internal set; }
        public decimal SenderSpendableXrp { get; internal set; }
        public decimal EstimatedRemainingBalanceXrp { get; internal set; }
        public decimal EstimatedRemainingSpendableXrp { get; internal set; }
        public decimal EstimatedMaximumAmountXrp { get; internal set; }
        public bool DestinationExists { get; internal set; }
        public decimal DestinationActivationReserveXrp { get; internal set; }
    }
}
