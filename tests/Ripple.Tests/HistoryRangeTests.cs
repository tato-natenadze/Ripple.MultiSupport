using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class HistoryRangeTests
    {
        [TestMethod]
        public async Task DefaultRangePinsTheFirstAvailableLedgerAndExposesLaterPruning()
        {
            int page = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                var history = RpcHandler.History(TestTransactions.Account, ++page == 1 ? 100u : 150u, 200);
                if (page == 1) history["marker"] = new JObject { ["ledger"] = 120, ["seq"] = 0 };
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ToLedger = 200 });
                var second = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
                Assert.IsTrue(first.RequestedRangeAvailable); Assert.IsFalse(second.RequestedRangeAvailable);
                Assert.AreEqual(100L, (long)handler.Requests[1]["params"][0]["ledger_index_min"]);
                Assert.AreEqual(200u, (uint)handler.Requests[1]["params"][0]["ledger_index_max"]);
            }
        }

        [TestMethod]
        public async Task ExplicitMissingRangeIsNotNarrowedByTheContinuationToken()
        {
            int page = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                var history = RpcHandler.History(TestTransactions.Account, 95, 100);
                if (++page == 1) history["marker"] = new JObject { ["ledger"] = 96, ["seq"] = 0 };
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { FromLedger = 90, ToLedger = 100 });
                var second = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
                Assert.IsFalse(first.RequestedRangeAvailable); Assert.IsFalse(second.RequestedRangeAvailable);
                Assert.AreEqual(90u, (uint)handler.Requests[1]["params"][0]["ledger_index_min"]);
            }
        }

        [TestMethod]
        public async Task PreviouslyIssuedDefaultTokenIsAcceptedAndUpgradedToAPinnedRange()
        {
            var old = new JObject { ["version"] = 1, ["endpoint"] = RpcHandler.Options().RpcEndpoint.AbsoluteUri,
                ["account"] = TestTransactions.Account, ["from"] = -1, ["to"] = 200,
                ["marker"] = new JObject { ["ledger"] = 150, ["seq"] = 0 } };
            int page = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                var history = RpcHandler.History(TestTransactions.Account, 100, 200);
                if (++page == 1) history["marker"] = new JObject { ["ledger"] = 160, ["seq"] = 0 };
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest {
                    ContinuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(old.ToString())) });
                await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
                Assert.AreEqual(-1L, (long)handler.Requests[0]["params"][0]["ledger_index_min"]);
                Assert.AreEqual(100L, (long)handler.Requests[1]["params"][0]["ledger_index_min"]);
            }
        }

        [DataTestMethod]
        [DataRow("account")]
        [DataRow("endpoint")]
        public async Task TokenBindingIsCheckedBeforeNetworkAccess(string field)
        {
            var old = new JObject { ["version"] = 1, ["endpoint"] = RpcHandler.Options().RpcEndpoint.AbsoluteUri,
                ["account"] = TestTransactions.Account, ["from"] = 100, ["to"] = 200,
                ["marker"] = new JObject { ["ledger"] = 150, ["seq"] = 0 } };
            old[field] = field == "account" ? TestTransactions.Destination : "https://different.example/";
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { ContinuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(old.ToString())) }));
            Assert.AreEqual(0, handler.Requests.Count);
        }
    }
}
