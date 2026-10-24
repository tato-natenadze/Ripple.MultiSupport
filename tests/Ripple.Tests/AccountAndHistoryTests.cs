using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class AccountAndHistoryTests
    {
        [TestMethod]
        public async Task AccountUsesPinnedValidatedLedgerAndDynamicReserve()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger() :
                RpcHandler.Account(TestTransactions.Account, flags: 0x01020000)) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var account = await client.GetAccountAsync(TestTransactions.Account);
                Assert.IsTrue(account.Exists); Assert.AreEqual(100m, account.BalanceXrp);
                Assert.AreEqual(1.4m, account.EstimatedReserveXrp); Assert.AreEqual(98.6m, account.EstimatedSpendableXrp);
                Assert.IsTrue(account.RequiresDestinationTag); Assert.IsTrue(account.DepositAuthorization);
                Assert.AreEqual(100u, (uint)handler.Requests[1]["params"][0]["ledger_index"]);
            }
        }

        [TestMethod]
        public async Task UnfundedAccountIsDistinctFromAnUnavailableServer()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger() : JObject.Parse("{'error':'actNotFound'}")) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.IsFalse((await client.GetAccountAsync(TestTransactions.Account)).Exists);
                handler.Respond = (m, p, ct) => throw new HttpRequestException("offline");
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAccountAsync(TestTransactions.Account));
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task UnvalidatedOrMismatchedAccountDataIsRejected(bool differentLedger)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger());
                var a = RpcHandler.Account(TestTransactions.Account, differentLedger ? 101u : 100u);
                a["validated"] = differentLedger; return Task.FromResult(a);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetAccountAsync(TestTransactions.Account));
        }

        [DataTestMethod]
        [DataRow("tesSUCCESS", true, TransactionStatus.Succeeded)]
        [DataRow("tecUNFUNDED_PAYMENT", true, TransactionStatus.Failed)]
        [DataRow("tesSUCCESS", false, TransactionStatus.Pending)]
        public async Task TransactionStatusUsesValidatedMetadata(string code, bool validated, TransactionStatus expected)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(RpcHandler.Payment(code: code, validated: validated)) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var tx = await client.GetTransactionAsync(new string('a', 64));
                Assert.AreEqual(expected, tx.Status); Assert.AreEqual(0u, tx.DestinationTag);
                Assert.AreEqual(validated && code == "tesSUCCESS" ? (decimal?)1 : null, tx.DeliveredAmountXrp);
            }
        }

        [TestMethod]
        public async Task UnknownHashIsNotReportedAsFailed()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => RpcHandler.Json("{'error':'txnNotFound'}") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var tx = await client.GetTransactionAsync(new string('A', 64));
                Assert.IsFalse(tx.Found); Assert.AreEqual(TransactionStatus.Unknown, tx.Status);
            }
        }

        [TestMethod]
        public async Task V1AndV2TransactionResponsesNormalizeIdentically()
        {
            var response = RpcHandler.Payment();
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var v2 = await client.GetTransactionAsync(new string('A', 64));
                var body = (JObject)response["tx_json"].DeepClone();
                body["Amount"] = body["DeliverMax"]; body.Remove("DeliverMax");
                body["hash"] = response["hash"]; body["meta"] = response["meta"];
                body["validated"] = true; body["ledger_index"] = 100; response = body;
                var v1 = await client.GetTransactionAsync(new string('A', 64));
                Assert.AreEqual(v2.DeliveredAmountXrp, v1.DeliveredAmountXrp);
                Assert.AreEqual(v2.AmountXrp, v1.AmountXrp); Assert.AreEqual(v2.Status, v1.Status);
            }
        }

        [TestMethod]
        public async Task HistoryContinuationPinsRangeAndIsBoundToAccount()
        {
            int page = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(100));
                var history = RpcHandler.History(TestTransactions.Account, 90, 100, RpcHandler.Payment());
                if (page++ == 0) history["marker"] = new JObject { ["ledger"] = 100, ["seq"] = 1 };
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account);
                Assert.AreEqual(1, first.Payments.Count); Assert.IsNotNull(first.ContinuationToken);
                var second = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
                Assert.AreEqual(100u, second.ToLedger); Assert.IsNull(second.ContinuationToken);
                Assert.AreEqual(100u, (uint)handler.Requests[2]["params"][0]["ledger_index_max"]);
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentsAsync(TestTransactions.Destination,
                    new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken }));
            }
        }

        [TestMethod]
        public async Task HistoryExposesUnavailableRangeAndFiltersOtherAssets()
        {
            var payment = RpcHandler.Payment();
            payment["tx_json"]["DeliverMax"] = new JObject { ["currency"] = "USD", ["value"] = "1", ["issuer"] = TestTransactions.Account };
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(RpcHandler.History(TestTransactions.Account, 95, 100, payment)) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var history = await client.GetPaymentsAsync(TestTransactions.Account, new PaymentHistoryRequest { FromLedger = 90, ToLedger = 100 });
                Assert.IsFalse(history.RequestedRangeAvailable); Assert.AreEqual(0, history.Payments.Count);
            }
        }

        [TestMethod]
        public async Task IncomingAmountUsesDeliveredAmountAndPreservesUnavailable()
        {
            var response = RpcHandler.Payment(); response["meta"]["delivered_amount"] = "500000";
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(0.5m, (await client.GetTransactionAsync(new string('A', 64))).DeliveredAmountXrp);
                response["meta"]["delivered_amount"] = "unavailable";
                Assert.IsNull((await client.GetTransactionAsync(new string('A', 64))).DeliveredAmountXrp);
            }
        }

        [TestMethod]
        public async Task NetworkAmountsAreCultureIndependent()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger() : RpcHandler.Account(TestTransactions.Account)) };
                using (var http = new HttpClient(handler))
                using (var client = new XrplClient(RpcHandler.Options(), http))
                    Assert.AreEqual(1.4m, (await client.GetAccountAsync(TestTransactions.Account)).EstimatedReserveXrp);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
