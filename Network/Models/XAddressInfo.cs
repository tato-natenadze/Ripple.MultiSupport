namespace Ripple
{
    public sealed class XAddressInfo
    {
        public string ClassicAddress { get; internal set; }
        /// <summary>Null means no tag; zero is a valid, distinct tag.</summary>
        public uint? DestinationTag { get; internal set; }
        public bool IsTestNetwork { get; internal set; }
    }
}
