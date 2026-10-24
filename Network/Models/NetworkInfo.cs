namespace Ripple
{
    public sealed class NetworkInfo
    {
        /// <summary>The server's network ID, or null if the server does not report it.</summary>
        public uint? NetworkId { get; internal set; }
        public uint ValidatedLedgerIndex { get; internal set; }
        public decimal BaseReserveXrp { get; internal set; }
        public decimal OwnerReserveXrp { get; internal set; }
    }
}
