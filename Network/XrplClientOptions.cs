using System;

namespace Ripple
{
    /// <summary>Explicit server endpoints and limits for an XRPL connection.</summary>
    public sealed class XrplClientOptions
    {
        public Uri RpcEndpoint { get; set; }
        public Uri WebSocketEndpoint { get; set; }
        public decimal MaxFeeXrp { get; set; } = 0.0001m;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan ConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
        public uint LastLedgerOffset { get; set; } = 4;

        internal XrplClientOptions Snapshot()
        {
            if (RpcEndpoint == null || !RpcEndpoint.IsAbsoluteUri ||
                (RpcEndpoint.Scheme != "https" && RpcEndpoint.Scheme != "http"))
                throw new ArgumentException("An absolute HTTP(S) RPC endpoint is required.", nameof(RpcEndpoint));
            if (WebSocketEndpoint != null && (!WebSocketEndpoint.IsAbsoluteUri ||
                (WebSocketEndpoint.Scheme != "wss" && WebSocketEndpoint.Scheme != "ws")))
                throw new ArgumentException("The WebSocket endpoint must use WS(S).", nameof(WebSocketEndpoint));
            ValidateTimeout(RequestTimeout, nameof(RequestTimeout));
            ValidateTimeout(ConfirmationTimeout, nameof(ConfirmationTimeout));
            ValidateTimeout(PollInterval, nameof(PollInterval));
            if (MaxFeeXrp <= 0 || MaxFeeXrp > 100000000000m || decimal.Truncate(MaxFeeXrp * 1000000m) != MaxFeeXrp * 1000000m)
                throw new ArgumentOutOfRangeException(nameof(MaxFeeXrp));
            if (LastLedgerOffset < 2 || LastLedgerOffset > 1000)
                throw new ArgumentOutOfRangeException(nameof(LastLedgerOffset));
            return (XrplClientOptions)MemberwiseClone();
        }

        private static void ValidateTimeout(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
