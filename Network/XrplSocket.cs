using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ripple
{
    internal interface IXrplSocket : IDisposable
    {
        Task ConnectAsync(Uri endpoint, CancellationToken token);
        Task SendAsync(string message, CancellationToken token);
        Task<string> ReceiveAsync(CancellationToken token);
    }

    internal sealed class XrplSocket : IXrplSocket
    {
        private readonly ClientWebSocket _client = new ClientWebSocket();
        private WebSocket _socket;
        private readonly HttpMessageInvoker _handshakeHttp;
#if !NET8_0_OR_GREATER
        private readonly Func<Uri, HttpMessageInvoker, CancellationToken, Task> _connectWithHttp;
        private readonly Func<Stream, bool, string, TimeSpan, WebSocket> _createFromStream;
        private HttpResponseMessage _streamResponse;
        private HttpWebRequest _legacyRequest;
        private HttpWebResponse _legacyResponse;
#endif

        internal XrplSocket()
        {
            _socket = _client;
#if NET8_0_OR_GREATER
            _handshakeHttp = new HttpMessageInvoker(new HandshakeHandler());
#else
            // Use the public custom-transport overload when the consuming runtime
            // provides it, even when this assembly was built for .NET Standard.
            var connect = typeof(ClientWebSocket).GetMethod("ConnectAsync", new[] {
                typeof(Uri), typeof(HttpMessageInvoker), typeof(CancellationToken) });
            if (connect != null)
            {
                _connectWithHttp = (Func<Uri, HttpMessageInvoker, CancellationToken, Task>)Delegate.CreateDelegate(
                    typeof(Func<Uri, HttpMessageInvoker, CancellationToken, Task>), _client, connect);
                _handshakeHttp = new HttpMessageInvoker(new HandshakeHandler());
            }
            else
            {
                var create = typeof(WebSocket).GetMethod("CreateFromStream", new[] {
                    typeof(Stream), typeof(bool), typeof(string), typeof(TimeSpan) });
                if (create != null)
                {
                    _createFromStream = (Func<Stream, bool, string, TimeSpan, WebSocket>)Delegate.CreateDelegate(
                        typeof(Func<Stream, bool, string, TimeSpan, WebSocket>), create);
                    _handshakeHttp = new HttpMessageInvoker(new HandshakeHandler());
                }
            }
#endif
        }

        private sealed class HandshakeHandler : DelegatingHandler
        {
            internal HandshakeHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var response = await base.SendAsync(request, token).ConfigureAwait(false);
                // This client requests neither extensions nor a subprotocol.
                if (response.StatusCode == HttpStatusCode.SwitchingProtocols &&
                    (response.Headers.Contains("Sec-WebSocket-Extensions") || response.Headers.Contains("Sec-WebSocket-Protocol")))
                {
                    response.Dispose();
                    throw new WebSocketException("The server selected an unrequested WebSocket extension or subprotocol.");
                }
                return response;
            }
        }

        public async Task ConnectAsync(Uri endpoint, CancellationToken token)
        {
            try
            {
#if NET8_0_OR_GREATER
                await _client.ConnectAsync(endpoint, _handshakeHttp, token).ConfigureAwait(false);
#else
                if (_connectWithHttp != null) await _connectWithHttp(endpoint, _handshakeHttp, token).ConfigureAwait(false);
                else if (_createFromStream != null) await ConnectStreamAsync(endpoint, token).ConfigureAwait(false);
                else await ConnectFrameworkAsync(endpoint, token).ConfigureAwait(false);
#endif
            }
            catch (Exception ex) when (token.IsCancellationRequested &&
                (ex is WebSocketException || ex is WebException || ex is ObjectDisposedException))
            { throw new OperationCanceledException("WebSocket connection cancelled.", ex, token); }
        }

#if !NET8_0_OR_GREATER
        private async Task ConnectStreamAsync(Uri endpoint, CancellationToken token)
        {
            var uri = new UriBuilder(endpoint) { Scheme = endpoint.Scheme == "wss" ? "https" : "http", Port = endpoint.Port }.Uri;
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                var nonce = new byte[16];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
                string key = Convert.ToBase64String(nonce);
                request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
                request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
                request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", key);
                HttpResponseMessage response;
                try { response = await _handshakeHttp.SendAsync(request, token).ConfigureAwait(false); }
                catch (HttpRequestException ex) { throw new WebSocketException("WebSocket connection failed.", ex); }
                try
                {
                    ValidateUpgrade(response.StatusCode, key, name => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null);
                    var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    _socket = _createFromStream(stream, false, null, _client.Options.KeepAliveInterval);
                    _streamResponse = response;
                }
                catch { response.Dispose(); throw; }
            }
        }

        private static void ValidateUpgrade(HttpStatusCode status, string key, Func<string, string> header)
        {
            // RFC 6455 section 4.1: validate the opening handshake before framing.
            string accept;
            using (var sha = SHA1.Create())
                accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            bool upgrade = false;
            foreach (string part in (header("Connection") ?? "").Split(','))
                if (string.Equals(part.Trim(), "Upgrade", StringComparison.OrdinalIgnoreCase)) upgrade = true;
            if (status != HttpStatusCode.SwitchingProtocols || string.IsNullOrEmpty(key) ||
                !string.Equals(header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase) || !upgrade ||
                !string.Equals(header("Sec-WebSocket-Accept"), accept, StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(header("Sec-WebSocket-Protocol")) || !string.IsNullOrEmpty(header("Sec-WebSocket-Extensions")))
                throw new WebSocketException("The server did not return a valid WebSocket upgrade.");
        }

        private async Task ConnectFrameworkAsync(Uri endpoint, CancellationToken token)
        {
            // .NET Framework's ClientWebSocket does not expose redirect control.
            // Use its registered WebSocket HTTP request and the same public BCL
            // framing factory, with an explicit RFC 6455 handshake validation.
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.AllowAutoRedirect = false;
            request.Proxy = _client.Options.Proxy;
            _legacyRequest = request;
            try
            {
                using (token.Register(request.Abort))
                {
                    var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        ValidateUpgrade(response.StatusCode, request.Headers["Sec-WebSocket-Key"], name => response.Headers[name]);
                        _socket = WebSocket.CreateClientWebSocket(response.GetResponseStream(), null, 4096, 4096,
                            _client.Options.KeepAliveInterval, false, WebSocket.CreateClientBuffer(4096, 4096));
                        _legacyResponse = response;
                    }
                    catch { response.Dispose(); throw; }
                }
            }
            catch (WebException ex) when (!token.IsCancellationRequested)
            { throw new WebSocketException("WebSocket connection failed.", ex); }
            finally { _legacyRequest = null; }
        }
