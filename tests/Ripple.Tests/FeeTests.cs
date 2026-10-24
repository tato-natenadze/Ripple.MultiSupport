using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class FeeTests
    {
        [DataTestMethod]
        [DataRow("10", "12", "0.000012")]
        [DataRow("12", "10", "0.000012")]
        [DataRow("1", "1", "0.000001")]
        [DataRow("10", "123456789", "123.456789")]
        public async Task EstimateUsesExactXrpUnitsAndOnlyReadsTheFee(string baseFee, string openFee, string expected)
        {
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(new JObject {
                ["drops"] = new JObject { ["base_fee"] = baseFee, ["open_ledger_fee"] = openFee } }) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), await client.GetFeeAsync());
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.AreEqual("fee", (string)handler.Requests.Single()["method"]);
            Assert.AreEqual(2, (int)handler.Requests.Single()["params"][0]["api_version"]);
        }

        [DataTestMethod]
        [DataRow("{}")]
        [DataRow("{'drops':null}")]
        [DataRow("{'drops':{'base_fee':'10'}}")]
        [DataRow("{'drops':{'open_ledger_fee':'10'}}")]
        [DataRow("{'drops':{'base_fee':'0','open_ledger_fee':'0'}}")]
        [DataRow("{'drops':{'base_fee':'10','open_ledger_fee':'-1'}}")]
        [DataRow("{'drops':{'base_fee':'10','open_ledger_fee':'1.5'}}")]
        [DataRow("{'drops':{'base_fee':'10','open_ledger_fee':'1e2'}}")]
        [DataRow("{'drops':{'base_fee':'10','open_ledger_fee':'100000000000000001'}}")]
        public async Task InvalidFeeDataCannotProduceAnEstimate(string response)
        {
            var handler = new RpcHandler { Respond = (method, args, token) => RpcHandler.Json(response) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetFeeAsync());
            Assert.IsTrue(handler.Requests.All(request => (string)request["method"] == "fee"));
        }

        [TestMethod]
        public async Task AnEstimateAboveTheLimitDoesNotRelaxTheSendLimit()
        {
            var server = new PaymentTests.Server { Fee = "101" };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(0.000101m, await client.GetFeeAsync());
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m }));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task SendingRecalculatesTheFeeInsteadOfUsingAnEarlierEstimate()
        {
            var server = new PaymentTests.Server { Fee = "12" };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(0.000012m, await client.GetFeeAsync());
                Assert.IsFalse(server.Submitted);
                server.Fee = "24";
                var payment = await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m });
                Assert.AreEqual(TransactionStatus.Succeeded, payment.Status);
                Assert.AreEqual("24", (string)server.Signed.Single()["Fee"]);
                Assert.AreEqual(0.000024m, payment.Transaction.FeeXrp);
                Assert.AreEqual(1, handler.Requests.Count(request => (string)request["method"] == "submit"));
            }
        }

        [TestMethod]
        public async Task FeeLookupPreservesRpcAndTransportErrors()
        {
            var handler = new RpcHandler { Respond = (method, args, token) => RpcHandler.Json("{'error':'tooBusy','status':'error'}") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var rpc = await Assert.ThrowsExceptionAsync<XrplRpcException>(() => client.GetFeeAsync());
                Assert.AreEqual("fee", rpc.Method);
                Assert.AreEqual("tooBusy", rpc.Code);
                handler.Respond = (method, args, token) => throw new HttpRequestException("offline");
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetFeeAsync());
            }
        }

        [TestMethod]
        public async Task FeeLookupCanBeCancelledAndCannotUseADisposedClient()
        {
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return new JObject(); } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                var lookup = client.GetFeeAsync(cancel.Token);
                cancel.Cancel();
                try { await lookup; Assert.Fail("Fee lookup ignored cancellation."); }
                catch (OperationCanceledException) { }
                client.Dispose();
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.GetFeeAsync());
            }
            Assert.AreEqual(1, handler.Requests.Count);
        }
    }
}
