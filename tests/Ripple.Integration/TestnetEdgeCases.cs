using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ripple;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;
using Path = System.IO.Path;

namespace Ripple.Integration
{
    // Explicit live test runner. Fixed Testnet endpoints, fresh test-only keys,
    // public receipts only on disk, and no automatic payment resubmission.
    internal static class TestnetEdgeCases
    {
        private static readonly Uri Rpc = new Uri("https://s.altnet.rippletest.net:51234/");
        private static readonly Uri Ws = new Uri("wss://s.altnet.rippletest.net:51233/");
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static XrplClientOptions Options(uint offset = 12) => new XrplClientOptions {
            RpcEndpoint = Rpc, WebSocketEndpoint = Ws, RequestTimeout = TimeSpan.FromSeconds(30),
            ConfirmationTimeout = TimeSpan.FromSeconds(75), PollInterval = TimeSpan.FromSeconds(1), LastLedgerOffset = offset };
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static long Drops(decimal amount) => checked((long)(amount * 1000000m));
        private static long Integer(JToken value) => long.Parse((string)value, NumberStyles.None, Invariant);
        private static string Text(long value) => value.ToString(Invariant);

        private sealed class RpcError : Exception
        {
            internal readonly string Code;
            internal RpcError(string code) : base("Testnet RPC error: " + code) { Code = code; }
        }
        private sealed class AccountState
        {
            internal bool Exists;
            internal long Balance;
            internal uint Sequence;
        }
        private sealed class RawRpc : IDisposable
        {
            private readonly HttpClient _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            internal bool TestnetVerified;
            internal async Task<JObject> Call(string method, JObject args, CancellationToken token)
            {
                if (method == "submit") Require(TestnetVerified, "Refusing submission before network_id=1 verification.");
                args = args == null ? new JObject() : (JObject)args.DeepClone(); args["api_version"] = 2;
                var body = new JObject { ["method"] = method, ["params"] = new JArray(args) };
                using (var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"))
                using (var response = await _http.PostAsync(Rpc, content, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var result = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))["result"] as JObject;
                    Require(result != null, "Missing raw RPC result.");
                    if (result["error"] != null) throw new RpcError((string)result["error"]);
                    return result;
                }
            }
            internal async Task<JObject> VerifyNetwork(CancellationToken token)
            {
                var info = (JObject)(await Call("server_info", null, token).ConfigureAwait(false))["info"];
                Require((int?)info["network_id"] == 1, "The fixed endpoint is not Testnet network_id=1.");
                TestnetVerified = true; return info;
            }
            internal async Task<AccountState> Account(string address, uint? ledger, CancellationToken token)
            {
                try
                {
                    var result = await Call("account_info", new JObject { ["account"] = address,
                        ["ledger_index"] = ledger.HasValue ? (JToken)ledger.Value : "validated" }, token).ConfigureAwait(false);
                    Require((bool?)result["validated"] == true, "Raw account snapshot is not validated.");
                    if (ledger.HasValue) Require((uint?)result["ledger_index"] == ledger, "Wrong raw account ledger.");
                    var data = (JObject)result["account_data"];
                    Require((string)data["Account"] == address, "Wrong raw account address.");
                    return new AccountState { Exists = true, Balance = Integer(data["Balance"]), Sequence = (uint)data["Sequence"] };
                }
                catch (RpcError ex) when (ex.Code == "actNotFound") { return new AccountState(); }
            }
            internal async Task<JObject> Transaction(string hash, CancellationToken token)
            {
                return await Call("tx", new JObject { ["transaction"] = hash, ["binary"] = false }, token).ConfigureAwait(false);
            }
            internal async Task<JObject> WaitForTransaction(string hash, CancellationToken token)
            {
                for (int attempt = 0; attempt < 90; attempt++)
                {
                    try
                    {
                        var result = await Transaction(hash, token).ConfigureAwait(false);
                        if ((bool?)result["validated"] == true) return result;
                    }
                    catch (RpcError ex) when (ex.Code == "txnNotFound") { }
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                throw new TimeoutException("No validated Testnet transaction: " + hash);
            }
            internal async Task<long> Fee(CancellationToken token)
            {
                var fee = await Call("fee", null, token).ConfigureAwait(false);
                return Math.Max(Integer(fee["drops"]["base_fee"]), Integer(fee["drops"]["open_ledger_fee"]));
            }
            internal async Task<JObject> SetFlag(AddressData wallet, uint flag, bool set, CancellationToken token)
            {
                var info = await VerifyNetwork(token).ConfigureAwait(false);
                uint ledger = (uint)info["validated_ledger"]["seq"];
                var account = await Account(wallet.Address, null, token).ConfigureAwait(false);
                var tx = new JObject { ["TransactionType"] = "AccountSet", ["Account"] = wallet.Address,
                    ["Sequence"] = account.Sequence, ["Fee"] = Text(await Fee(token).ConfigureAwait(false)),
                    ["LastLedgerSequence"] = checked(ledger + 12), [set ? "SetFlag" : "ClearFlag"] = flag, ["Seed"] = wallet.Seed };
                string blob = RippleClient.SignTransaction(tx);
                string hash = B16.Encode(Utils.TransactionIdBytes(B16.Decode(blob)));
                await Call("submit", new JObject { ["tx_blob"] = blob }, token).ConfigureAwait(false);
                var result = await WaitForTransaction(hash, token).ConfigureAwait(false);
                Require((string)result["meta"]["TransactionResult"] == "tesSUCCESS", "Testnet AccountSet setup failed.");
                return new JObject { ["hash"] = hash, ["flag"] = flag, ["set"] = set,
                    ["ledger"] = result["ledger_index"], ["fee_drops"] = (result["tx_json"] ?? result)["Fee"] };
            }
            public void Dispose() { _http.Dispose(); }
        }

