using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    [TestClass]
    public class HttpSecurityBoundaryTests
    {
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
            internal RawHandler(byte[] bytes) { _bytes = bytes; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) }); }
        }

        private sealed class SingleReplyServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private TcpClient _connection;
            private volatile bool _stopping;
            private int _requests;
            internal int Requests => Volatile.Read(ref _requests);
            internal Uri Endpoint { get; }
            internal Task Completion { get; }
            internal SingleReplyServer(string headers, string body)
            {
                _listener.Start();
                Endpoint = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/");
                Completion = ServeAsync(headers, body);
            }
            private async Task ServeAsync(string headers, string body)
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
                            string line; int length = 0;
                            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                    length = int.Parse(line.Substring("Content-Length:".Length).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                            var buffer = new char[1024];
                            while (length > 0)
                            {
                                int read = await reader.ReadAsync(buffer, 0, Math.Min(length, buffer.Length));
                                if (read == 0) break;
                                length -= read;
                            }
                            byte[] response = Encoding.UTF8.GetBytes(headers + "Content-Length: " + Encoding.UTF8.GetByteCount(body) +
                                "\r\nConnection: close\r\n\r\n" + body);
                            await stream.WriteAsync(response, 0, response.Length);
                        }
                    }
                }
                catch (Exception ex) when (_stopping && (ex is SocketException || ex is ObjectDisposedException || ex is IOException)) { }
            }
            internal void Stop() { _stopping = true; _listener.Stop(); _connection?.Dispose(); }
            public void Dispose() { Stop(); }
        }
    }
}
