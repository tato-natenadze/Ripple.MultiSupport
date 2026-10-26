using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentWatchRecoveryTests
    {
        private static XrplClientOptions Options()
        {
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/"); return options;
        }
        private static uint NextLedger(string checkpoint) => (uint)JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(checkpoint)))["next_ledger"];
        private static JObject Error(string code) => new JObject { ["error"] = code, ["status"] = "error" };
        private static async Task Cancelled(Task task)
        {
            try { await task; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
        }

        [DataTestMethod]
        [DataRow("Transport")]
        [DataRow("tooBusy")]
        [DataRow("noNetwork")]
        [DataRow("noCurrent")]
        [DataRow("noClosed")]
        [DataRow("failedToForward")]
        [Timeout(10000)]
        public async Task InitialLedgerFailureRecoversBeforeSavingTheStartCheckpoint(string failure)
        {
            int tips = 0, sockets = 0, payments = 0;
            var saved = new List<uint>();
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info")
                {
                    tips++;
                    if (tips == 1)
                    {
                        if (failure == "Transport") throw new HttpRequestException("temporary outage");
                        return Task.FromResult(Error(failure));
                    }
                    return Task.FromResult(RpcHandler.Ledger(tips == 2 ? 100u : 101u));
                }
                return Task.FromResult(RpcHandler.History(TestTransactions.Destination, 101, 101, RpcHandler.Payment(ledger: 101)));
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                    Assert.AreEqual(1, saved.Count); payments++; return Task.CompletedTask;
                }, new PaymentWatchOptions { SaveCheckpointAsync = (checkpoint, ct) => {
                    saved.Add(NextLedger(checkpoint)); if (saved.Count == 2) cancel.Cancel(); return Task.CompletedTask;
                } }, cancel.Token));
                CollectionAssert.AreEqual(new uint[] { 101, 102 }, saved);
                Assert.AreEqual(1, payments); Assert.AreEqual(3, tips); Assert.AreEqual(1, sockets);
                Assert.IsTrue(handler.Requests.TrueForAll(r => (string)r["params"][0]["api_version"] == "2"));
            }
        }

        [DataTestMethod]
        [DataRow("tooBusy")]
        [DataRow("noNetwork")]
        [DataRow("noCurrent")]
        [DataRow("noClosed")]
        [DataRow("failedToForward")]
        [Timeout(10000)]
        public async Task TransientRpcFailureAfterSubscriptionReconnectsAndCatchesUp(string failure)
        {
            int tips = 0, sockets = 0, payments = 0, saved = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info"
                ? (++tips == 1 ? Error(failure) : RpcHandler.Ledger(101))
                : RpcHandler.History(TestTransactions.Destination, 101, 101, RpcHandler.Payment(ledger: 101))) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => {
                    Assert.AreEqual(1, saved); payments++; return Task.CompletedTask;
                }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (checkpoint, ct) => {
                    saved++; if (saved == 2) cancel.Cancel(); return Task.CompletedTask;
                } }, cancel.Token));
                Assert.AreEqual(2, sockets); Assert.AreEqual(2, tips);
                Assert.AreEqual(1, payments); Assert.AreEqual(2, saved);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task PermanentRpcErrorsStillStopWithoutRetrying(bool explicitRange)
        {
            int tips = 0, sockets = 0;
            var handler = new RpcHandler { Respond = (m, p, ct) => { tips++; return Task.FromResult(Error("invalidParams")); } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                var error = await Assert.ThrowsExceptionAsync<XrplRpcException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => Task.CompletedTask, new PaymentWatchOptions { FromLedger = explicitRange ? 101u : (uint?)null }));
                Assert.AreEqual("invalidParams", error.Code); Assert.AreEqual(1, tips);
                Assert.AreEqual(explicitRange ? 1 : 0, sockets);
            }
        }

        [TestMethod]
        public async Task CallbackRpcErrorsAreNeverRetriedOrCheckpointed()
        {
            int sockets = 0, saved = 0;
            var expected = new XrplRpcException("callback", "tooBusy", "storage busy");
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101)
                : RpcHandler.History(TestTransactions.Destination, 101, 101, RpcHandler.Payment(ledger: 101))) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            {
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                var actual = await Assert.ThrowsExceptionAsync<XrplRpcException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => throw expected, new PaymentWatchOptions { FromLedger = 101,
                        SaveCheckpointAsync = (s, ct) => { saved++; return Task.CompletedTask; } }));
                Assert.AreSame(expected, actual); Assert.AreEqual(1, sockets); Assert.AreEqual(1, saved);
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public async Task TransientHistoryFailureReplaysTheUnfinishedBatchBeforeCheckpointing()
        {
            int pages = 0, sockets = 0;
            var first = RpcHandler.Payment(ledger: 101);
            var second = RpcHandler.Payment(new string('B', 64), 101); second["meta"]["TransactionIndex"] = 1;
            var delivered = new List<string>(); var saved = new List<uint>();
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info") return Task.FromResult(RpcHandler.Ledger(101));
                pages++;
                if (pages == 2) return Task.FromResult(Error("tooBusy"));
                var response = RpcHandler.History(TestTransactions.Destination, 101, 101,
                    pages == 1 ? new[] { first } : new[] { first, second });
                if (pages == 1) response["marker"] = new JObject { ["page"] = 2 };
                return Task.FromResult(response);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                client.SocketFactory = () => { sockets++; return new PaymentWatchTests.Socket(); };
                await Cancelled(client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (payment, ct) => {
                    Assert.AreEqual(1, saved.Count); delivered.Add(payment.Hash); return Task.CompletedTask;
                }, new PaymentWatchOptions { FromLedger = 101, SaveCheckpointAsync = (checkpoint, ct) => {
                    saved.Add(NextLedger(checkpoint));
                    if (saved.Count == 2) { Assert.AreEqual(3, delivered.Count); cancel.Cancel(); }
                    return Task.CompletedTask;
                } }, cancel.Token));
                CollectionAssert.AreEqual(new[] { (string)first["hash"], (string)first["hash"], (string)second["hash"] }, delivered);
                CollectionAssert.AreEqual(new uint[] { 101, 102 }, saved);
                Assert.AreEqual(3, pages); Assert.AreEqual(2, sockets);
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public async Task CancellationStopsInitialRecoveryDuringBackoff()
        {
            int requests = 0;
            var firstRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var options = Options(); options.PollInterval = TimeSpan.FromSeconds(10);
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                requests++; firstRequest.TrySetResult(true); throw new HttpRequestException("temporary outage");
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            using (var cancel = new CancellationTokenSource())
            {
                var watch = client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (p, ct) => Task.CompletedTask, cancellationToken: cancel.Token);
                await TestAwait.UntilAsync(firstRequest.Task);
                Assert.AreEqual(1, requests);
                cancel.CancelAfter(100); await Cancelled(watch);
                Assert.AreEqual(1, requests);
            }
        }
    }
}
