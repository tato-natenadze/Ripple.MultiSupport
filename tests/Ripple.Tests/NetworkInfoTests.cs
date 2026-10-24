using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class NetworkInfoTests
    {
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(1025)]
        public async Task NetworkIdentityAndReservesAreReadWithoutAccountOrSubmission(int networkId)
        {
            var result = RpcHandler.Ledger(456);
            result["info"]["network_id"] = networkId;
            result["info"]["validated_ledger"]["reserve_base_xrp"] = 2.5m;
            result["info"]["validated_ledger"]["reserve_inc_xrp"] = 0.125m;
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(result) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var network = await client.GetNetworkInfoAsync();
                Assert.AreEqual((uint)networkId, network.NetworkId);
                Assert.AreEqual(456u, network.ValidatedLedgerIndex);
                Assert.AreEqual(2.5m, network.BaseReserveXrp);
                Assert.AreEqual(0.125m, network.OwnerReserveXrp);
            }
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.AreEqual("server_info", (string)handler.Requests[0]["method"]);
        }

        [TestMethod]
        public async Task AnUnreportedNetworkIsNotGuessedAsMainnet()
        {
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(RpcHandler.Ledger()) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                Assert.IsNull((await client.GetNetworkInfoAsync()).NetworkId);
        }

        [DataTestMethod]
        [DataRow("-1")]
        [DataRow("4294967296")]
        [DataRow("1.5")]
        [DataRow("true")]
        [DataRow("\"1\"")]
        [DataRow("null")]
        public async Task InvalidNetworkIdsAreRejected(string networkJson)
        {
            var result = RpcHandler.Ledger();
            result["info"]["network_id"] = JToken.Parse(networkJson);
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(result) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetNetworkInfoAsync());
        }

        [TestMethod]
        public async Task StaleOrUnvalidatedReserveInformationIsRejected()
        {
            var result = RpcHandler.Ledger();
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(result) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                result["info"]["validated_ledger"]["age"] = 121;
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetNetworkInfoAsync());
                result["info"]["closed_ledger"] = result["info"]["validated_ledger"];
                ((JObject)result["info"]).Remove("validated_ledger");
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetNetworkInfoAsync());
            }
        }
    }
}
