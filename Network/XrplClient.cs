using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    /// <summary>Async XRPL payment client for .NET Standard 2.0 and .NET 8. Construction performs no I/O.</summary>
    public sealed partial class XrplClient : IDisposable
    {
        private readonly XrplClientOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private int _disposed;
        private const int MaximumResponseBytes = 16 * 1024 * 1024;

        public XrplClient(XrplClientOptions options) : this(options, null) { }

        /// <param name="httpClient">Optional caller-owned HTTP client; it is never disposed by this class.</param>
        public XrplClient(XrplClientOptions options, HttpClient httpClient = null)
        {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            _ownsHttp = httpClient == null;
            _http = httpClient ?? new HttpClient();
        }

        internal async Task<JObject> RpcAsync(string method, JObject parameters, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
            {
                timeout.CancelAfter(_options.RequestTimeout);
                var args = parameters == null ? new JObject() : (JObject)parameters.DeepClone();
                args["api_version"] = 2;
                var body = new JObject { ["method"] = method, ["params"] = new JArray(args) };
                using (var request = new HttpRequestMessage(HttpMethod.Post, _options.RpcEndpoint))
                {
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string json = await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
                        JObject envelope;
                        try { envelope = JObject.Parse(json); }
                        catch (JsonException ex) { throw new InvalidDataException("XRPL returned invalid JSON.", ex); }
                        var result = envelope["result"] as JObject;
                        if (result == null) throw new InvalidDataException("XRPL response has no result object.");
                        string error = (string)result["error"];
                        if (error != null || (string)result["status"] == "error")
                            throw new XrplRpcException(method, error ?? "unknown", (string)result["error_message"] ?? "Request rejected.", result);
                        return result;
                    }
                }
            }
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
        {
            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var data = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (data.Length + count > MaximumResponseBytes) throw new InvalidDataException("XRPL response is too large.");
                    data.Write(buffer, 0, count);
                }
                return Encoding.UTF8.GetString(data.ToArray());
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(XrplClient));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Cancel();
            if (_ownsHttp) _http.Dispose();
            // Keep the CTS alive: in-flight operations can still register their cancellation callbacks.
        }
    }
}
