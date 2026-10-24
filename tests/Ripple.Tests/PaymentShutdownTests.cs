using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentShutdownTests
    {
        // Schedule shutdown after the complete tx response has arrived but before
        // receipt recovery starts its next RPC. No timing-dependent sleeps.
        private sealed class EndOfResponseStream : MemoryStream
        {
            private readonly Action _onEnd;
            private bool _ended;
            internal EndOfResponseStream(byte[] bytes, Action onEnd) : base(bytes) { _onEnd = onEnd; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                int read = base.Read(buffer, offset, count);
                if (read == 0 && !_ended) { _ended = true; _onEnd(); }
                return Task.FromResult(read);
            }
        }

        private sealed class EndOfResponseContent : HttpContent
        {
            private readonly byte[] _bytes;
            private readonly Action _onEnd;
            internal EndOfResponseContent(string value, Action onEnd) { _bytes = Encoding.UTF8.GetBytes(value); _onEnd = onEnd; }
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new EndOfResponseStream(_bytes, _onEnd));
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => stream.WriteAsync(_bytes, 0, _bytes.Length);
            protected override bool TryComputeLength(out long length) { length = _bytes.Length; return true; }
        }

        private sealed class ShutdownHandler : DelegatingHandler
        {
            internal Action OnTransactionResponse;
            internal ShutdownHandler(HttpMessageHandler inner) : base(inner) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var body = JObject.Parse(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                var response = await base.SendAsync(request, token).ConfigureAwait(false);
                if ((string)body["method"] == "tx")
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.Content.Dispose();
                    response.Content = new EndOfResponseContent(json, OnTransactionResponse);
                }
                return response;
            }
        }

        [DataTestMethod]
        [DataRow(true, false)]
        [DataRow(true, true)]
        [DataRow(false, false)]
        [DataRow(false, true)]
        public async Task ShutdownDuringRecoveryReturnsTheReceipt(bool dispose, bool persist)
        {
            var server = new PaymentTests.Server { NeverFind = true };
            var rpc = new RpcHandler { Respond = server.Respond };
            var handler = new ShutdownHandler(rpc);
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                handler.OnTransactionResponse = () => { if (dispose) client.Dispose(); else cancel.Cancel(); };
                PaymentReceipt saved = null;
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                if (persist) payment.OnPreparedAsync = (r, ct) => { saved = r; return Task.CompletedTask; };
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed, payment, cancel.Token);
                Assert.AreEqual(TransactionStatus.Unknown, result.Status);
                Assert.IsNotNull(result.Receipt);
                Assert.AreEqual(PaymentErrorKind.Cancelled, result.Error.Kind);
                if (persist) Assert.AreEqual(saved.Hash, result.Receipt.Hash);
                Assert.AreEqual(1, server.Signed.Count);
                Assert.AreEqual(1, rpc.Requests.Count(r => (string)r["method"] == "submit"));
                Assert.IsFalse(JObject.FromObject(result).ToString().Contains(TestTransactions.SecpSeed));
                Assert.IsFalse(rpc.Disposed, "Injected HTTP client remains caller-owned.");
            }
        }

        [TestMethod]
        public async Task DisposalDuringAFinalResponsePreservesTheValidatedOutcome()
        {
            var server = new PaymentTests.Server();
            var handler = new ShutdownHandler(new RpcHandler { Respond = server.Respond });
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                handler.OnTransactionResponse = client.Dispose;
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 });
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status);
                Assert.IsTrue(result.Transaction.Validated);
                Assert.IsNull(result.Error);
                Assert.AreEqual(1, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task DisposalDuringPreparationDoesNotSubmit()
        {
            var server = new PaymentTests.Server();
            using (var http = new HttpClient(new RpcHandler { Respond = server.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                try
                {
                    await client.SendXrpAsync(TestTransactions.SecpSeed, new XrpPaymentRequest {
                        Destination = TestTransactions.Destination, AmountXrp = 1,
                        OnPreparedAsync = (r, ct) => { client.Dispose(); return Task.CompletedTask; } });
                    Assert.Fail("Expected cancellation before submission.");
                }
                catch (OperationCanceledException) { }
                Assert.AreEqual(0, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task AlreadyDisposedClientRejectsSendWithoutIO()
        {
            var rpc = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O.") };
            using (var http = new HttpClient(rpc))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                client.Dispose();
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 }));
                Assert.AreEqual(0, rpc.Requests.Count);
            }
        }
    }
}
