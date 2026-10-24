using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class NetworkTransportTests
    {
        [TestMethod]
        public async Task RpcUsesV2WithoutMutatingArgumentsOrOwningInjectedHttp()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => RpcHandler.Json("{'value':1}") };
            using (var http = new HttpClient(handler))
            {
                var options = RpcHandler.Options();
                var args = new JObject { ["account"] = TestTransactions.Account };
                using (var client = new XrplClient(options, http))
                {
                    options.RpcEndpoint = null;
                    Assert.AreEqual(0, handler.Requests.Count);
                    Assert.AreEqual(1, (int)(await client.RpcAsync("account_info", args, CancellationToken.None))["value"]);
                    Assert.IsNull(args["api_version"]);
                    Assert.AreEqual(2, (int)handler.Requests[0]["params"][0]["api_version"]);
                }
                Assert.IsFalse(handler.Disposed);
            }
        }

        [TestMethod]
        public async Task RpcErrorsRemainDistinctFromTransportErrors()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => RpcHandler.Json("{'error':'actNotFound','status':'error'}") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var ex = await Assert.ThrowsExceptionAsync<XrplRpcException>(() => client.RpcAsync("account_info", null, CancellationToken.None));
                Assert.AreEqual("actNotFound", ex.Code);
                handler.Respond = (m, p, ct) => throw new HttpRequestException("offline");
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.RpcAsync("account_info", null, CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task RequestsCanBeCancelledAndDisposedClientsCannotSend()
        {
            var handler = new RpcHandler { Respond = async (m, p, ct) => { await Task.Delay(10000, ct); return new JObject(); } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                var request = client.RpcAsync("account_info", null, cancel.Token);
                cancel.Cancel();
                try { await request; Assert.Fail("Request was not cancelled."); } catch (OperationCanceledException) { }
                client.Dispose();
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.RpcAsync("account_info", null, CancellationToken.None));
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
