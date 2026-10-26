using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentFinalityTests
    {
        private static XrpPaymentRequest Payment() => new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
        private static JObject Missing(bool searchedAll = false) => new JObject { ["error"] = "txnNotFound", ["searched_all"] = searchedAll };

        [DataTestMethod]
        [DataRow("tesSUCCESS")]
        [DataRow("tecUNFUNDED_PAYMENT")]
        public async Task LaterMissingHistoryDoesNotReopenAFinalPayment(string code)
        {
            var server = new PaymentTests.Server { FinalCode = code };
            string oldHash = null; int oldQueries = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "tx" && (string)p["transaction"] == oldHash) { oldQueries++; return Task.FromResult(Missing()); }
                return server.Respond(m, p, ct);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                Assert.AreEqual(code == "tesSUCCESS" ? TransactionStatus.Succeeded : TransactionStatus.Failed, first.Status);
                oldHash = first.Receipt.Hash; server.FinalCode = "tesSUCCESS";
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(first.Receipt)).Status,
                    "Fresh reads must still expose unavailable server history.");
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                Assert.AreEqual(1, oldQueries, "Sending must not query a known-final receipt again.");
                Assert.AreEqual(2, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task LaterMissingHistoryDoesNotReopenProvenExpiry()
        {
            var receipt = PaymentRecoveryTests.Receipt(104);
            bool complete = true;
            var server = new PaymentTests.Server { Ledger = 105 };
            var handler = new RpcHandler { Respond = (m, p, ct) => m == "tx" && (string)p["transaction"] == receipt.Hash
                ? Task.FromResult(Missing(complete)) : server.Respond(m, p, ct) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(receipt)).Status);
                complete = false;
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(receipt)).Status);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                Assert.AreEqual(1, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task OldFinalityDoesNotClearOrReplaceANewerUnresolvedReceipt()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                server.LoseSubmitResponse = true; server.NeverFind = true;
                var second = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                Assert.AreEqual(TransactionStatus.Unknown, second.Status);
                handler.Respond = (m, p, ct) => m == "tx" && (string)p["transaction"] == first.Receipt.Hash
                    ? Task.FromResult(Missing()) : server.Respond(m, p, ct);
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(first.Receipt)).Status);
                var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(second.Receipt.Hash, blocked.Receipt.Hash);
                Assert.AreEqual(2, server.Signed.Count);
                server.Ledger = second.Receipt.LastLedger + 1; server.SearchedAll = true;
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(second.Receipt)).Status);
                server.NeverFind = false; server.LoseSubmitResponse = false;
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                Assert.AreEqual(3, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task FinalityMemoryDoesNotHideFreshLookupErrors()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                handler.Respond = (m, p, ct) => {
                    if (m == "tx" && (string)p["transaction"] == first.Receipt.Hash) throw new HttpRequestException("history offline");
                    return server.Respond(m, p, ct);
                };
                await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetTransactionAsync(first.Receipt));
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [Timeout(10000)]
        public async Task AWaitingSendObservesConcurrentFinalRecovery(bool otherUnresolved)
        {
            var server = new PaymentTests.Server { LoseSubmitResponse = true, NeverFind = true };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                server.NeverFind = false; server.LoseSubmitResponse = false;
                var final = await server.Respond("tx", new JObject(), CancellationToken.None);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var other = PaymentRecoveryTests.Receipt(108, "2000000");
                int oldQueries = 0;
                handler.Respond = async (m, p, ct) => {
                    if (m == "tx" && (string)p["transaction"] == first.Receipt.Hash)
                    {
                        if (Interlocked.Increment(ref oldQueries) == 1)
                        { entered.TrySetResult(true); await TestAwait.UntilAsync(release.Task).ConfigureAwait(false); return Missing(); }
                        return (JObject)final.DeepClone();
                    }
                    if (m == "tx" && (string)p["transaction"] == other.Hash) return Missing();
                    return await server.Respond(m, p, ct).ConfigureAwait(false);
                };
                var sending = client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                await TestAwait.UntilAsync(entered.Task);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.GetTransactionAsync(first.Receipt)).Status);
                if (otherUnresolved) await client.GetTransactionAsync(other);
                release.TrySetResult(true);
                if (otherUnresolved)
                {
                    var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(async () => { await sending; });
                    Assert.AreEqual(other.Hash, blocked.Receipt.Hash);
                    Assert.AreEqual(1, server.Signed.Count);
                }
                else
                {
                    Assert.AreEqual(TransactionStatus.Succeeded, (await sending).Status);
                    Assert.AreEqual(2, server.Signed.Count);
                }
            }
        }
    }
}
