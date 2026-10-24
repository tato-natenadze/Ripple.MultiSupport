using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentErrorTests
    {
        private static XrpPaymentRequest Payment() => new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
        private static JObject Error(PaymentResult result)
        {
            Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
            var serializer = new JsonSerializer(); serializer.Converters.Add(new StringEnumConverter());
            var serialized = JObject.FromObject(result, serializer);
            Assert.IsFalse(serialized.ToString().Contains(TestTransactions.SecpSeed));
            var error = serialized["Error"] as JObject; Assert.IsNotNull(error, "Structured error must be returned."); return error;
        }

        [DataTestMethod]
        [DataRow("tooBusy")]
        [DataRow("invalidParams")]
        public async Task RpcErrorsPreserveTheirCodeAndMethodWithoutServerMessages(string code)
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = (m, p, ct) => m == "submit" ? Task.FromResult(new JObject {
                ["error"] = code, ["error_message"] = "untrusted-server-text " + TestTransactions.SecpSeed }) : server.Respond(m, p, ct) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment()); var error = Error(result);
                Assert.AreEqual(code, (string)error["RpcCode"]); Assert.AreEqual("submit", (string)error["Method"]);
                Assert.AreEqual("Rpc", (string)error["Kind"]);
                Assert.IsFalse(JObject.FromObject(result).ToString().Contains("untrusted-server-text"));
            }
        }

        private sealed class StatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly PaymentTests.Server _server = new PaymentTests.Server();
            internal StatusHandler(int status) { _status = (HttpStatusCode)status; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var body = JObject.Parse(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                string method = (string)body["method"];
                if (method == "submit") return new HttpResponseMessage(_status) { Content = new StringContent("untrusted-server-text " + TestTransactions.SecpSeed) };
                var result = await _server.Respond(method, (JObject)body["params"][0], token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject { ["result"] = result }.ToString()) };
            }
        }

        [DataTestMethod]
        [DataRow(400)]
        [DataRow(429)]
        [DataRow(503)]
        public async Task HttpStatusIsAvailableOnBothFrameworks(int status)
        {
            using (var http = new HttpClient(new StatusHandler(status)))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var error = Error(await client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(status, (int)error["HttpStatusCode"]); Assert.AreEqual("submit", (string)error["Method"]);
                Assert.AreEqual("Http", (string)error["Kind"]);
            }
        }

        [DataTestMethod]
        [DataRow("Transport")]
        [DataRow("InvalidResponse")]
        [DataRow("Timeout")]
        [DataRow("Cancelled")]
        public async Task FailureKindsAreDistinctAndKeepTheReceipt(string kind)
        {
            using (var cancel = new CancellationTokenSource())
            {
                var options = RpcHandler.Options(); options.RequestTimeout = TimeSpan.FromMilliseconds(30);
                var server = new PaymentTests.Server();
                var handler = new RpcHandler { Respond = async (m, p, ct) => {
                    if (m == "submit")
                    {
                        if (kind == "Transport") throw new HttpRequestException(TestTransactions.SecpSeed);
                        if (kind == "InvalidResponse") throw new InvalidDataException(TestTransactions.SecpSeed);
                        if (kind == "Timeout") await Task.Delay(10000, ct).ConfigureAwait(false);
                        if (kind == "Cancelled") { cancel.Cancel(); ct.ThrowIfCancellationRequested(); }
                    }
                    return await server.Respond(m, p, ct).ConfigureAwait(false);
                } };
                using (var http = new HttpClient(handler))
                using (var client = new XrplClient(options, http))
                {
                    var error = Error(await client.SendXrpAsync(TestTransactions.SecpSeed, Payment(), cancel.Token));
                    Assert.AreEqual(kind, (string)error["Kind"]);
                }
            }
        }

        [TestMethod]
        public async Task ConfirmationDeadlineReturnsATimeoutDiagnostic()
        {
            var options = RpcHandler.Options(); options.ConfirmationTimeout = TimeSpan.FromMilliseconds(50);
            var server = new PaymentTests.Server { NeverFind = true };
            using (var http = new HttpClient(new RpcHandler { Respond = server.Respond }))
            using (var client = new XrplClient(options, http))
            {
                var error = Error(await client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual("Timeout", (string)error["Kind"]); Assert.AreEqual("tx", (string)error["Method"]);
            }
        }

        [TestMethod]
        public async Task ConfirmationRpcErrorKeepsTheActualMethod()
        {
            var server = new PaymentTests.Server { NeverFind = true }; int tips = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => m == "server_info" && ++tips > 1
                ? RpcHandler.Json("{'error':'noNetwork'}") : server.Respond(m, p, ct) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var error = Error(await client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual("noNetwork", (string)error["RpcCode"]); Assert.AreEqual("server_info", (string)error["Method"]);
            }
        }

        [TestMethod]
        public async Task UntrustedRpcCodeCannotEchoTheSeed()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = (m, p, ct) => m == "submit" ? Task.FromResult(new JObject {
                ["error"] = TestTransactions.SecpSeed, ["error_message"] = TestTransactions.SecpSeed }) : server.Respond(m, p, ct) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var error = Error(await client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(JTokenType.Null, error["RpcCode"].Type);
            }
        }
    }
}
