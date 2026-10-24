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
    public class PaymentEdgeCaseTests
    {
        private sealed class RejectingContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object state) => throw new InvalidOperationException("Library captured the request context.");
        }

        [TestMethod]
        public async Task LibraryContinuationsDoNotRequireTheWebFormsRequestContext()
        {
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                await Task.Delay(10, ct).ConfigureAwait(false);
                return m == "server_info" ? RpcHandler.Ledger() : RpcHandler.Account(TestTransactions.Account);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                SynchronizationContext original = SynchronizationContext.Current;
                Task<AccountInfo> task;
                try { SynchronizationContext.SetSynchronizationContext(new RejectingContext()); task = client.GetAccountAsync(TestTransactions.Account); }
                finally { SynchronizationContext.SetSynchronizationContext(original); }
                Assert.IsTrue((await task).Exists);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExternalPendingTransactionsAreNotSignedOver(bool queued)
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                var result = await server.Respond(m, p, ct);
                if (m == "account_info" && (string)p["ledger_index"] == "current")
                {
                    if (queued) result["queue_data"]["txn_count"] = 1;
                    else result["account_data"]["Sequence"] = 2;
                }
                return result;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 }));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task StaleValidatedLedgerIsRejected()
        {
            var ledger = RpcHandler.Ledger(); ledger["info"]["validated_ledger"]["age"] = 121;
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(ledger) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetAccountAsync(TestTransactions.Account));
        }

        [TestMethod]
        public async Task HashMismatchAndMissingFinalMetadataAreRejected()
        {
            var response = RpcHandler.Payment(new string('B', 64));
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(new string('A', 64)));
                response = RpcHandler.Payment(); response.Remove("meta");
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(new string('A', 64)));
            }
        }

        [TestMethod]
        public async Task ConfirmationTimeoutPreservesTheReceiptAndDoesNotResubmit()
        {
            var server = new PaymentTests.Server { NeverFind = true }; var handler = new RpcHandler { Respond = server.Respond };
            var options = RpcHandler.Options(); options.ConfirmationTimeout = TimeSpan.FromMilliseconds(50);
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            {
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 });
                Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
                Assert.AreEqual(1, handler.Requests.Count(r => (string)r["method"] == "submit"));
            }
        }

        [TestMethod]
        public async Task RequestDeadlineCancelsUnresponsiveTransport()
        {
            var handler = new RpcHandler { Respond = async (m, p, ct) => { await Task.Delay(10000, ct).ConfigureAwait(false); return new JObject(); } };
            var options = RpcHandler.Options(); options.RequestTimeout = TimeSpan.FromMilliseconds(20);
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            {
                try { await client.GetAccountAsync(TestTransactions.Account); Assert.Fail("Expected timeout."); }
                catch (OperationCanceledException) { }
            }
        }

        [TestMethod]
        public async Task RestoredUnresolvedReceiptBlocksAnotherPayment()
        {
            var server = new PaymentTests.Server { NeverFind = true }; var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(PaymentTests.Receipt())).Status);
                await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 }));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task MemoByteLimitAndInvalidAddressFailBeforeIO()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1, Memo = new string('ქ', 334) }));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountAsync("not-an-address"));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }
    }
}
