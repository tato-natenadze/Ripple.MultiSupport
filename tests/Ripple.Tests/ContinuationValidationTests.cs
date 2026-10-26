using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class ContinuationValidationTests
    {
        private static JObject State() => new JObject { ["version"] = 1, ["account"] = TestTransactions.Account,
            ["endpoint"] = RpcHandler.Options().RpcEndpoint.AbsoluteUri, ["from"] = 100, ["to"] = 200,
            ["marker"] = new JObject { ["ledger"] = 150, ["seq"] = 0 }, ["next_ledger"] = 101 };
        private static string Encode(JObject state) => Convert.ToBase64String(Encoding.UTF8.GetBytes(state.ToString()));

        private static string Invalid(string scenario, bool watch)
        {
            var state = State();
            string range = watch ? "next_ledger" : "from";
            switch (scenario)
            {
                case "base64": return "%%%";
                case "root": return Convert.ToBase64String(Encoding.UTF8.GetBytes("[]"));
                case "version-string": state["version"] = "1"; break;
                case "version-fraction": state["version"] = 1.0m; break;
                case "version-missing": state.Remove("version"); break;
                case "version-overflow": state["version"] = 4294967296L; break;
                case "account-type": state["account"] = new JObject(); break;
                case "endpoint-type": state["endpoint"] = new JArray(); break;
                case "range-string": state[range] = "not-a-number"; break;
                case "range-number-string": state[range] = "101"; break;
                case "range-fraction": state[range] = 100.5m; break;
                case "range-overflow": state[range] = 4294967296L; break;
                case "range-negative": state[range] = -2; break;
                case "range-missing": state.Remove(range); break;
                case "range-null": state[range] = JValue.CreateNull(); break;
                case "range-invalid": if (watch) state[range] = 0; else state[range] = 201; break;
                case "extra-undefined": state["extra"] = JValue.CreateUndefined(); break;
                case "duplicate": return Convert.ToBase64String(Encoding.UTF8.GetBytes(state.ToString().Replace("\"version\": 1", "\"version\": 2, \"version\": 1")));
                case "utf8":
                    state["extra"] = "?"; var bytes = Encoding.UTF8.GetBytes(state.ToString()); bytes[Array.IndexOf(bytes, (byte)'?')] = 255;
                    return Convert.ToBase64String(bytes);
            }
            return Encode(state);
        }

        [DataTestMethod]
        [DataRow("base64")]
        [DataRow("root")]
        [DataRow("version-string")]
        [DataRow("version-fraction")]
        [DataRow("version-missing")]
        [DataRow("version-overflow")]
        [DataRow("account-type")]
        [DataRow("endpoint-type")]
        [DataRow("range-string")]
        [DataRow("range-number-string")]
        [DataRow("range-fraction")]
        [DataRow("range-overflow")]
        [DataRow("range-negative")]
        [DataRow("range-missing")]
        [DataRow("range-null")]
        [DataRow("range-invalid")]
        [DataRow("extra-undefined")]
        [DataRow("duplicate")]
        [DataRow("utf8")]
        public async Task InvalidHistoryStateIsAnArgumentErrorBeforeIO(string scenario)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { ContinuationToken = Invalid(scenario, false) }));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("base64")]
        [DataRow("root")]
        [DataRow("version-string")]
        [DataRow("version-fraction")]
        [DataRow("version-missing")]
        [DataRow("version-overflow")]
        [DataRow("account-type")]
        [DataRow("endpoint-type")]
        [DataRow("range-string")]
        [DataRow("range-number-string")]
        [DataRow("range-fraction")]
        [DataRow("range-overflow")]
        [DataRow("range-negative")]
        [DataRow("range-missing")]
        [DataRow("range-null")]
        [DataRow("range-invalid")]
        [DataRow("extra-undefined")]
        [DataRow("duplicate")]
        [DataRow("utf8")]
        public async Task InvalidCheckpointIsAnArgumentErrorBeforeIO(string scenario)
        {
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/");
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") }; int sockets = 0;
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            {
                client.SocketFactory = () => { sockets++; throw new Exception("Unexpected connection"); };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Account,
                    (p, ct) => Task.CompletedTask, new PaymentWatchOptions { Checkpoint = Invalid(scenario, true) }));
            }
            Assert.AreEqual(0, handler.Requests.Count); Assert.AreEqual(0, sockets);
        }

        [DataTestMethod]
        [DataRow("null-marker")]
        [DataRow("missing-marker")]
        [DataRow("bad-upper-bound")]
        public async Task IncompleteHistoryTokensFailBeforeIO(string scenario)
        {
            var state = State();
            if (scenario == "null-marker") state["marker"] = JValue.CreateNull();
            else if (scenario == "missing-marker") state.Remove("marker"); else state["to"] = "200";
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { ContinuationToken = Encode(state) }));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [DataTestMethod]
        [DataRow("\"2026-10-24T01:02:03.0000000+00:00\"")]
        [DataRow("[1,\"cursor\",null]")]
        [DataRow("true")]
        [DataRow("123")]
        public async Task OpaqueMarkerTypesArePreserved(string marker)
        {
            var state = State(); var expected = Newtonsoft.Json.JsonConvert.DeserializeObject<JToken>(marker,
                new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
            state["marker"] = expected;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                Assert.IsTrue(JToken.DeepEquals(expected, p["marker"]));
                return Task.FromResult(RpcHandler.History(TestTransactions.Account, 100, 200));
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = Encode(state) });
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task MutableRequestIsSnapshottedBeforeTheFirstAwait()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                if (m == "server_info") { entered.TrySetResult(true); await TestAwait.UntilAsync(release.Task).ConfigureAwait(false); return RpcHandler.Ledger(); }
                Assert.AreEqual(1, (int)p["limit"]); Assert.AreEqual(-1L, (long)p["ledger_index_min"]); Assert.AreEqual(100u, (uint)p["ledger_index_max"]);
                return RpcHandler.History(TestTransactions.Account, 90, 100);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new PaymentHistoryRequest { Limit = 1 };
                var pending = client.GetPaymentsAsync(TestTransactions.Account, request); await TestAwait.UntilAsync(entered.Task);
                request.Limit = 401; request.FromLedger = 99; request.ToLedger = 999; request.ContinuationToken = "%%%";
                release.TrySetResult(true); await pending;
            }
        }

        [TestMethod]
        public async Task DateLikeServerMarkerRoundTripsWithoutReformatting()
        {
            const string marker = "2026-10-24T01:02:03.0000000+00:00"; int pages = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                var history = RpcHandler.History(TestTransactions.Account, 100, 200);
                if (++pages == 1) history["marker"] = marker;
                else { Assert.AreEqual(JTokenType.String, p["marker"].Type); Assert.AreEqual(marker, (string)p["marker"]); }
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ToLedger = 200 });
                await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
            }
            Assert.AreEqual(2, pages);
        }

        private sealed class InvalidUtf8Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var history = RpcHandler.History(TestTransactions.Account, 100, 200); history["marker"] = "?";
                var bytes = Encoding.UTF8.GetBytes(new JObject { ["result"] = history }.ToString());
                bytes[Array.IndexOf(bytes, (byte)'?')] = 255;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
        }

        [TestMethod]
        public async Task InvalidUtf8ResponseCannotBeRewrittenIntoAValidMarker()
        {
            using (var http = new HttpClient(new InvalidUtf8Handler()))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<System.IO.InvalidDataException>(() => client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { ToLedger = 200 }));
        }
    }
}
