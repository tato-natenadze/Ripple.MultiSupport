using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    internal sealed class RpcHandler : HttpMessageHandler
    {
        internal readonly List<JObject> Requests = new List<JObject>();
        internal Func<string, JObject, CancellationToken, Task<JObject>> Respond;
        internal bool Disposed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JObject.Parse(await request.Content.ReadAsStringAsync());
            lock (Requests) Requests.Add(body);
            JObject result = await Respond((string)body["method"], (JObject)body["params"][0], cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject { ["result"] = result }.ToString()) };
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        internal static XrplClientOptions Options() => new XrplClientOptions
        {
            RpcEndpoint = new Uri("https://xrpl.example/"), PollInterval = TimeSpan.FromMilliseconds(1)
        };
        internal static Task<JObject> Json(string value) => Task.FromResult(JObject.Parse(value));
    }
}
