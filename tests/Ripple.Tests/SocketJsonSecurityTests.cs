using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Ripple.Tests
{
    [TestClass]
    public class SocketJsonSecurityTests
    {
        [DataTestMethod]
        [DataRow(true, "{\"id\":\"payments\",\"status\":\"error\",\"status\":\"success\",\"result\":{}}")]
        [DataRow(true, "{\"id\":\"payments\",\"status\":\"success\",\"result\":{},\"value\":NaN}")]
        [DataRow(true, "{\"id\":\"payments\",\"status\":\"success\",\"result\":{},\"value\":undefined}")]
        [DataRow(true, "{\"id\":\"payments\",\"status\":\"success\",\"result\":{},\"value\":new Date(1)}")]
        [DataRow(false, "{\"type\":\"ledgerClosed\",\"type\":\"transaction\"}")]
        [DataRow(false, "{\"type\":\"ledgerClosed\",\"value\":Infinity}")]
        public async Task InvalidSocketJsonStopsWithoutCreditingOrAdvancingCheckpoint(bool acknowledgement, string payload)
        {
            var options = RpcHandler.Options();
            options.WebSocketEndpoint = new Uri("wss://xrpl.example/");
            var handler = new RpcHandler { Respond = (method, args, token) => Task.FromResult(RpcHandler.Ledger(100)) };
            var socket = new JsonSocket(acknowledgement, payload);
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                int credited = 0, saved = 0;
                client.SocketFactory = () => socket;
                await Assert.ThrowsExceptionAsync<JsonReaderException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (payment, token) => { credited++; return Task.CompletedTask; }, new PaymentWatchOptions {
                        FromLedger = 101, SaveCheckpointAsync = (value, token) => { saved++; return Task.CompletedTask; }
                    }, deadline.Token));
                Assert.AreEqual(0, credited);
                Assert.AreEqual(1, saved, "Only the initial checkpoint may be saved.");
                Assert.AreEqual(acknowledgement ? 0 : 1, handler.Requests.Count);
                Assert.IsTrue(socket.Disposed);
            }
        }

        private sealed class JsonSocket : IXrplSocket
        {
            private readonly bool _acknowledgement;
            private readonly string _payload;
            private int _reads;
            internal bool Disposed;
            internal JsonSocket(bool acknowledgement, string payload) { _acknowledgement = acknowledgement; _payload = payload; }
            public Task ConnectAsync(Uri endpoint, CancellationToken token) => Task.CompletedTask;
            public Task SendAsync(string message, CancellationToken token) => Task.CompletedTask;
            public Task<string> ReceiveAsync(CancellationToken token)
            {
                int read = ++_reads;
                if ((_acknowledgement && read == 1) || (!_acknowledgement && read == 2)) return Task.FromResult(_payload);
                if (read == 1) return Task.FromResult("{\"id\":\"payments\",\"status\":\"success\",\"result\":{}}");
                throw new InvalidOperationException("Monitoring continued after invalid JSON.");
            }
            public void Dispose() { Disposed = true; }
        }
    }
}
