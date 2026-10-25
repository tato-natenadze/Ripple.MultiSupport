using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    [TestClass]
    public class HttpSecurityBoundaryTests
    {
        [DataTestMethod]
        [DataRow("bad-accept")]
        [DataRow("bad-upgrade")]
        [DataRow("bad-connection")]
        [DataRow("unsolicited-protocol")]
        [DataRow("unsolicited-extension")]
        public async Task InvalidWebSocketUpgradeCannotStartHistoryReads(string invalid)
        {
            var handler = new RawHandler(Encoding.UTF8.GetBytes("{}"));
            using (var server = new SingleReplyServer(invalid, "", true))
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(new XrplClientOptions { RpcEndpoint = RpcHandler.Options().RpcEndpoint,
                WebSocketEndpoint = new UriBuilder(server.Endpoint) { Scheme = "ws" }.Uri,
                PollInterval = TimeSpan.FromMilliseconds(10), RequestTimeout = TimeSpan.FromMilliseconds(250) }, http))
            {
                try
                {
                    try { await client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                        (tx, token) => throw new AssertFailedException("Invalid handshake credited a payment."),
                        new PaymentWatchOptions { FromLedger = 101 }, stop.Token); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    Assert.IsTrue(server.Requests > 0); Assert.AreEqual(0, handler.Requests);
                }
                finally { server.Stop(); await server.Completion; }
            }
        }

        [TestMethod]
        public async Task RealWebSocketUpgradeCanDeliverAConfirmedPaymentAndCancel()
        {
            using (var server = new SingleReplyServer("", "", true))
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using (var http = new HttpClient(new RpcHandler { Respond = (method, args, token) => Task.FromResult(
                method == "server_info" ? RpcHandler.Ledger(101) : RpcHandler.History(TestTransactions.Destination,
                    101, 101, RpcHandler.Payment(ledger: 101))) }))
            using (var client = new XrplClient(new XrplClientOptions { RpcEndpoint = RpcHandler.Options().RpcEndpoint,
                WebSocketEndpoint = new UriBuilder(server.Endpoint) { Scheme = "ws" }.Uri }, http))
            {
                int received = 0;
                try
                {
                    try { await client.WatchIncomingPaymentsAsync(TestTransactions.Destination, (tx, token) => {
                        Assert.AreEqual(TransactionStatus.Succeeded, tx.Status); Assert.AreEqual(1m, tx.DeliveredAmountXrp);
                        received++; stop.Cancel(); return Task.CompletedTask;
                    }, new PaymentWatchOptions { FromLedger = 101 }, stop.Token); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    Assert.AreEqual(1, received); Assert.AreEqual(1, server.Requests);
                }
                finally { server.Stop(); await server.Completion; }
            }
        }

        [DataTestMethod]
        [DataRow("http")]
        [DataRow("ws")]
        public async Task WebSocketHandshakeDoesNotFollowARedirectToAnotherEndpoint(string scheme)
        {
            using (var target = new SingleReplyServer("HTTP/1.1 200 OK\r\n", ""))
            using (var source = new SingleReplyServer("HTTP/1.1 307 Temporary Redirect\r\nLocation: " +
                new UriBuilder(target.Endpoint) { Scheme = scheme }.Uri + "\r\n", ""))
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            using (var http = new HttpClient(new RawHandler(Encoding.UTF8.GetBytes("{}"))))
            using (var client = new XrplClient(new XrplClientOptions { RpcEndpoint = RpcHandler.Options().RpcEndpoint,
                WebSocketEndpoint = new UriBuilder(source.Endpoint) { Scheme = "ws" }.Uri,
                PollInterval = TimeSpan.FromMilliseconds(10), RequestTimeout = TimeSpan.FromMilliseconds(250) }, http))
            {
                int credits = 0;
                try
                {
                    try { await client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                        (tx, token) => { credits++; return Task.CompletedTask; }, new PaymentWatchOptions { FromLedger = 101 }, stop.Token); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    Assert.IsTrue(source.Requests > 0);
                    Assert.AreEqual(0, target.Requests, "A WebSocket redirect must not trigger another service request.");
                    Assert.AreEqual(0, credits);
                }
                finally { source.Stop(); target.Stop(); await Task.WhenAll(source.Completion, target.Completion); }
            }
        }

        [TestMethod]
        public async Task DefaultTransportDoesNotFollowAServerRedirectToAnotherEndpoint()
        {
            using (var target = new SingleReplyServer("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n",
                "{\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"}}}"))
            using (var source = new SingleReplyServer("HTTP/1.1 307 Temporary Redirect\r\nLocation: " + target.Endpoint + "\r\n", ""))
            using (var client = new XrplClient(new XrplClientOptions {
                RpcEndpoint = source.Endpoint, RequestTimeout = TimeSpan.FromSeconds(5) }))
            {
                try
                {
                    await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetFeeAsync());
                    Assert.AreEqual(1, source.Requests);
                    Assert.AreEqual(0, target.Requests, "Untrusted server redirects must not trigger requests to another service.");
                }
                finally
                {
                    source.Stop(); target.Stop();
                    await Task.WhenAll(source.Completion, target.Completion);
                }
            }
        }

        [DataTestMethod]
        [DataRow("{\"result\":{},\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"}}}")]
        [DataRow("{\"result\":{\"drops\":{\"base_fee\":\"999\",\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"}}}")]
        [DataRow("{\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"},\"x\":NaN}}")]
        [DataRow("{\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"},\"x\":new Date(1)}}")]
        [DataRow("{\"result\":{\"drops\":{\"base_fee\":\"10\",\"open_ledger_fee\":\"10\"}}}{}")]
        public async Task AmbiguousOrExecutableLookingJsonCannotProduceAFee(string json)
        {
            using (var http = new HttpClient(new RawHandler(Encoding.UTF8.GetBytes(json))))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetFeeAsync());
        }

        [TestMethod]
        public async Task InvalidUtf8AndDeepJsonAreRejectedThroughThePublicFeeMethod()
        {
            var bytes = Encoding.UTF8.GetBytes("{\"result\":{\"x\":\"?\"}}"); bytes[Array.IndexOf(bytes, (byte)'?')] = 255;
            using (var http = new HttpClient(new RawHandler(bytes)))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetFeeAsync());
            string nested = new string('[', 1000) + "0" + new string(']', 1000);
            using (var http = new HttpClient(new RawHandler(Encoding.UTF8.GetBytes("{\"result\":{\"x\":" + nested + "}}"))))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetFeeAsync());
        }

        private sealed class RawHandler : HttpMessageHandler
        {
            private readonly byte[] _bytes;
            internal int Requests;
            internal RawHandler(byte[] bytes) { _bytes = bytes; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { Interlocked.Increment(ref Requests); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) }); }
        }

        private sealed class SingleReplyServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private TcpClient _connection;
            private volatile bool _stopping;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private int _requests;
            internal int Requests => Volatile.Read(ref _requests);
            internal Uri Endpoint { get; }
            internal Task Completion { get; }
            internal SingleReplyServer(string headers, string body, bool upgrade = false)
            {
                _listener.Start();
                Endpoint = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/");
                Completion = ServeAsync(headers, body, upgrade);
            }
            private async Task ServeAsync(string headers, string body, bool upgrade)
            {
                try
                {
                    using (var connection = await _listener.AcceptTcpClientAsync())
                    {
                        _connection = connection;
                        if (_stopping) return;
                        using (var stream = connection.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                        {
                            Interlocked.Increment(ref _requests);
                            string line; int length = 0; string key = null;
                            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                            {
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                    length = int.Parse(line.Substring("Content-Length:".Length).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                                    key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                            }
                            var buffer = new char[1024];
                            while (length > 0)
                            {
                                int read = await reader.ReadAsync(buffer, 0, Math.Min(length, buffer.Length));
                                if (read == 0) break;
                                length -= read;
                            }
                            if (upgrade)
                            {
                                string accept;
                                using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(
                                    Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                                if (headers == "bad-accept") accept = Convert.ToBase64String(new byte[20]);
                                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: " +
                                    (headers == "bad-upgrade" ? "invalid" : "websocket") + "\r\nConnection: " +
                                    (headers == "bad-connection" ? "close" : "Upgrade") + "\r\nSec-WebSocket-Accept: " + accept + "\r\n" +
                                    (headers == "unsolicited-protocol" ? "Sec-WebSocket-Protocol: unexpected\r\n" : "") +
                                    (headers == "unsolicited-extension" ? "Sec-WebSocket-Extensions: permessage-deflate\r\n" : "") + "\r\n");
                                await stream.WriteAsync(response, 0, response.Length);
                                byte[] ack = Encoding.UTF8.GetBytes("{\"id\":\"payments\",\"status\":\"success\",\"result\":{}}");
                                byte[] frame = new byte[ack.Length + 2]; frame[0] = 0x81; frame[1] = (byte)ack.Length;
                                Buffer.BlockCopy(ack, 0, frame, 2, ack.Length); await stream.WriteAsync(frame, 0, frame.Length);
                                await Task.Delay(Timeout.Infinite, _stop.Token);
                                return;
                            }
                            byte[] reply = Encoding.UTF8.GetBytes(headers + "Content-Length: " + Encoding.UTF8.GetByteCount(body) +
                                "\r\nConnection: close\r\n\r\n" + body);
                            await stream.WriteAsync(reply, 0, reply.Length);
                        }
                    }
                }
                catch (Exception ex) when (_stopping && (ex is SocketException || ex is ObjectDisposedException || ex is IOException || ex is OperationCanceledException)) { }
            }
            internal void Stop() { if (_stopping) return; _stopping = true; _stop.Cancel(); _listener.Stop(); _connection?.Dispose(); }
            public void Dispose() { Stop(); _stop.Dispose(); }
        }
    }
}
