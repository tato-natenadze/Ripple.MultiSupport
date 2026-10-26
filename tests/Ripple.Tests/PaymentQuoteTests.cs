using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentQuoteTests
    {
        private static decimal Xrp(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

        [DataTestMethod]
        [DataRow("100000000", "1.000001", "1.000013", "98.999987", "97.599987", "98.599988")]
        [DataRow("2400012", "1", "1.000012", "1.4", "0", "1")]
        [DataRow("1400013", "0.000001", "0.000013", "1.4", "0", "0.000001")]
        public async Task QuoteIncludesExactDebitReserveAndRemainingFundsWithoutSubmitting(string balance, string amount,
            string debit, string remainingBalance, string remainingSpendable, string maximum)
        {
            var server = new PaymentTests.Server { Balance = balance };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                int callbacks = 0;
                var quote = await client.GetPaymentQuoteAsync(TestTransactions.Account, new XrpPaymentRequest {
                    Destination = TestTransactions.Destination, AmountXrp = Xrp(amount), DestinationTag = 0,
                    OnPreparedAsync = (receipt, token) => { callbacks++; throw new Exception("Quote must not prepare a transaction."); } });
                Assert.AreEqual(TestTransactions.Account, quote.Sender);
                Assert.AreEqual(TestTransactions.Destination, quote.Destination);
                Assert.AreEqual(0u, quote.DestinationTag);
                Assert.AreEqual(100u, quote.LedgerIndex);
                Assert.AreEqual(Xrp(amount), quote.AmountXrp);
                Assert.AreEqual(0.000012m, quote.EstimatedFeeXrp);
                Assert.AreEqual(Xrp(debit), quote.EstimatedTotalDebitXrp);
                Assert.AreEqual(1.4m, quote.SenderReserveXrp);
                Assert.AreEqual(Xrp(remainingBalance), quote.EstimatedRemainingBalanceXrp);
                Assert.AreEqual(Xrp(remainingSpendable), quote.EstimatedRemainingSpendableXrp);
                Assert.AreEqual(Xrp(maximum), quote.EstimatedMaximumAmountXrp);
                Assert.AreEqual(0, callbacks);
                Assert.IsFalse(server.Submitted);
                Assert.AreEqual(0, server.Signed.Count);
                Assert.IsTrue(handler.Requests.All(r => (string)r["method"] != "submit"));
                Assert.IsTrue(handler.Requests.Where(r => (string)r["method"] == "account_info" &&
                    (string)r["params"][0]["ledger_index"] != "current").All(r => (uint)r["params"][0]["ledger_index"] == quote.LedgerIndex));
            }
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("-1")]
        [DataRow("0.0000001")]
        [DataRow("100000000001")]
        public async Task InvalidAmountsAreRejectedWithoutNetworkAccess(string amount)
        {
            var handler = new RpcHandler { Respond = (method, args, token) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = Xrp(amount) }));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task QuoteEnforcesTheCurrentFeeLimitAndReserve()
        {
            var server = new PaymentTests.Server { Fee = "101" };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m };
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                server.Fee = "12"; server.Balance = "2400011";
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task QuoteUsesTheDestinationActivationThreshold()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = (method, args, token) => method == "account_info" &&
                (string)args["account"] == TestTransactions.Destination
                ? RpcHandler.Json("{'error':'actNotFound'}") : server.Respond(method, args, token) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 0.999999m };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                request.AmountXrp = 1m;
                var quote = await client.GetPaymentQuoteAsync(TestTransactions.Account, request);
                Assert.IsFalse(quote.DestinationExists);
                Assert.AreEqual(1m, quote.DestinationActivationReserveXrp);
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task QuoteRejectsMissingTagsAndUnauthorizedDeposits()
        {
            var server = new PaymentTests.Server { TargetFlags = 0x00020000 };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                request.DestinationTag = 0;
                Assert.AreEqual(0u, (await client.GetPaymentQuoteAsync(TestTransactions.Account, request)).DestinationTag);
                server.TargetFlags = 0x01000000;
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
            }
        }

        [TestMethod]
        public async Task ExternalQueuedPaymentsCannotProduceAnExecutableQuote()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                var response = await server.Respond(method, args, token);
                if (method == "account_info" && (string)args["ledger_index"] == "current")
                    response["queue_data"]["txn_count"] = 1;
                return response;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m }));
            Assert.IsFalse(server.Submitted);
        }

        [TestMethod]
        public async Task AnUnresolvedReceiptAlsoBlocksQuotesWithoutResubmitting()
        {
            var server = new PaymentTests.Server { LoseSubmitResponse = true, NeverFind = true };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m };
                var sent = await client.SendXrpAsync(TestTransactions.SecpSeed, request);
                var error = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                Assert.AreEqual(sent.Receipt.Hash, error.Receipt.Hash);
                Assert.AreEqual(1, handler.Requests.Count(r => (string)r["method"] == "submit"));
            }
        }

        [TestMethod]
        public async Task SendingRechecksTheFeeAfterAnEarlierQuote()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m };
                var quote = await client.GetPaymentQuoteAsync(TestTransactions.Account, request);
                server.Fee = "24";
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed, request);
                Assert.AreEqual(0.000012m, quote.EstimatedFeeXrp);
                Assert.AreEqual(0.000024m, result.Transaction.FeeXrp);
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task QuoteSnapshotsCallerInputBeforeAwaiting()
        {
            var server = new PaymentTests.Server();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                if (method == "server_info") { entered.TrySetResult(true); await TestAwait.UntilAsync(release.Task); }
                return await server.Respond(method, args, token);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m, DestinationTag = 0 };
                var pending = client.GetPaymentQuoteAsync(TestTransactions.Account, request);
                await TestAwait.UntilAsync(entered.Task);
                request.AmountXrp = 99m; request.Destination = TestTransactions.Account; request.DestinationTag = 77;
                release.SetResult(true);
                var quote = await pending;
                Assert.AreEqual(1m, quote.AmountXrp);
                Assert.AreEqual(TestTransactions.Destination, quote.Destination);
                Assert.AreEqual(0u, quote.DestinationTag);
            }
        }

        [TestMethod]
        public async Task QuoteCancellationReleasesTheSenderGate()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                await Task.Delay(Timeout.Infinite, token); return new JObject(); } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m };
                var pending = client.GetPaymentQuoteAsync(TestTransactions.Account, request, cancel.Token);
                cancel.Cancel();
                try { await pending; Assert.Fail("Quote ignored cancellation."); } catch (OperationCanceledException) { }
                handler.Respond = server.Respond;
                Assert.AreEqual(1m, (await client.GetPaymentQuoteAsync(TestTransactions.Account, request)).AmountXrp);
                Assert.IsFalse(server.Submitted);
            }
        }
    }
}
