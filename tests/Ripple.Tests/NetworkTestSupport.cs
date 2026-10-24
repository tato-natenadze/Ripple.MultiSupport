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
            var body = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(await request.Content.ReadAsStringAsync().ConfigureAwait(false),
                new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
            lock (Requests) Requests.Add(body);
            JObject result = await Respond((string)body["method"], (JObject)body["params"][0], cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject { ["result"] = result }.ToString()) };
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        internal static XrplClientOptions Options() => new XrplClientOptions
        {
            RpcEndpoint = new Uri("https://xrpl.example/"), PollInterval = TimeSpan.FromMilliseconds(1)
        };
        internal static Task<JObject> Json(string value) => Task.FromResult(JObject.Parse(value));
        internal static JObject Ledger(uint index = 100) => new JObject { ["info"] = new JObject {
            ["validated_ledger"] = new JObject { ["seq"] = index, ["reserve_base_xrp"] = 1m, ["reserve_inc_xrp"] = 0.2m, ["age"] = 1 } } };
        internal static JObject Account(string address, uint ledger = 100, uint sequence = 1, uint flags = 0, string balance = "100000000") => new JObject
        {
            ["validated"] = true, ["ledger_index"] = ledger, ["account_data"] = new JObject {
                ["Account"] = address, ["Balance"] = balance, ["Flags"] = flags, ["OwnerCount"] = 2, ["Sequence"] = sequence }
        };
        internal static JObject Payment(string hash = null, uint ledger = 100, string code = "tesSUCCESS", bool validated = true) => new JObject
        {
            ["hash"] = hash ?? new string('A', 64), ["validated"] = validated, ["ledger_index"] = ledger,
            ["tx_json"] = new JObject { ["TransactionType"] = "Payment", ["Account"] = TestTransactions.Account,
                ["Destination"] = TestTransactions.Destination, ["DestinationTag"] = 0, ["DeliverMax"] = "1000000", ["Fee"] = "12", ["Sequence"] = 1 },
            ["meta"] = new JObject { ["TransactionResult"] = code, ["TransactionIndex"] = 0, ["delivered_amount"] = "1000000" }
        };
        internal static JObject History(string address, uint from, uint to, params JObject[] transactions) => new JObject
        {
            ["account"] = address, ["validated"] = true, ["ledger_index_min"] = from, ["ledger_index_max"] = to,
            ["transactions"] = new JArray(transactions)
        };
    }
}
