using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ripple
{
    public sealed class XrpPaymentRequest
    {
        public string Destination { get; set; }
        public decimal AmountXrp { get; set; }
        public uint? DestinationTag { get; set; }
        /// <summary>Optional UTF-8 memo, at most 1000 bytes.</summary>
        public string Memo { get; set; }
        /// <summary>Called BEFORE broadcast. Persist the receipt here for recovery after process failure.
        /// If this callback fails, nothing is submitted. The callback must not re-enter SendXrpAsync.</summary>
        public Func<PaymentReceipt, CancellationToken, Task> OnPreparedAsync { get; set; }
    }
}