        private enum Fault { None, LostResponse, Cancel, Dispose }
        private sealed class ObservedHandler : DelegatingHandler
        {
            internal int Requests, Submissions, Injected;
            internal Fault Fault;
            internal Action AfterSubmission;
            internal Func<string, bool> ContainsSecret;
            internal ObservedHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Require(request.RequestUri == Rpc, "Unexpected payment RPC endpoint.");
                string json = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                Require(ContainsSecret == null || !ContainsSecret(json), "Signing secret entered an HTTP request.");
                Interlocked.Increment(ref Requests);
                bool submission = (string)JObject.Parse(json)["method"] == "submit";
                if (submission) Interlocked.Increment(ref Submissions);
                var response = await base.SendAsync(request, token).ConfigureAwait(false);
                if (submission && Fault != Fault.None && Interlocked.Exchange(ref Injected, 1) == 0)
                {
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AfterSubmission?.Invoke();
                    if (Fault == Fault.LostResponse) { response.Dispose(); throw new HttpRequestException("Injected loss AFTER actual Testnet submit response."); }
                }
                return response;
            }
        }

        private static async Task<TransactionInfo> Recover(XrplClient client, PaymentReceipt receipt, CancellationToken token)
        {
            for (int attempt = 0; attempt < 90; attempt++)
            {
                var tx = await client.GetTransactionAsync(receipt, token).ConfigureAwait(false);
                if (tx.Status == TransactionStatus.Succeeded || tx.Status == TransactionStatus.Failed || tx.Status == TransactionStatus.Expired) return tx;
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            throw new TimeoutException("Receipt recovery deadline: " + receipt.Hash);
        }

        private static async Task<JObject> VerifyPayment(RawRpc raw, XrplClient client, PaymentReceipt receipt,
            string destination, long requestedDrops, uint? tag, bool success, string memo, CancellationToken token)
        {
            var wire = await raw.WaitForTransaction(receipt.Hash, token).ConfigureAwait(false);
            var body = (JObject)(wire["tx_json"] ?? wire);
            var meta = (JObject)wire["meta"];
            uint ledger = (uint)wire["ledger_index"];
            long fee = Integer(body["Fee"]), delivered = success ? Integer(meta["delivered_amount"]) : 0;
            Require((string)(wire["hash"] ?? body["hash"]) == receipt.Hash, "Raw transaction hash mismatch.");
            Require((string)body["Account"] == receipt.Sender && (string)body["Destination"] == destination, "Raw transaction address mismatch.");
            Require(Integer(body["DeliverMax"] ?? body["Amount"]) == requestedDrops, "Signed/ledger requested amount mismatch.");
            Require((uint?)body["DestinationTag"] == tag, "DestinationTag mismatch.");
            Require(success ? (string)meta["TransactionResult"] == "tesSUCCESS" : (string)meta["TransactionResult"] == "tecDST_TAG_NEEDED", "Wrong final ledger result.");
            Require(delivered == (success ? requestedDrops : 0), "Wrong delivered amount in drops.");
            Require(ledger >= receipt.FirstLedger && ledger <= receipt.LastLedger, "Payment validated outside receipt bounds.");
            if (memo != null) Require((string)body["Memos"][0]["Memo"]["MemoData"] == B16.Encode(Encoding.UTF8.GetBytes(memo)), "UTF-8 memo mismatch.");
            var sourceBefore = await raw.Account(receipt.Sender, ledger - 1, token).ConfigureAwait(false);
            var sourceAfter = await raw.Account(receipt.Sender, ledger, token).ConfigureAwait(false);
            var targetBefore = await raw.Account(destination, ledger - 1, token).ConfigureAwait(false);
            var targetAfter = await raw.Account(destination, ledger, token).ConfigureAwait(false);
            // account_tx indexes affected accounts from metadata, not every
            // address mentioned in a rejected transaction's body.
            var affectedAccounts = ((JArray)meta["AffectedNodes"]).OfType<JObject>()
                .Select(node => node.Properties().Single().Value)
                .Select(node => node["NewFields"] ?? node["FinalFields"])
                .Where(fields => fields?["Account"] != null).Select(fields => (string)fields["Account"])
                .Distinct(StringComparer.Ordinal).ToArray();
            Require(affectedAccounts.Contains(receipt.Sender), "Payment metadata omitted the debited sender.");
            Require(affectedAccounts.Contains(destination) == success, "Fee-only failure unexpectedly affected the recipient.");
            Require(sourceBefore.Balance - sourceAfter.Balance == delivered + fee, "Sender debit differs from delivered XRP plus fee.");
            Require(targetAfter.Balance - targetBefore.Balance == delivered, "Recipient credit differs from delivered XRP.");
            Require(sourceAfter.Sequence == sourceBefore.Sequence + 1 && receipt.Sequence == sourceBefore.Sequence, "Account sequence mismatch.");
            var byReceipt = await client.GetTransactionAsync(receipt, token).ConfigureAwait(false);
            var byHash = await client.GetTransactionAsync(receipt.Hash, token).ConfigureAwait(false);
            foreach (var tx in new[] { byReceipt, byHash })
            {
                Require(tx.Validated && tx.Hash == receipt.Hash && tx.LedgerIndex == ledger, "SDK final transaction identity mismatch.");
                Require(tx.Status == (success ? TransactionStatus.Succeeded : TransactionStatus.Failed), "SDK status differs from ledger.");
                Require(tx.AmountXrp == requestedDrops / 1000000m && tx.FeeXrp == fee / 1000000m, "SDK amount/fee conversion mismatch.");
                Require(tx.DeliveredAmountXrp == (success ? delivered / 1000000m : (decimal?)null), "SDK delivered amount mismatch.");
                Require(tx.DestinationTag == tag, "SDK tag mismatch.");
            }
            return new JObject { ["hash"] = receipt.Hash, ["sender"] = receipt.Sender, ["destination"] = destination,
                ["ledger"] = ledger, ["sequence"] = receipt.Sequence, ["amount_drops"] = Text(requestedDrops),
                ["delivered_drops"] = Text(delivered), ["fee_drops"] = Text(fee), ["destination_tag"] = tag,
                ["sender_debit_drops"] = Text(sourceBefore.Balance - sourceAfter.Balance),
                ["recipient_credit_drops"] = Text(targetAfter.Balance - targetBefore.Balance),
                ["sender_balance_after_drops"] = Text(sourceAfter.Balance), ["recipient_balance_after_drops"] = Text(targetAfter.Balance),
                ["status"] = success ? "Succeeded" : "Failed", ["result_code"] = meta["TransactionResult"],
                ["affected_accounts"] = new JArray(affectedAccounts),
                ["memo"] = memo, ["memo_utf8_bytes"] = memo == null ? 0 : Encoding.UTF8.GetByteCount(memo),
                ["balance_and_amount_mismatches"] = 0 };
        }

        internal static async Task<string> RunAsync(string directory, CancellationToken token)
        {
            using (var run = new Runner(directory, token)) return await run.Run().ConfigureAwait(false);
        }
        internal static async Task<string> VerifyAsync(string directory, CancellationToken token)
        {
            var report = JObject.Parse(File.ReadAllText(Path.Combine(directory, "report.json")));
            Require(report["payments"] is JArray && report["payments"].HasValues, "No saved payment receipts to verify.");
            using (var raw = new RawRpc())
            using (var client = new XrplClient(Options()))
            {
                await raw.VerifyNetwork(token).ConfigureAwait(false); int count = 0;
                var proofs = new List<JObject>();
                foreach (JObject payment in report["payments"])
                {
                    string file = (string)payment["receipt_file"];
                    Require(Path.GetFileName(file) == file, "Invalid receipt filename.");
                    var receipt = JsonConvert.DeserializeObject<PaymentReceipt>(File.ReadAllText(Path.Combine(directory, file)));
                    var proof = await VerifyPayment(raw, client, receipt, (string)payment["destination"], Integer(payment["amount_drops"]),
                        (uint?)payment["destination_tag"], (string)payment["status"] == "Succeeded", (string)payment["memo"], token).ConfigureAwait(false);
                    proof["case"] = payment["case"]; proof["receipt_file"] = file; proofs.Add(proof);
                    count++;
                    Console.WriteLine("verified opposite-runtime receipt " + count + ": " + receipt.Hash);
                }
                uint first = (uint?)report["history_from_ledger"] ?? proofs.Min(p => (uint)p["ledger"]);
                uint last = proofs.Max(p => (uint)p["ledger"]);
                var senderHistory = await VerifyHistory(client, proofs, (string)report["sender"], first, last, token).ConfigureAwait(false);
                var receiverHistory = await VerifyHistory(client, proofs, (string)report["receiver_ed25519"], first, last, token).ConfigureAwait(false);
                int incoming = proofs.Count(p => (string)p["destination"] == (string)report["receiver_ed25519"] &&
                    (string)p["status"] == "Succeeded" && (string)p["case"] != "activation-primary");
                Require((int)report["unique_credited_hashes"] == incoming, "Recorded live watch credit count differs from validated incoming payments.");
                var verification = new JObject { ["status"] = "passed", ["runtime"] = Environment.Version.ToString(),
                    ["source_run_status"] = report["status"], ["prior_completed_checks"] = ((JArray)report["checks"]).Count,
                    ["verified_receipts"] = count, ["amount_mismatches"] = 0, ["new_submissions"] = 0,
                    ["sender_history"] = senderHistory, ["receiver_history"] = receiverHistory, ["payments"] = new JArray(proofs) };
                string output = "verification-" + (Environment.Version.Major >= 8 ? "net8" : "framework") + ".json";
                File.WriteAllText(Path.Combine(directory, output), verification.ToString(Formatting.Indented));
                return new JObject { ["status"] = "passed", ["runtime"] = Environment.Version.ToString(),
                    ["source_run_status"] = report["status"], ["verified_receipts"] = count,
                    ["histories_verified"] = 2, ["amount_mismatches"] = 0, ["new_submissions"] = 0 }.ToString(Formatting.None);
            }
        }

        private static async Task<JObject> VerifyHistory(XrplClient client, IEnumerable<JObject> payments, string address,
            uint first, uint last, CancellationToken token)
        {
            var expected = payments.Where(p => ((JArray)p["affected_accounts"]).Values<string>().Contains(address))
                .ToDictionary(p => (string)p["hash"], StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal); string continuation = null; int pages = 0;
            do
            {
                var page = await client.GetPaymentsAsync(address, new PaymentHistoryRequest { FromLedger = first, ToLedger = last,
                    Limit = 1, ContinuationToken = continuation }, token).ConfigureAwait(false);
                Require(page.RequestedRangeAvailable, "History range is incomplete.");
                foreach (var payment in page.Payments)
                {
                    JObject proof;
                    Require(expected.TryGetValue(payment.Hash, out proof) && seen.Add(payment.Hash), "Unexpected or repeated history hash.");
                    Require(payment.AmountXrp == Integer(proof["amount_drops"]) / 1000000m && payment.FeeXrp == Integer(proof["fee_drops"]) / 1000000m,
                        "History amount/fee mismatch.");
                    Require(payment.Status.ToString() == (string)proof["status"] && payment.DestinationTag == (uint?)proof["destination_tag"], "History status/tag mismatch.");
                    Require(payment.DeliveredAmountXrp == ((string)proof["status"] == "Succeeded" ? Integer(proof["delivered_drops"]) / 1000000m : (decimal?)null), "History delivered amount mismatch.");
                }
                continuation = page.ContinuationToken; Require(++pages < 200, "History marker loop.");
            } while (continuation != null);
            Require(seen.SetEquals(expected.Keys), "History omitted a transaction affecting the account.");
            return new JObject { ["pages"] = pages, ["payments"] = seen.Count };
        }

        private sealed class Expected
        {
            internal string Destination;
            internal long Drops;
            internal bool Success;
        }

        private sealed class Runner : IDisposable
        {
            private readonly string _directory;
            private readonly CancellationToken _token;
            private readonly RawRpc _raw = new RawRpc();
            private readonly AddressData _sender = RippleClient.GenerateAddress();
            private readonly AddressData _receiver;
            private readonly AddressData _cold = RippleClient.GenerateAddress();
            private readonly ObservedHandler _handler = new ObservedHandler();
            private readonly HttpClient _http;
            private readonly XrplClient _client;
            private readonly JArray _payments = new JArray(), _checks = new JArray(), _setup = new JArray();
            private readonly object _recordLock = new object();
            private readonly ConcurrentDictionary<string, Expected> _expected = new ConcurrentDictionary<string, Expected>();
            private readonly ConcurrentDictionary<string, byte> _credited = new ConcurrentDictionary<string, byte>();
            private long _callbackCount;
            private uint _firstLedger;
            private decimal _reserve;
            private PaymentReceipt _unbroadcast;
            private WatchSession _watch;
            private string _initialCheckpoint;

            internal Runner(string directory, CancellationToken token)
            {
                _directory = directory; _token = token;
                var seed = Seed.FromRandom().SetEd25519();
                _receiver = new AddressData { Address = seed.KeyPair().Id(), Seed = seed.ToString() };
                _handler.ContainsSecret = ContainsSecret;
                _http = new HttpClient(_handler); _client = new XrplClient(Options(), _http);
            }
            private bool ContainsSecret(string value) => value.Contains(_sender.Seed) || value.Contains(_receiver.Seed) || value.Contains(_cold.Seed);
            private void Save(string status)
            {
                var report = new JObject { ["status"] = status, ["runtime"] = Environment.Version.ToString(),
                    ["library"] = typeof(XrplClient).Assembly.Location, ["rpc"] = Rpc.AbsoluteUri, ["network_id"] = 1,
                    ["sender"] = _sender.Address, ["receiver_ed25519"] = _receiver.Address, ["cold_account"] = _cold.Address,
                    ["history_from_ledger"] = _firstLedger,
                    ["payments"] = _payments, ["checks"] = _checks, ["setup_transactions"] = _setup,
                    ["unique_credited_hashes"] = _credited.Count, ["payment_callbacks"] = Interlocked.Read(ref _callbackCount),
                    ["amount_mismatches"] = status == "passed" ? (JToken)0 : JValue.CreateNull(), ["seeds_persisted"] = false };
                string json = report.ToString(Formatting.Indented); Require(!ContainsSecret(json), "Secret in test report.");
                File.WriteAllText(Path.Combine(_directory, "report.json"), json);
            }
            private void Passed(string name, JObject details = null)
            {
                lock (_recordLock)
                {
                    details = details ?? new JObject(); details["case"] = name; details["status"] = "passed";
                    _checks.Add(details); Save("running"); Console.WriteLine("passed: " + name);
                }
            }
            private async Task Fund()
            {
                await _raw.VerifyNetwork(_token).ConfigureAwait(false);
                using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                using (var response = await http.PostAsync("https://faucet.altnet.rippletest.net/accounts",
                    new StringContent(new JObject { ["destination"] = _sender.Address }.ToString(), Encoding.UTF8, "application/json"), _token).ConfigureAwait(false))
                    response.EnsureSuccessStatusCode();
                for (int attempt = 0; attempt < 45; attempt++)
                {
                    if ((await _raw.Account(_sender.Address, null, _token).ConfigureAwait(false)).Exists) return;
                    await Task.Delay(1000, _token).ConfigureAwait(false);
                }
                throw new TimeoutException("Testnet faucet funding did not validate.");
            }

            private string Persist(string name, PaymentReceipt receipt)
            {
                string file = "receipt-" + name + ".json";
                string json = JsonConvert.SerializeObject(receipt, Formatting.Indented);
                Require(!ContainsSecret(json), "Secret in receipt file.");
                File.WriteAllText(Path.Combine(_directory, file), json); return file;
            }

            private async Task<JObject> Pay(string name, AddressData sender, AddressData destination, decimal amount,
                uint? tag = null, string memo = null, bool success = true, Func<Task> beforeBroadcast = null,
                XrplClient client = null, ObservedHandler observer = null, CancellationToken? sendToken = null,
                Fault expectedFault = Fault.None, bool parallel = false)
            {
                Require(_raw.TestnetVerified, "Network must be verified before a payment.");
                client = client ?? _client; observer = observer ?? _handler;
                int submissions = observer.Submissions; PaymentReceipt prepared = null; string file = null;
                long amountDrops = Drops(amount);
                var result = await client.SendXrpAsync(sender.Seed, new XrpPaymentRequest {
                    Destination = destination.Address, AmountXrp = amount, DestinationTag = tag, Memo = memo,
                    OnPreparedAsync = async (receipt, ct) => {
                        prepared = receipt; file = Persist(name, receipt);
                        var signed = StObject.FromHex(receipt.TxBlob).ToJsonObject();
                        Require(Integer(signed["Amount"]) == amountDrops, "Prepared signed amount is not exact.");
                        Require((uint?)signed["DestinationTag"] == tag, "Prepared tag mismatch.");
                        Require(Integer(signed["Fee"]) > 0 && Integer(signed["Fee"]) <= 100, "Unexpected prepared fee.");
                        _expected[receipt.Hash] = new Expected { Destination = destination.Address, Drops = amountDrops, Success = success };
                        if (beforeBroadcast != null) await beforeBroadcast().ConfigureAwait(false);
                    } }, sendToken ?? _token).ConfigureAwait(false);
                Require(prepared != null && result.Receipt.Hash == prepared.Hash, "Prepared/result receipt mismatch.");
                if (!parallel) Require(observer.Submissions == submissions + 1, "Payment was submitted more than once.");
                if (expectedFault != Fault.None)
                {
                    Require(observer.Injected == 1 && result.Status == TransactionStatus.Unknown, "Live submission fault was not observed as Unknown.");
                    Require(result.Error != null && result.Error.Kind == (expectedFault == Fault.LostResponse ? PaymentErrorKind.Transport : PaymentErrorKind.Cancelled), "Wrong post-submit error kind.");
                }
                using (var lookup = new XrplClient(Options()))
                {
                    var final = await Recover(lookup, prepared, _token).ConfigureAwait(false);
                    Require(final.Status == (success ? TransactionStatus.Succeeded : TransactionStatus.Failed), "Unexpected recovered payment outcome: " + final.Status);
                    var proof = await VerifyPayment(_raw, lookup, prepared, destination.Address, amountDrops, tag, success, memo, _token).ConfigureAwait(false);
                    proof["case"] = name; proof["receipt_file"] = file; proof["initial_status"] = result.Status.ToString();
                    proof["injected_fault"] = expectedFault.ToString();
                    if (result.Error != null) proof["initial_error_kind"] = result.Error.Kind.ToString();
                    lock (_recordLock) { _payments.Add(proof); Passed(name, new JObject { ["hash"] = prepared.Hash, ["amount_drops"] = Text(amountDrops) }); }
                    return proof;
                }
            }

            private async Task Reject<T>(string name, XrpPaymentRequest payment, bool noIO = false,
                XrplClient client = null, ObservedHandler observer = null) where T : Exception
            {
                client = client ?? _client; observer = observer ?? _handler;
                int calls = observer.Requests, submissions = observer.Submissions;
                Exception failure = null;
                try { await client.SendXrpAsync(_sender.Seed, payment, _token).ConfigureAwait(false); }
                catch (Exception ex) { failure = ex; }
                Require(failure is T, "Expected " + typeof(T).Name + " in " + name + ", got " + (failure == null ? "no exception" : failure.GetType().Name));
                Require(observer.Submissions == submissions, "Rejected operation submitted a transaction: " + name);
                if (noIO) Require(observer.Requests == calls, "Invalid input performed I/O: " + name);
                Passed(name, new JObject { ["exception"] = failure.GetType().Name, ["submissions"] = 0, ["rpc_calls"] = observer.Requests - calls });
            }
            private XrpPaymentRequest Request(decimal amount = 1m, uint? tag = null) => new XrpPaymentRequest { Destination = _receiver.Address, AmountXrp = amount, DestinationTag = tag };

            private async Task Rejections()
            {
                var before = await _raw.Account(_sender.Address, null, _token).ConfigureAwait(false);
                foreach (decimal value in new[] { 0m, -1m, 0.0000001m, 100000000001m, decimal.MaxValue })
                    await Reject<ArgumentOutOfRangeException>("invalid-amount-" + value.ToString(Invariant), Request(value), true).ConfigureAwait(false);
                var bad = Request(); bad.Destination = "not-an-address";
                await Reject<ArgumentException>("invalid-destination", bad, true).ConfigureAwait(false);
                bad = Request(); bad.Destination = _sender.Address;
                await Reject<ArgumentException>("self-payment", bad, true).ConfigureAwait(false);
                bad = Request(); bad.Memo = new string('x', 1001);
                await Reject<ArgumentException>("memo-1001-bytes", bad, true).ConfigureAwait(false);
                bad = Request(); bad.Memo = "\ud800";
                await Reject<ArgumentException>("memo-invalid-utf8", bad, true).ConfigureAwait(false);
                bad = Request(); bad.OnPreparedAsync = (r, ct) => throw new IOException("Expected test persistence failure.");
                await Reject<IOException>("persistence-failure", bad).ConfigureAwait(false);
                var account = await _client.GetAccountAsync(_sender.Address, _token).ConfigureAwait(false);
                await Reject<InvalidOperationException>("insufficient-spendable", Request(account.EstimatedSpendableXrp), false).ConfigureAwait(false);
                var cheap = Options(); cheap.MaxFeeXrp = 0.000001m;
                var observer = new ObservedHandler { ContainsSecret = ContainsSecret };
                using (var http = new HttpClient(observer))
                using (var client = new XrplClient(cheap, http))
                    await Reject<InvalidOperationException>("fee-limit-one-drop", Request(), client: client, observer: observer).ConfigureAwait(false);
                using (var cancelled = new CancellationTokenSource())
                {
                    int calls = _handler.Requests; cancelled.Cancel();
                    try { await _client.SendXrpAsync(_sender.Seed, Request(), cancelled.Token).ConfigureAwait(false); throw new InvalidOperationException("Cancelled payment did not throw."); }
                    catch (OperationCanceledException) { }
                    Require(_handler.Requests == calls, "Already-cancelled payment performed I/O.");
                    Passed("already-cancelled-no-io");
                }
                var after = await _raw.Account(_sender.Address, null, _token).ConfigureAwait(false);
                Require(before.Balance == after.Balance && before.Sequence == after.Sequence, "Rejected operations changed balance or sequence.");
                Passed("all-rejections-preserve-balance-and-sequence");
            }

            private async Task PrepareWithoutBroadcast()
            {
                var observer = new ObservedHandler { ContainsSecret = ContainsSecret };
                using (var http = new HttpClient(observer))
                using (var client = new XrplClient(Options(8), http))
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(_token))
                {
                    var payment = Request(0.000001m); payment.Memo = "never-broadcast";
                    payment.OnPreparedAsync = (r, ct) => { _unbroadcast = r; Persist("never-broadcast", r); stop.Cancel(); return Task.CompletedTask; };
                    try { await client.SendXrpAsync(_sender.Seed, payment, stop.Token).ConfigureAwait(false); throw new InvalidOperationException("Preparation cancellation did not throw."); }
                    catch (OperationCanceledException) { }
                    Require(_unbroadcast != null && observer.Submissions == 0, "Cancelled preparation submitted a payment.");
                    var restored = JsonConvert.DeserializeObject<PaymentReceipt>(File.ReadAllText(Path.Combine(_directory, "receipt-never-broadcast.json")));
                    Require((await client.GetTransactionAsync(restored, _token).ConfigureAwait(false)).Status == TransactionStatus.Unknown, "Fresh unbroadcast receipt must be indeterminate.");
                    await Reject<PendingPaymentException>("restored-unknown-blocks-replacement", Request(0.000002m), client: client, observer: observer).ConfigureAwait(false);
                    Passed("cancel-before-submit-keeps-prepared-receipt", new JObject { ["hash"] = restored.Hash, ["submissions"] = 0 });
                }
            }

            private async Task FaultPayment(string name, Fault fault)
            {
                var observer = new ObservedHandler { ContainsSecret = ContainsSecret, Fault = fault };
                using (var http = new HttpClient(observer))
                using (var client = new XrplClient(Options(), http))
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(_token))
                {
                    if (fault == Fault.Cancel) observer.AfterSubmission = stop.Cancel;
                    if (fault == Fault.Dispose) observer.AfterSubmission = client.Dispose;
                    await Pay(name, _sender, _receiver, 0.000001m, 0, client: client, observer: observer,
                        sendToken: stop.Token, expectedFault: fault).ConfigureAwait(false);
                }
            }

