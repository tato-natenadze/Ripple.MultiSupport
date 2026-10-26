using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentWatchTests
    {
        internal sealed class Socket : IXrplSocket
        {
            internal bool Disposed;
            internal Action Connected;
            internal Func<int, CancellationToken, Task<string>> Receive;
            internal JObject Subscription;
            private int _reads;
            public Task ConnectAsync(Uri endpoint, CancellationToken token) { Connected?.Invoke(); return Task.CompletedTask; }
            public Task SendAsync(string message, CancellationToken token) { Subscription = JObject.Parse(message); return Task.CompletedTask; }
            public async Task<string> ReceiveAsync(CancellationToken token)
            {
                if (_reads++ == 0) return "{'id':'payments','status':'success','result':{}}";
                if (Receive != null) return await Receive(_reads, token);
                await Task.Delay(10000, token); return "{}";
            }
            public void Dispose() { Disposed = true; }
        }
        private static XrplClientOptions Options()
        {
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/"); return options;
        }
        private static async Task Cancelled(Task task)
        {
            try { await task; Assert.Fail("Watch did not cancel."); } catch (OperationCanceledException) { }
        }

        [TestMethod]
        public async Task WatchReplaysConfirmedIncomingOnlyAndCheckpointsAfterAcknowledgement()
        {
            var incoming = RpcHandler.Payment(ledger: 101);
            var failed = RpcHandler.Payment(new string('B', 64), 101, "tecNO_PERMISSION"); failed["meta"]["TransactionIndex"] = 1;
            var outgoing = RpcHandler.Payment(new string('C', 64), 101); outgoing["meta"]["TransactionIndex"] = 2;
            outgoing["tx_json"]["Account"] = TestTransactions.Destination; outgoing["tx_json"]["Destination"] = TestTransactions.Account;
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, incoming, incoming, failed, outgoing)) };
            var socket = new Socket(); int received = 0, saved = 0;
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => socket;
                var watch = client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (payment, ct) => {
                    received++; Assert.AreEqual(1, saved); Assert.AreEqual(0u, payment.DestinationTag);
                    Assert.AreEqual(1m, payment.DeliveredAmountXrp); return Task.CompletedTask;
                }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (checkpoint, ct) => {
                    saved++; if (saved == 2) { Assert.AreEqual(1, received); cancel.Cancel(); } return Task.CompletedTask;
                } }, cancel.Token);
                await Cancelled(watch);
                Assert.AreEqual(1, received); Assert.AreEqual(2, saved); Assert.IsTrue(socket.Disposed);
                Assert.IsNull(socket.Subscription["accounts_proposed"]);
            }
        }

        [TestMethod]
        public async Task CallbackFailureStopsWatchWithoutAdvancingCheckpoint()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, RpcHandler.Payment(ledger: 101))) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new Socket(); int saved = 0;
                var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (payment, ct) => throw new HttpRequestException("database offline"), new PaymentWatchOptions {
                        FromLedger = 101, SaveCheckpointAsync = (s, ct) => { saved++; return Task.CompletedTask; } }));
                Assert.AreEqual("database offline", ex.Message); Assert.AreEqual(1, saved);
            }
        }

        [TestMethod]
        public async Task MissingHistoryOrDeliveredAmountStopsWithoutCrediting()
        {
            bool missingRange = true;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(101));
                var payment = RpcHandler.Payment(ledger: 101); payment["meta"]["delivered_amount"] = "unavailable";
                return Task.FromResult(RpcHandler.History(TestTransactions.Destination, missingRange ? 102u : 101u, 101,
                    missingRange ? new JObject[0] : new[] { payment }));
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new Socket(); int received = 0;
                Func<TransactionInfo, CancellationToken, Task> callback = (p, ct) => { received++; return Task.CompletedTask; };
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination, callback,
                    new PaymentWatchOptions { FromLedger = 101 }));
                missingRange = false;
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination, callback,
                    new PaymentWatchOptions { FromLedger = 101 }));
                Assert.AreEqual(0, received);
            }
        }

        [TestMethod]
        public async Task RestartUsesSavedCheckpointAndRejectsDifferentAccount()
        {
            string checkpoint = null; uint historyFrom = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(105));
                historyFrom = (uint)p["ledger_index_min"];
                return Task.FromResult(RpcHandler.History(TestTransactions.Destination, historyFrom, 105));
            } };
            using (var http = new HttpClient(handler))
            {
                using (var client = new XrplClient(Options(), http))
                using (var cancel = new CancellationTokenSource())
                {
                    client.SocketFactory = () => new Socket(); int saves = 0;
                    await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask,
                        new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (s, ct) => {
                            checkpoint = s; if (++saves == 2) cancel.Cancel(); return Task.CompletedTask;
                        } }, cancel.Token));
                }
                handler.Respond = (m, p, ct) => {
                    if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(106));
                    historyFrom = (uint)p["ledger_index_min"];
                    return Task.FromResult(RpcHandler.History(TestTransactions.Destination, historyFrom, 106));
                };
                using (var client = new XrplClient(Options(), http))
                using (var cancel = new CancellationTokenSource())
                {
                    client.SocketFactory = () => new Socket();
                    await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask,
                        new PaymentWatchOptions { Checkpoint = checkpoint, SaveCheckpointAsync = (s, ct) => { cancel.Cancel(); return Task.CompletedTask; } }, cancel.Token));
                    Assert.AreEqual(106u, historyFrom);
                    await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Account,
                        (p, ct) => Task.CompletedTask, new PaymentWatchOptions { Checkpoint = checkpoint }));
                }
            }
        }

        [TestMethod]
        public async Task ReconnectCatchesUpWithoutTrustingLiveTransactionPayload()
        {
            uint ledger = 100; int sockets = 0, received = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(ledger) :
                RpcHandler.History(TestTransactions.Destination, (uint)p["ledger_index_min"], (uint)p["ledger_index_max"], RpcHandler.Payment(ledger: 101))) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => {
                    int attempt = ++sockets;
                    return new Socket { Connected = () => { if (attempt == 2) ledger = 101; },
                        Receive = (i, ct) => throw new WebSocketException("disconnected") };
                };
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                    received++; return Task.CompletedTask;
                }, new PaymentWatchOptions { SaveCheckpointAsync = (s, ct) => {
                    if (received == 1) cancel.Cancel(); return Task.CompletedTask;
                } }, cancel.Token));
                Assert.AreEqual(1, received); Assert.AreEqual(2, sockets);
            }
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task ClientDisposalCancelsAnIdleWatchAndPreventsDuplicateWatch()
        {
            var ready = new TaskCompletionSource<bool>();
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(RpcHandler.Ledger()) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => new Socket { Receive = async (i, ct) => {
                    ready.TrySetResult(true); await Task.Delay(10000, ct); return "{}";
                } };
                var watch = client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask);
                await TestAwait.UntilAsync(ready.Task);
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask));
                client.Dispose(); await Cancelled(watch);
            }
        }
    }
}
