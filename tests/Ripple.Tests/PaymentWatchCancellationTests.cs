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
    public class PaymentWatchCancellationTests
    {
        private static XrplClientOptions Options()
        {
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/"); return options;
        }
        private static RpcHandler History()
        {
            var a = RpcHandler.Payment(ledger: 101); var b = RpcHandler.Payment(new string('B', 64), 101); b["meta"]["TransactionIndex"] = 1;
            return new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, a, b)) };
        }
        private static async Task Cancelled(Task task)
        {
            try { await task; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CancellationOrDisposalStopsBeforeTheNextPaymentAndCheckpoint(bool dispose)
        {
            using (var http = new HttpClient(History()))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket(); int received = 0, saves = 0;
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                    received++; if (dispose) client.Dispose(); else cancel.Cancel(); return Task.CompletedTask;
                }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (s, ct) => { saves++; return Task.CompletedTask; } }, cancel.Token));
                Assert.AreEqual(1, received); Assert.AreEqual(1, saves);
            }
        }

        [TestMethod]
        public async Task AlreadyCancelledWatchCreatesNoCheckpointOrConnection()
        {
            var handler = History();
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                int saves = 0, sockets = 0; cancel.Cancel();
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask,
                    new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (s, ct) => { saves++; return Task.CompletedTask; } }, cancel.Token));
                Assert.AreEqual(0, saves); Assert.AreEqual(0, sockets); Assert.AreEqual(0, handler.Requests.Count);
            }
        }

        [TestMethod]
        public async Task RestartReplaysTheUnfinishedRangeAndAllowsHashDeduplication()
        {
            string checkpoint = null; int saves = 0, firstCallbacks = 0, replayCallbacks = 0;
            var credited = new HashSet<string>();
            using (var http = new HttpClient(History()))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                using (var cancel = new CancellationTokenSource())
                {
                    await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                        firstCallbacks++; credited.Add(p.Hash); cancel.Cancel(); return Task.CompletedTask;
                    }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (s, ct) => {
                        saves++; checkpoint = s; return Task.CompletedTask;
                    } }, cancel.Token));
                }
                Assert.AreEqual(1, firstCallbacks); Assert.AreEqual(1, saves);
                using (var cancel = new CancellationTokenSource())
                {
                    await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                        replayCallbacks++; credited.Add(p.Hash); return Task.CompletedTask;
                    }, new PaymentWatchOptions { Checkpoint = checkpoint, SaveCheckpointAsync = (s, ct) => {
                        saves++; checkpoint = s; cancel.Cancel(); return Task.CompletedTask;
                    } }, cancel.Token));
                }
                Assert.AreEqual(2, replayCallbacks); Assert.AreEqual(2, credited.Count); Assert.AreEqual(2, saves);
            }
        }

        [TestMethod]
        public async Task CheckpointFailureStopsWithTheOriginalException()
        {
            using (var http = new HttpClient(History()))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket(); int saves = 0, received = 0;
                var ex = await Assert.ThrowsExceptionAsync<IOException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => { received++; return Task.CompletedTask; }, new PaymentWatchOptions { FromLedger = 101,
                        SaveCheckpointAsync = (s, ct) => { if (++saves == 2) throw new IOException("checkpoint unavailable"); return Task.CompletedTask; } }));
                Assert.AreEqual("checkpoint unavailable", ex.Message); Assert.AreEqual(2, received); Assert.AreEqual(2, saves);
            }
        }
    }
}