            private sealed class WatchSession
            {
                internal readonly CancellationTokenSource Stop;
                internal readonly Task Task;
                internal readonly ConcurrentDictionary<string, byte> Seen = new ConcurrentDictionary<string, byte>();
                internal string Checkpoint;
                internal long Next;
                private int _ended;
                internal WatchSession(Runner run, uint? from, string checkpoint)
                {
                    Stop = CancellationTokenSource.CreateLinkedTokenSource(run._token);
                    Checkpoint = checkpoint;
                    if (checkpoint != null) Next = Decode(checkpoint);
                    var client = new XrplClient(Options());
                    Task = Execute(run, client, from, checkpoint);
                }
                private async Task Execute(Runner run, XrplClient client, uint? from, string checkpoint)
                {
                    using (client)
                    {
                        await client.WatchIncomingPaymentsAsync(run._receiver.Address, (payment, ct) => {
                            Expected expected;
                            Require(run._expected.TryGetValue(payment.Hash, out expected), "Unexpected incoming payment callback.");
                            Require(expected.Success && expected.Destination == run._receiver.Address && payment.Validated &&
                                payment.Status == TransactionStatus.Succeeded && payment.DeliveredAmountXrp == expected.Drops / 1000000m,
                                "Watch delivered a failed payment or incorrect amount.");
                            Seen.TryAdd(payment.Hash, 0); run._credited.TryAdd(payment.Hash, 0);
                            Interlocked.Increment(ref run._callbackCount); return System.Threading.Tasks.Task.CompletedTask;
                        }, new PaymentWatchOptions { FromLedger = from, Checkpoint = checkpoint, SaveCheckpointAsync = (saved, ct) => {
                            Checkpoint = saved; Interlocked.Exchange(ref Next, Decode(saved));
                            if (run._initialCheckpoint == null) run._initialCheckpoint = saved;
                            File.WriteAllText(Path.Combine(run._directory, "checkpoint.txt"), saved);
                            return System.Threading.Tasks.Task.CompletedTask;
                        } }, Stop.Token).ConfigureAwait(false);
                    }
                }
                private static uint Decode(string checkpoint) => (uint)JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(checkpoint)))["next_ledger"];
                internal async Task WaitFor(IEnumerable<JObject> payments, CancellationToken token)
                {
                    var list = payments.ToList(); uint last = list.Max(p => (uint)p["ledger"]);
                    for (int attempt = 0; attempt < 90; attempt++)
                    {
                        if (Task.IsCompleted) { await Task.ConfigureAwait(false); throw new InvalidOperationException("Watch stopped unexpectedly."); }
                        if (Interlocked.Read(ref Next) > last && list.All(p => Seen.ContainsKey((string)p["hash"]))) return;
                        await System.Threading.Tasks.Task.Delay(500, token).ConfigureAwait(false);
                    }
                    throw new TimeoutException("Watch did not acknowledge every expected incoming hash and checkpoint.");
                }
                internal async Task End()
                {
                    if (Interlocked.Exchange(ref _ended, 1) != 0) return;
                    Stop.Cancel();
                    try { await Task.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (Stop.IsCancellationRequested) { }
                    finally { Stop.Dispose(); }
                }
            }

