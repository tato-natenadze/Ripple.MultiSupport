using System;
using System.IO;
using System.Net.WebSockets;
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
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        public Task ConnectAsync(Uri endpoint, CancellationToken token) => _socket.ConnectAsync(endpoint, token);
        public Task SendAsync(string message, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(message);
            return _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, token);
        }
        public async Task<string> ReceiveAsync(CancellationToken token)
        {
            using (var data = new MemoryStream())
            {
                var buffer = new byte[8192];
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("XRPL closed the connection.");
                    if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Expected a text WebSocket message.");
                    if (data.Length + result.Count > 16 * 1024 * 1024) throw new InvalidDataException("WebSocket message is too large.");
                    data.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return new UTF8Encoding(false, true).GetString(data.ToArray());
            }
        }
        public void Dispose() => _socket.Dispose();
    }
}
