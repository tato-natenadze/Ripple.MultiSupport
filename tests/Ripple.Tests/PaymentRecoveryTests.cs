using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentRecoveryTests
    {
        internal static PaymentReceipt Receipt(uint lastLedger, string amount = "1000000", bool ed = false)
        {
            var tx = TestTransactions.Payment(); tx.Remove("Seed");
            tx["LastLedgerSequence"] = lastLedger; tx["Amount"] = amount;
            if (ed) { tx["Account"] = TestTransactions.Destination; tx["Destination"] = TestTransactions.Account; }
            var signed = TxSigner.SignJson(tx, ed ? TestTransactions.EdSeed : TestTransactions.SecpSeed);
            return new PaymentReceipt(signed.Hash, (string)tx["Account"], 1, 101, lastLedger, signed.TxBlob, RpcHandler.Options().RpcEndpoint);
        }

        private static JObject Missing(bool searchedAll) => new JObject { ["error"] = "txnNotFound", ["searched_all"] = searchedAll };
        private static XrpPaymentRequest Payment() => new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task EveryRecoveredReceiptBlocksUntilItsOwnOutcomeIsFinal(bool reverse)
        {
            var a = Receipt(104); var b = Receipt(108, "2000000");
            bool expiredA = false, expiredB = false;
            var server = new PaymentTests.Server { Ledger = 103, NeverFind = true, LoseSubmitResponse = true };
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "tx" && (string)p["transaction"] == a.Hash) return Task.FromResult(Missing(expiredA));
                if (m == "tx" && (string)p["transaction"] == b.Hash) return Task.FromResult(Missing(expiredB));
                return server.Respond(m, p, ct);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(reverse ? b : a)).Status);
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(reverse ? a : b)).Status);
                server.Ledger = 105; expiredA = true;
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(a)).Status);
                var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(b.Hash, blocked.Receipt.Hash);
                Assert.AreEqual(0, server.Signed.Count);
                server.Ledger = 109; expiredB = true;
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(b)).Status);
                server.NeverFind = false; server.LoseSubmitResponse = false;
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                Assert.AreEqual(1, server.Signed.Count);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ConcurrentRecoveryRetainsBothUnresolvedHashes()
        {
            var a = Receipt(104); var b = Receipt(108, "2000000");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int readers = 0; bool recovering = true;
            var server = new PaymentTests.Server { Ledger = 103, LoseSubmitResponse = true };
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                if (m == "tx")
                {
                    if (recovering)
                    {
                        if (Interlocked.Increment(ref readers) == 2) entered.TrySetResult(true);
                        await TestAwait.UntilAsync((string)p["transaction"] == a.Hash ? releaseA.Task : releaseB.Task).ConfigureAwait(false);
                        return Missing(false);
                    }
                    return Missing((string)p["transaction"] == a.Hash);
                }
                return await server.Respond(m, p, ct).ConfigureAwait(false);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = client.GetTransactionAsync(a); var second = client.GetTransactionAsync(b);
                await TestAwait.UntilAsync(entered.Task); releaseA.TrySetResult(true); await first; releaseB.TrySetResult(true);
                var recovered = await Task.WhenAll(first, second);
                Assert.AreEqual(TransactionStatus.Unknown, recovered[0].Status);
                Assert.AreEqual(TransactionStatus.Unknown, recovered[1].Status);
                recovering = false; server.Ledger = 105;
                Assert.AreEqual(TransactionStatus.Expired, (await client.GetTransactionAsync(a)).Status);
                var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(b.Hash, blocked.Receipt.Hash);
                Assert.AreEqual(0, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task RecoveryDuringPreparationDoesNotOverwriteOtherReceipts()
        {
            var a = Receipt(104, "2000000"); var b = Receipt(108, "3000000");
            bool expiredA = false;
            var server = new PaymentTests.Server { Ledger = 103, NeverFind = true, LoseSubmitResponse = true };
            var handler = new RpcHandler { Respond = (m, p, ct) => m == "tx"
                ? Task.FromResult(Missing(expiredA && (string)p["transaction"] == a.Hash)) : server.Respond(m, p, ct) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = Payment(); payment.OnPreparedAsync = async (r, ct) => {
                    await client.GetTransactionAsync(a, ct); await client.GetTransactionAsync(b, ct);
                };
                await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                server.Ledger = 105; expiredA = true;
                await client.GetTransactionAsync(a);
                var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, Payment()));
                Assert.AreEqual(b.Hash, blocked.Receipt.Hash);
                Assert.AreEqual(0, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task ReceiptsFromDifferentSendersRemainIndependent()
        {
            var a = Receipt(104); var b = Receipt(108, ed: true);
            bool expiredA = false;
            var server = new PaymentTests.Server { Ledger = 103 };
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                string hash = (string)p["transaction"];
                if (m == "tx" && hash == a.Hash) return Task.FromResult(Missing(expiredA));
                if (m == "tx" && hash == b.Hash) return Task.FromResult(Missing(false));
                return server.Respond(m, p, ct);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await client.GetTransactionAsync(a); await client.GetTransactionAsync(b);
                server.Ledger = 105; expiredA = true; await client.GetTransactionAsync(a);
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed, Payment())).Status);
                var blocked = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.EdSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Account, AmountXrp = 1 }));
                Assert.AreEqual(b.Hash, blocked.Receipt.Hash);
                Assert.AreEqual(1, server.Signed.Count);
            }
        }
    }
}
