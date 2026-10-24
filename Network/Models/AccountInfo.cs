namespace Ripple
{
    public sealed class AccountInfo
    {
        public string Address { get; internal set; }
        public bool Exists { get; internal set; }
        public uint LedgerIndex { get; internal set; }
        public decimal BalanceXrp { get; internal set; }
        public decimal EstimatedReserveXrp { get; internal set; }
        /// <summary>Estimate before fees and pending transactions; the ledger makes the final decision.</summary>
        public decimal EstimatedSpendableXrp { get; internal set; }
        public uint Sequence { get; internal set; }
        public uint OwnerCount { get; internal set; }
        public bool RequiresDestinationTag { get; internal set; }
        public bool DepositAuthorization { get; internal set; }
        public bool MasterKeyDisabled { get; internal set; }
        public string RegularKey { get; internal set; }
    }
}
