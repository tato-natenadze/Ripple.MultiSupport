using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        /// <summary>
        /// Reads the current fee estimate, in XRP, for a normal single-signed XRP payment.
        /// Does not submit a transaction. SendXrpAsync reads the fee again and enforces MaxFeeXrp.
        /// </summary>
        public async Task<decimal> GetFeeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await RpcAsync("fee", null, cancellationToken).ConfigureAwait(false);
            decimal fee = ReadPaymentFee(result);
            if (fee <= 0) throw new InvalidDataException("The network returned an invalid payment fee.");
            return fee;
        }

        private static decimal ReadPaymentFee(JObject result)
        {
            var fees = result["drops"] as JObject ?? throw new InvalidDataException("Missing fee data.");
            return Math.Max(DropsToXrp(fees["open_ledger_fee"]), DropsToXrp(fees["base_fee"]));
        }
    }
}
