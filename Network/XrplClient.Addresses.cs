using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private static string NormalizePaymentDestination(string destination, ref uint? tag, out bool? testNetwork)
        {
            testNetwork = null;
            if (string.IsNullOrEmpty(destination) || (destination[0] != 'X' && destination[0] != 'T')) return destination;
            var address = XAddressCodec.Decode(destination);
            if (tag.HasValue && tag != address.DestinationTag)
                throw new ArgumentException("DestinationTag conflicts with the X-address.", "payment");
            tag = address.DestinationTag;
            testNetwork = address.IsTestNetwork;
            return address.ClassicAddress;
        }

        private async Task<LedgerSnapshot> GetPaymentLedgerAsync(bool? destinationTestNetwork, CancellationToken token)
        {
            if (!destinationTestNetwork.HasValue) return await GetLedgerAsync(token).ConfigureAwait(false);
            var network = await GetNetworkInfoAsync(token).ConfigureAwait(false);
            if (!network.NetworkId.HasValue || network.NetworkId > 2)
                throw new InvalidDataException("The server did not report a supported Mainnet, Testnet or Devnet identity for this X-address.");
            bool test = network.NetworkId.Value != 0;
            if (test != destinationTestNetwork.Value)
                throw new ArgumentException("The X-address network does not match the connected network.", "payment");
            return new LedgerSnapshot { Index = network.ValidatedLedgerIndex,
                BaseReserve = network.BaseReserveXrp, OwnerReserve = network.OwnerReserveXrp };
        }
    }
}
