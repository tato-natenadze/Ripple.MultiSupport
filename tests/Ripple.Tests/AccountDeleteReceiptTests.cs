using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class AccountDeleteReceiptTests
    {
        [TestMethod]
        public void SignaturesMatchTheIndependentOfficialSdkWithoutChangingTheInput()
        {
            var fixtures = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Fixtures", "account-delete-transactions.json")));
            foreach (JObject entry in fixtures["transactions"])
            {
                var transaction = (JObject)entry["transaction"];
                string before = transaction.ToString();
                string blob = RippleClient.SignTransaction(transaction);
                Assert.AreEqual((string)entry["tx_blob"], blob, (string)entry["name"]);
                Assert.AreEqual((string)entry["hash"], Utils.TransactionId(B16.Decode(blob)));
                Assert.AreEqual(before, transaction.ToString());
                var receipt = new AccountDeleteReceipt((string)entry["hash"], (string)transaction["Account"],
                    (uint)transaction["Sequence"], 1001, 1005, blob, RpcHandler.Options().RpcEndpoint);
                var restored = JsonConvert.DeserializeObject<AccountDeleteReceipt>(JsonConvert.SerializeObject(receipt));
                Assert.AreEqual(receipt.Hash, restored.Hash); Assert.AreEqual(receipt.TxBlob, restored.TxBlob);
                Assert.AreEqual(receipt.Sender, restored.Sender); Assert.AreEqual(receipt.Destination, restored.Destination);
                Assert.AreEqual(receipt.DestinationTag, restored.DestinationTag); Assert.AreEqual(0.2m, restored.FeeXrp);
                Assert.AreEqual(receipt.FirstLedger, restored.FirstLedger); Assert.AreEqual(receipt.LastLedger, restored.LastLedger);
                Assert.AreEqual(receipt.RpcEndpoint, restored.RpcEndpoint);
                Assert.IsFalse(JsonConvert.SerializeObject(receipt).Contains((string)transaction["Seed"]));
            }
        }

        [DataTestMethod]
        [DataRow("73FF")]
        [DataRow("71FF")]
        [DataRow("74FF")]
        [DataRow("00")]
        [DataRow("FF")]
        [DataRow("F1")]
        [DataRow("GG")]
        [DataRow("ABC")]
        public void MalformedBinaryReceiptsRejectTheBlobConsistently(string blob)
        {
            var error = Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(new string('A', 64),
                TestTransactions.Account, 100, 1001, 1005, blob, RpcHandler.Options().RpcEndpoint));
            Assert.AreEqual("txBlob", error.ParamName);
        }

        [TestMethod]
        public void ReceiptCannotClaimAPaymentOrDifferentHashSenderSequenceOrLedgerRange()
        {
            var r = AccountDeleteTests.Receipt();
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(new string('F', 64), r.Sender, r.Sequence,
                r.FirstLedger, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Destination, r.Sequence,
                r.FirstLedger, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence + 1,
                r.FirstLedger, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence,
                r.FirstLedger, r.LastLedger + 1, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence,
                0, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence,
                1, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence,
                r.FirstLedger, r.LastLedger, new string('A', 65538), r.RpcEndpoint));
            var payment = PaymentRecoveryTests.Receipt(1005);
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(payment.Hash, payment.Sender,
                payment.Sequence, payment.FirstLedger, payment.LastLedger, payment.TxBlob, payment.RpcEndpoint));
            var unsigned = StObject.FromHex(r.TxBlob).ToJsonObject(); unsigned["TxnSignature"] = "";
            string unsignedBlob = StObject.FromJson(unsigned).ToHex();
            Assert.ThrowsException<ArgumentException>(() => new AccountDeleteReceipt(Utils.TransactionId(B16.Decode(unsignedBlob)),
                r.Sender, r.Sequence, r.FirstLedger, r.LastLedger, unsignedBlob, r.RpcEndpoint));
        }

        [TestMethod]
        public async Task WrongEndpointReceiptAndDisposedClientCannotMakeRequests()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new AssertFailedException("Unexpected RPC") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var r = AccountDeleteTests.Receipt();
                var wrong = new AccountDeleteReceipt(r.Hash, r.Sender, r.Sequence, r.FirstLedger, r.LastLedger, r.TxBlob,
                    new Uri("https://different.example/"));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountDeleteAsync(wrong));
                client.Dispose();
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.GetAccountDeleteAsync(r));
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.GetAccountDeleteQuoteAsync(r.Sender, AccountDeleteTests.Request()));
                await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, AccountDeleteTests.Request()));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }

        [DataTestMethod]
        [DataRow("Transport")]
        [DataRow("InvalidResponse")]
        [DataRow("Timeout")]
        [DataRow("Cancelled")]
        public async Task PostSubmissionFailuresKeepTheReceiptAndSafeStructuredError(string kind)
        {
            using (var cancel = new CancellationTokenSource())
            {
                var node = new AccountDeleteTests.Node();
                var options = RpcHandler.Options(); options.RequestTimeout = TimeSpan.FromMilliseconds(40);
                node.Override = async (method, args, token) => {
                    if (method != "submit") return null;
                    node.Submissions++;
                    if (kind == "Transport") throw new HttpRequestException(TestTransactions.SecpSeed);
                    if (kind == "InvalidResponse") throw new InvalidDataException(TestTransactions.SecpSeed);
                    if (kind == "Timeout") await Task.Delay(10000, token).ConfigureAwait(false);
                    if (kind == "Cancelled") { cancel.Cancel(); token.ThrowIfCancellationRequested(); }
                    return new JObject { ["engine_result"] = "tesSUCCESS" };
                };
                using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
                using (var client = new XrplClient(options, http))
                {
                    var result = await client.DeleteAccountAsync(TestTransactions.SecpSeed, AccountDeleteTests.Request(), cancel.Token);
                    Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
                    Assert.AreEqual(kind, result.Error.Kind.ToString()); Assert.AreEqual("submit", result.Error.Method);
                    Assert.IsFalse(JsonConvert.SerializeObject(result).Contains(TestTransactions.SecpSeed));
                    Assert.AreEqual(1, node.Submissions);
                }
            }
        }

        [TestMethod]
        public async Task TooManyDeletableObjectsAreRejectedOnlyAfterCountingAllPages()
        {
            foreach (int count in new[] { 1000, 1001 })
            {
                var node = new AccountDeleteTests.Node();
                node.Override = (method, args, ct) => {
                    if (method != "account_objects" || (bool)args["deletion_blockers_only"]) return Task.FromResult<JObject>(null);
                    int start = args["marker"] == null ? 0 : Convert.ToInt32(((string)args["marker"]).Split(',')[0], 16);
                    int end = Math.Min(start + 400, count); var objects = new JArray();
                    for (int i = start; i < end; i++) objects.Add(new JObject {
                        ["LedgerEntryType"] = "Offer", ["index"] = i.ToString("X64"), ["Account"] = TestTransactions.Account,
                        ["Sequence"] = i + 1, ["Flags"] = 0, ["TakerGets"] = "1", ["TakerPays"] = TestTransactions.Iou("1"),
                        ["BookDirectory"] = new string('A', 64), ["BookNode"] = "0", ["OwnerNode"] = "0",
                        ["PreviousTxnID"] = new string('B', 64), ["PreviousTxnLgrSeq"] = 999 });
                    var response = new JObject { ["account"] = TestTransactions.Account, ["validated"] = true,
                        ["ledger_index"] = node.Ledger, ["account_objects"] = objects };
                    if (end != count) response["marker"] = end.ToString("X64") + "," + new string('C', 64);
                    return Task.FromResult(response);
                };
                using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
                using (var client = new XrplClient(RpcHandler.Options(), http))
                {
                    var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, AccountDeleteTests.Request());
                    Assert.AreEqual(count == 1000, quote.CanDelete); Assert.AreEqual(0, node.Submissions);
                }
            }
        }

        [TestMethod]
        public async Task RequestSnapshotIsStableWhileTheCallerChangesItsObject()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var node = new AccountDeleteTests.Node(); int calls = 0, prepared = 0;
            node.Override = async (m, p, ct) => {
                if (m == "server_info" && Interlocked.Increment(ref calls) == 1)
                { entered.TrySetResult(true); await release.Task.ConfigureAwait(false); }
                return null;
            };
            var request = AccountDeleteTests.Request();
            request.OnPreparedAsync = (r, ct) => { prepared++; return Task.CompletedTask; };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var task = client.DeleteAccountAsync(TestTransactions.SecpSeed, request); await entered.Task;
                request.Destination = TestTransactions.Account; request.DestinationTag = 15; request.MaxFeeXrp = 0;
                request.OnPreparedAsync = (r, ct) => throw new AssertFailedException("Mutated callback used");
                release.TrySetResult(true);
                var result = await task;
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status); Assert.AreEqual(1, prepared);
                Assert.AreEqual(TestTransactions.Destination, result.Receipt.Destination); Assert.AreEqual(0u, result.Receipt.DestinationTag);
            }
        }

        [TestMethod]
        public async Task CancellingAQuoteWaitingForDeletionDoesNotBreakTheSharedSenderGate()
        {
            var prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var node = new AccountDeleteTests.Node(); var request = AccountDeleteTests.Request();
            request.OnPreparedAsync = async (r, ct) => { prepared.TrySetResult(true); await release.Task.ConfigureAwait(false); };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var cancel = new CancellationTokenSource())
            {
                var deletion = client.DeleteAccountAsync(TestTransactions.SecpSeed, request); await prepared.Task;
                var quote = client.GetAccountDeleteQuoteAsync(TestTransactions.Account, AccountDeleteTests.Request(), cancel.Token);
                cancel.Cancel();
                try { await quote; Assert.Fail("Cancelled waiting quote must not continue."); }
                catch (OperationCanceledException) { }
                release.TrySetResult(true);
                Assert.AreEqual(TransactionStatus.Succeeded, (await deletion).Status);
                Assert.AreEqual(1, node.Submissions);
            }
        }
    }
}
