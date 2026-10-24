using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ripple
{
    public sealed class PaymentWatchOptions
    {
        /// <summary>Inclusive first ledger to replay. If omitted, starts after the validated ledger at invocation.</summary>
        public uint? FromLedger { get; set; }
        /// <summary>Previously saved opaque checkpoint. Bound to the account and RPC endpoint.</summary>
        public string Checkpoint { get; set; }
        /// <summary>Called after all payment callbacks in a ledger batch succeed. Persist for restart recovery.
        /// Delivery is at least once: deduplicate credits by transaction hash in your database.</summary>
        public Func<string, CancellationToken, Task> SaveCheckpointAsync { get; set; }
    }
}
