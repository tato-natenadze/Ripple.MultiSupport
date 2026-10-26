using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    [TestClass]
    public class NetworkTransportTests
    {
        [TestMethod]
        public async Task PublicHistoryUsesV2WithoutMutatingRequestsOrOwningInjectedHttp()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) =>
                Task.FromResult(RpcHandler.History(TestTransactions.Account, 100, 200)) };
            using (var http = new HttpClient(handler))
            {
                var options = RpcHandler.Options();
                var request = new PaymentHistoryRequest { FromLedger = 100, ToLedger = 200, Limit = 20 };
                using (var client = new XrplClient(options, http))
                {
                    options.RpcEndpoint = null;
                    Assert.AreEqual(0, handler.Requests.Count);
                    var page = await client.GetPaymentsAsync(TestTransactions.Account, request);
                    Assert.AreEqual(0, page.Payments.Count);
                    Assert.IsTrue(page.RequestedRangeAvailable);
                    Assert.AreEqual(100u, request.FromLedger);
                    Assert.AreEqual(200u, request.ToLedger);
                    Assert.AreEqual(20, request.Limit);
                    Assert.IsNull(request.ContinuationToken);
                    Assert.AreEqual(1, handler.Requests.Count);
                    Assert.AreEqual("account_tx", (string)handler.Requests[0]["method"]);
                    Assert.AreEqual(2, (int)handler.Requests[0]["params"][0]["api_version"]);
                }
                Assert.IsFalse(handler.Disposed);
            }
        }

        [TestMethod]
        public void InvalidConfigurationIsRejectedBeforeNetworkAccess()
        {
            Assert.ThrowsException<ArgumentException>(() => new XrplClient(new XrplClientOptions()));
            var options = RpcHandler.Options(); options.MaxFeeXrp = 0.0000001m;
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new XrplClient(options));
        }
    }
}
