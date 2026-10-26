using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentStateTests
    {
        private static XrpPaymentRequest Payment() => new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };

        [DataTestMethod]
        [DataRow("tesSUCCESS")]
        [DataRow("tecUNFUNDED_PAYMENT")]
        public async Task FinalizedPaymentIsNotQueriedAgainBeforeTheNextSend(string code)
        {
            var server = new PaymentTests.Server { FinalCode = code };
            string finalized = null; int oldQueries = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "tx" && (string)p["transaction"] == finalized) { oldQueries++; throw new HttpRequestException("old history unavailable"); }
                return server.Respond(m, p, ct);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                Assert.IsTrue(first.Transaction.Validated); finalized = first.Receipt.Hash; server.FinalCode = "tesSUCCESS";
                var second = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                Assert.AreEqual(TransactionStatus.Succeeded, second.Status);
                CollectionAssert.AreEqual(new uint[] { 1, 2 }, server.Signed.Select(t => (uint)t["Sequence"]).ToArray());
                Assert.AreEqual(0, oldQueries);
            }
        }

        [TestMethod]
        public async Task RecoveringAReceiptClearsOnlyThatPayment()
        {
            var server = new PaymentTests.Server { LoseSubmitResponse = true, NeverFind = true };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                server.NeverFind = false;
                var finalResponse = await server.Respond("tx", new JObject(), default(System.Threading.CancellationToken));
                handler.Respond = (m, p, ct) => m == "tx" && (string)p["transaction"] == first.Receipt.Hash
                    ? Task.FromResult((JObject)finalResponse.DeepClone()) : server.Respond(m, p, ct);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.GetTransactionAsync(first.Receipt)).Status);
                server.NeverFind = true;
                var second = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                Assert.AreEqual(TransactionStatus.Unknown, second.Status);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.GetTransactionAsync(first.Receipt)).Status);
                var ex = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(second.Receipt.Hash, ex.Receipt.Hash);
                Assert.AreEqual(2, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task ProvenExpiryRemovesTheRestoredPendingReceipt()
        {
            var receipt = PaymentTests.Receipt();
            var server = new PaymentTests.Server { NeverFind = true, Ledger = 103 };
            bool rejectOldLookup = false;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (rejectOldLookup && m == "tx" && (string)p["transaction"] == receipt.Hash) throw new HttpRequestException("old history unavailable");
                return server.Respond(m, p, ct);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(receipt)).Status);
                server.Ledger = 105; server.SearchedAll = true;
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(receipt)).Status);
                rejectOldLookup = true; server.NeverFind = false;
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConcurrentOldFinalLookupCannotRemoveANewerPendingPayment()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                var final = await server.Respond("tx", new JObject(), default(System.Threading.CancellationToken));
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Respond = async (m, p, ct) => {
                    if (m == "tx" && (string)p["transaction"] == first.Receipt.Hash)
                    { entered.TrySetResult(true); await TestAwait.UntilAsync(release.Task).ConfigureAwait(false); return (JObject)final.DeepClone(); }
                    return await server.Respond(m, p, ct).ConfigureAwait(false);
                };
                var lookup = client.GetTransactionAsync(first.Receipt);
                await TestAwait.UntilAsync(entered.Task);
                server.LoseSubmitResponse = true; server.NeverFind = true;
                var second = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                release.TrySetResult(true); await lookup;
                var ex = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(second.Receipt.Hash, ex.Receipt.Hash); Assert.AreEqual(2, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task ReceiptRestoredDuringPreparationIsNeverOverwritten()
        {
            var tx = TestTransactions.Payment(); tx.Remove("Seed"); tx["Amount"] = "2000000"; tx["LastLedgerSequence"] = 104;
            var signed = Ripple.LibraryClasses.Signer.TxSigner.SignJson(tx, TestTransactions.SecpSeed);
            var other = new PaymentReceipt(signed.Hash, TestTransactions.Account, 1, 101, 104, signed.TxBlob, RpcHandler.Options().RpcEndpoint);
            var server = new PaymentTests.Server { NeverFind = true };
            using (var http = new HttpClient(new RpcHandler { Respond = server.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = Payment(); payment.OnPreparedAsync = async (r, ct) => { await client.GetTransactionAsync(other, ct); };
                var ex = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                Assert.AreEqual(other.Hash, ex.Receipt.Hash); Assert.AreEqual(0, server.Signed.Count);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConcurrentStaleUnknownLookupCannotRestoreAFinalizedReceipt()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, Payment());
                var final = await server.Respond("tx", new JObject(), default(System.Threading.CancellationToken));
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int lookups = 0;
                handler.Respond = async (m, p, ct) => {
                    if (m == "tx" && (string)p["transaction"] == first.Receipt.Hash)
                    {
                        int lookup = ++lookups;
                        if (lookup == 1) { entered.TrySetResult(true); await TestAwait.UntilAsync(release.Task).ConfigureAwait(false); return JObject.Parse("{'error':'txnNotFound','searched_all':false}"); }
                        if (lookup == 2) return (JObject)final.DeepClone();
                        throw new HttpRequestException("old history unavailable");
                    }
                    return await server.Respond(m, p, ct).ConfigureAwait(false);
                };
                var stale = client.GetTransactionAsync(first.Receipt); await TestAwait.UntilAsync(entered.Task);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.GetTransactionAsync(first.Receipt)).Status);
                release.TrySetResult(true); Assert.AreEqual(TransactionStatus.Unknown, (await stale).Status);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                Assert.AreEqual(2, lookups);
            }
        }
    }
}
