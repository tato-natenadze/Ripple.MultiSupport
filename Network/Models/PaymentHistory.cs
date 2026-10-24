using System.Collections.Generic;

namespace Ripple
{
    public sealed class PaymentHistoryRequest
    {
        public uint? FromLedger { get; set; }
        public uint? ToLedger { get; set; }
        public int Limit { get; set; } = 200;
        /// <summary>Pass back the previous page's token unchanged. The token pins the ledger range.</summary>
        public string ContinuationToken { get; set; }
    }

    public sealed class PaymentHistoryPage
    {
        public IReadOnlyList<TransactionInfo> Payments { get; internal set; }
        public string ContinuationToken { get; internal set; }
        public uint FromLedger { get; internal set; }
        public uint ToLedger { get; internal set; }
        /// <summary>False if the server searched a narrower ledger range than explicitly requested.</summary>
        public bool RequestedRangeAvailable { get; internal set; }
    }
}