#endif

        public async Task SendAsync(string message, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(message);
            try { await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, token).ConfigureAwait(false); }
            catch (WebSocketException ex) when (token.IsCancellationRequested)
            { throw new OperationCanceledException("WebSocket send cancelled.", ex, token); }
        }
        public async Task<string> ReceiveAsync(CancellationToken token)
        {
            using (var data = new MemoryStream())
            {
                var buffer = new byte[8192];
                WebSocketReceiveResult result;
                do
                {
                    try { result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false); }
                    catch (WebSocketException ex) when (token.IsCancellationRequested)
                    { throw new OperationCanceledException("WebSocket receive cancelled.", ex, token); }
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("XRPL closed the connection.");
                    if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Expected a text WebSocket message.");
                    if (data.Length + result.Count > 16 * 1024 * 1024) throw new InvalidDataException("WebSocket message is too large.");
                    data.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return new UTF8Encoding(false, true).GetString(data.ToArray());
            }
        }
        public void Dispose()
        {
#if !NET8_0_OR_GREATER
            _legacyRequest?.Abort();
            _legacyResponse?.Dispose();
            _streamResponse?.Dispose();
#endif
            _socket.Dispose();
            if (!ReferenceEquals(_socket, _client)) _client.Dispose();
            _handshakeHttp?.Dispose();
        }
    }
}
