using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        /// <summary>Reads the network identity and XRP reserves from a current validated ledger.</summary>
        public async Task<NetworkInfo> GetNetworkInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await RpcAsync("server_info", null, cancellationToken).ConfigureAwait(false);
            var ledger = ReadValidatedLedger(result);
            var network = result["info"]["network_id"];
            uint? networkId = null;
            if (network != null)
            {
                if (network.Type != JTokenType.Integer || !uint.TryParse(network.ToString(),
                    System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint value))
                    throw new InvalidDataException("Invalid network ID.");
                networkId = value;
            }
            return new NetworkInfo { NetworkId = networkId, ValidatedLedgerIndex = ledger.Index,
                BaseReserveXrp = ledger.BaseReserve, OwnerReserveXrp = ledger.OwnerReserve };
        }
    }
}