            private List<JObject> Incoming() => _payments.OfType<JObject>().Where(p => (string)p["destination"] == _receiver.Address &&
                (string)p["status"] == "Succeeded" && (string)p["case"] != "activation-primary").ToList();

            private async Task History(string address)
            {
                uint end = _payments.OfType<JObject>().Max(p => (uint)p["ledger"]);
                var proof = await VerifyHistory(_client, _payments.OfType<JObject>(), address, _firstLedger, end, _token).ConfigureAwait(false);
                Passed("history-" + (address == _sender.Address ? "sender" : "receiver"), proof);
            }

            internal async Task<string> Run()
            {
                try
                {
                    await Fund().ConfigureAwait(false);
                    var info = await _raw.VerifyNetwork(_token).ConfigureAwait(false);
                    _reserve = (decimal)info["validated_ledger"]["reserve_base_xrp"];
                    _firstLedger = checked((uint)info["validated_ledger"]["seq"] + 1);
                    Passed("fresh-testnet-network-and-faucet");
                    var account = await _client.GetAccountAsync(_sender.Address, _token).ConfigureAwait(false);
                    var rawAccount = await _raw.Account(_sender.Address, account.LedgerIndex, _token).ConfigureAwait(false);
                    Require(account.BalanceXrp == rawAccount.Balance / 1000000m && account.Sequence == rawAccount.Sequence, "Initial account amount mismatch.");
                    var tooSmall = new XrpPaymentRequest { Destination = _cold.Address, AmountXrp = _reserve - 0.000001m };
                    await Reject<ArgumentException>("unfunded-below-reserve", tooSmall).ConfigureAwait(false);
                    var activation = await Pay("activation-primary", _sender, _receiver, _reserve + 5m).ConfigureAwait(false);
                    await Pay("activation-exact-reserve", _sender, _cold, _reserve).ConfigureAwait(false);
                    Require((await _client.GetAccountAsync(_cold.Address, _token).ConfigureAwait(false)).BalanceXrp == _reserve, "Exact reserve activation mismatch.");
                    await Rejections().ConfigureAwait(false);
                    _watch = new WatchSession(this, checked((uint)activation["ledger"] + 1), null);
                    await PrepareWithoutBroadcast().ConfigureAwait(false);
                    var amounts = new[] { 0.000001m, 0.123456m, 1.000001m, 10.999999m };
                    var cultures = new[] { "en-US", "ka-GE", "de-DE", "fr-FR" };
                    for (int i = 0; i < amounts.Length; i++)
                    {
                        var original = CultureInfo.CurrentCulture;
                        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultures[i]);
                            await Pay("amount-" + cultures[i], _sender, _receiver, amounts[i]).ConfigureAwait(false); }
                        finally { CultureInfo.CurrentCulture = original; }
                    }
                    int parallelSubmits = _handler.Submissions;
                    var parallel = await System.Threading.Tasks.Task.WhenAll(
                        Pay("parallel-one-drop", _sender, _receiver, 0.000001m, parallel: true),
                        Pay("parallel-two-drops", _sender, _receiver, 0.000002m, parallel: true)).ConfigureAwait(false);
                    Require(_handler.Submissions == parallelSubmits + 2 && Math.Abs((long)(uint)parallel[0]["sequence"] - (uint)parallel[1]["sequence"]) == 1,
                        "Parallel submissions used wrong counts or sequences.");
                    Passed("parallel-sequences-and-exact-balances");
                    await Pay("ed25519-return-one-drop", _receiver, _sender, 0.000001m).ConfigureAwait(false);
                    await Pay("validated-fee-only-failure", _sender, _receiver, 0.000001m, success: false, beforeBroadcast: async () => {
                        _setup.Add(await _raw.SetFlag(_receiver, 1, true, _token).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                    await Reject<ArgumentException>("missing-required-tag", Request(0.000001m)).ConfigureAwait(false);
                    await Pay("required-tag-zero", _sender, _receiver, 0.000001m, 0).ConfigureAwait(false);
                    await Pay("maximum-tag-and-1000-byte-memo", _sender, _receiver, 0.100001m, uint.MaxValue, new string('ქ', 333) + "a").ConfigureAwait(false);
                    _setup.Add(await _raw.SetFlag(_receiver, 9, true, _token).ConfigureAwait(false));
                    await Reject<InvalidOperationException>("unauthorized-deposit", Request(0.000001m, 0)).ConfigureAwait(false);
                    _setup.Add(await _raw.SetFlag(_receiver, 9, false, _token).ConfigureAwait(false));
                    _setup.Add(await _raw.SetFlag(_cold, 9, true, _token).ConfigureAwait(false));
                    await Pay("deposit-auth-reserve-exemption", _sender, _cold, 0.000001m).ConfigureAwait(false);
                    await FaultPayment("lost-submit-response", Fault.LostResponse).ConfigureAwait(false);
                    await FaultPayment("cancel-after-submit", Fault.Cancel).ConfigureAwait(false);
                    await FaultPayment("dispose-after-submit", Fault.Dispose).ConfigureAwait(false);
                    await _watch.WaitFor(Incoming(), _token).ConfigureAwait(false);
                    await _watch.End().ConfigureAwait(false); _watch = null;
                    int credits = _credited.Count;
                    _watch = new WatchSession(this, null, _initialCheckpoint);
                    await _watch.WaitFor(Incoming(), _token).ConfigureAwait(false);
                    Require(_credited.Count == credits, "Replay duplicated an application credit.");
                    string checkpoint = _watch.Checkpoint;
                    await _watch.End().ConfigureAwait(false); _watch = null;
                    Passed("checkpoint-replay-and-host-hash-deduplication", new JObject { ["unique_credits"] = credits });
                    _watch = new WatchSession(this, null, checkpoint);
                    var restart = await Pay("after-checkpoint-restart", _sender, _receiver, 0.000001m, 0).ConfigureAwait(false);
                    account = await _client.GetAccountAsync(_sender.Address, _token).ConfigureAwait(false);
                    decimal spend = account.EstimatedSpendableXrp - (await _raw.Fee(_token).ConfigureAwait(false)) / 1000000m;
                    var boundary = await Pay("exact-spendable-boundary", _sender, _receiver, spend, 0).ConfigureAwait(false);
                    account = await _client.GetAccountAsync(_sender.Address, _token).ConfigureAwait(false);
                    Require(account.BalanceXrp == _reserve && account.EstimatedSpendableXrp == 0, "Spendable boundary did not leave the exact reserve.");
                    await Reject<InvalidOperationException>("reserve-cannot-be-spent", Request(0.000001m, 0)).ConfigureAwait(false);
                    await _watch.WaitFor(new[] { restart, boundary }, _token).ConfigureAwait(false);
                    Require(_watch.Seen.Count == 2, "Advanced checkpoint replayed an old payment.");
                    await _watch.End().ConfigureAwait(false); _watch = null;
                    Passed("advanced-checkpoint-resumes-only-new-payments");
                    using (var recovery = new XrplClient(Options()))
                        Require((await Recover(recovery, _unbroadcast, _token).ConfigureAwait(false)).Status == TransactionStatus.Expired, "Unbroadcast receipt did not prove expiry.");
                    Passed("unbroadcast-receipt-proven-expired", new JObject { ["hash"] = _unbroadcast.Hash, ["submissions"] = 0 });
                    await History(_sender.Address).ConfigureAwait(false);
                    await History(_receiver.Address).ConfigureAwait(false);
                    Require(_credited.Count == Incoming().Count, "Watch credit count differs from validated successful incoming hashes.");
                    Save("passed");
                    return new JObject { ["status"] = "passed", ["runtime"] = Environment.Version.ToString(), ["payments"] = _payments.Count,
                        ["checks"] = _checks.Count, ["amount_mismatches"] = 0, ["report"] = Path.Combine(_directory, "report.json") }.ToString(Formatting.None);
                }
                catch { Save("failed"); throw; }
                finally { if (_watch != null) { await _watch.End().ConfigureAwait(false); _watch = null; } }
            }
            public void Dispose() { _client.Dispose(); _http.Dispose(); _raw.Dispose(); }
        }
    }
}
