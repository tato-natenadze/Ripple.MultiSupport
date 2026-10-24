using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentWatchOrderingTests
    {
        private static XrplClientOptions Options()
        {
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/"); return options;
        }

        private static JObject Payment(char hash, uint ledger, uint index)
        {
            var payment = RpcHandler.Payment(new string(hash, 64), ledger);
            payment["meta"]["TransactionIndex"] = index;
            return payment;
        }

        [TestMethod]
        public async Task ShuffledPagesAreReplayedInLedgerOrderWithoutDuplicateCallbacks()
        {
            int pages = 0, saves = 0;
            var a = Payment('A', 101, 0); var b = Payment('B', 102, 0);
            var c = Payment('C', 102, 2); var d = Payment('D', 102, 8);
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(102));
                var history = RpcHandler.History(TestTransactions.Destination, 101, 102,
                    pages++ == 0 ? new[] { b, a, a } : new[] { d, c, b });
                if (pages == 1) history["marker"] = new JObject { ["ledger"] = 102, ["seq"] = 1 };
                return Task.FromResult(history);
            } };
            var received = new List<string>();
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                try { await client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                    received.Add(p.Hash); return Task.CompletedTask;
                }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (s, ct) => {
                    if (++saves == 2) cancel.Cancel(); return Task.CompletedTask;
                } }, cancel.Token); } catch (OperationCanceledException) { }
            }
            CollectionAssert.AreEqual(new[] { new string('A', 64), new string('B', 64), new string('C', 64), new string('D', 64) }, received);
            Assert.AreEqual(2, pages); Assert.AreEqual(2, saves);
        }

        [TestMethod]
        public async Task BackwardsPageDoesNotAdvanceCheckpoint()
        {
            int pages = 0, saves = 0, received = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(102));
                var history = RpcHandler.History(TestTransactions.Destination, 101, 102,
                    ++pages == 1 ? Payment('B', 102, 0) : Payment('A', 101, 0));
                if (pages == 1) history["marker"] = new JObject { ["ledger"] = 102, ["seq"] = 1 };
                return Task.FromResult(history);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => { received++; return Task.CompletedTask; }, new PaymentWatchOptions { FromLedger = 101,
                        SaveCheckpointAsync = (s, ct) => { saves++; return Task.CompletedTask; } }));
            }
            Assert.AreEqual(1, received); Assert.AreEqual(1, saves);
        }

        [TestMethod]
        public async Task MissingIndexIsRejectedBeforeAnyCallbackOnThePage()
        {
            var invalid = Payment('B', 101, 1); ((JObject)invalid["meta"]).Remove("TransactionIndex");
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, Payment('A', 101, 0), invalid)) };
            int received = 0;
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => { received++; return Task.CompletedTask; }, new PaymentWatchOptions { FromLedger = 101 }));
            }
            Assert.AreEqual(0, received);
        }

        [TestMethod]
        public async Task CallbackFailureAfterSortingPreservesOriginalExceptionAndCheckpoint()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, Payment('B', 101, 1), Payment('A', 101, 0))) };
            string received = null; int saves = 0;
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                var ex = await Assert.ThrowsExceptionAsync<IOException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => { received = p.Hash; throw new IOException("credit failed"); }, new PaymentWatchOptions { FromLedger = 101,
                        SaveCheckpointAsync = (s, ct) => { saves++; return Task.CompletedTask; } }));
                Assert.AreEqual("credit failed", ex.Message);
            }
            Assert.AreEqual(new string('A', 64), received); Assert.AreEqual(1, saves);
        }
    }
}
