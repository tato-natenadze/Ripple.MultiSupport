using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;

namespace Ripple.Tests
{
    [TestClass]
    public class PublicApiSecurityTests
    {
        [DataTestMethod]
        [DataRow("DestinationTag", "1.5")]
        [DataRow("DestinationTag", "true")]
        [DataRow("Sequence", "1.5")]
        [DataRow("Sequence", "true")]
        [DataRow("Flags", "0.5")]
        [DataRow("Flags", "false")]
        [DataRow("SourceTag", "0.1")]
        [DataRow("SourceTag", "true")]
        [DataRow("LastLedgerSequence", "104.5")]
        [DataRow("LastLedgerSequence", "true")]
        [DataRow("TickSize", "3.5")]
        [DataRow("TickSize", "true")]
        [DataRow("TransferFee", "1.5")]
        [DataRow("TransferFee", "true")]
        public void SigningDoesNotCoerceFractionsOrBooleansIntoUnsignedFields(string field, string json)
        {
            var tx = field == "TickSize" ? TestTransactions.Create("AccountSet") :
                field == "TransferFee" ? TestTransactions.Create("NFTokenMint") : TestTransactions.Payment();
            if (field == "TransferFee") tx["NFTokenTaxon"] = 0;
            tx[field] = JToken.Parse(json);
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [DataTestMethod]
        [DataRow(false, 12)]
        [DataRow(true, 6)]
        [DataRow(false, 100)]
        [DataRow(false, 4096)]
        [DataRow(true, 2048)]
        public void SigningRejectsJsonBeyondTheProtocolNestingLimit(bool arrays, int levels)
        {
            var tx = TestTransactions.Payment();
            var item = tx;
            for (int i = 0; i < levels; i++)
            {
                var nested = new JObject();
                if (i == 0 || arrays) item["Memos"] = new JArray(new JObject { ["Memo"] = nested });
                else item["Memo"] = nested;
                item = nested;
            }
            item["MemoData"] = "4142";
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [DataTestMethod]
        [DataRow(false, 9)]
        [DataRow(true, 5)]
        public void SigningAndBinaryParsingKeepTheSameProtocolDepthBoundary(bool arrays, int levels)
        {
            var tx = TestTransactions.Payment();
            var item = tx;
            for (int i = 0; i < levels; i++)
            {
                var nested = new JObject();
                if (i == 0 || arrays) item["Memos"] = new JArray(new JObject { ["Memo"] = nested });
                else item["Memo"] = nested;
                item = nested;
            }
            item["MemoData"] = "4142";
            string blob = RippleClient.SignTransaction(tx);
            Assert.AreEqual(blob, StObject.FromHex(blob).ToHex());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SigningBoundsUnknownJsonInBothObjectAndTokenInputs(bool objectInput)
        {
            var payload = new JObject();
            var child = payload;
            for (int i = 0; i < 4096; i++)
            {
                var nested = new JObject(); child["unknown"] = nested; child = nested;
            }
            if (objectInput)
            {
                Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(new {
                    TransactionType = "Payment", Seed = TestTransactions.SecpSeed, Payload = payload }));
            }
            else
            {
                var tx = TestTransactions.Payment(); tx["Payload"] = payload;
                Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
            }
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("4294967295")]
        [DataRow("1.0")]
        [DataRow("\"1\"")]
        public void SigningPreservesExactIntegralTagsAndLeavesInputIntact(string json)
        {
            var tx = TestTransactions.Payment(); tx["DestinationTag"] = JToken.Parse(json);
            string original = tx.ToString();
            string blob = RippleClient.SignTransaction(tx);
            var parsed = StObject.FromHex(blob);
            Assert.AreEqual((uint)tx["DestinationTag"], (uint)parsed[Field.DestinationTag]);
            Assert.AreEqual(blob, parsed.ToHex());
            Assert.AreEqual(original, tx.ToString());
        }

        [DataTestMethod]
        [DataRow("\"}],\"method\":\"submit\",\"params\":[{\"tx_blob\":\"00\"}]")]
        [DataRow("<script>alert('x')</script>; DROP TABLE payments; --")]
        [DataRow("${jndi:ldap://example.invalid/a}\r\nInjected: value\u0000 ქართული")]
        public async Task MemoRemainsExactDataAndCannotInjectRpcFields(string memo)
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = TestTransactions.Destination,
                    AmountXrp = 0.000001m, DestinationTag = 0, Memo = memo };
                var quote = await client.GetPaymentQuoteAsync(TestTransactions.Account, request);
                Assert.AreEqual(0.000001m, quote.AmountXrp);
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed, request);
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status);
                Assert.AreEqual(0.000001m, result.Transaction.DeliveredAmountXrp);
                var tx = server.Signed.Single();
                Assert.AreEqual("1", (string)tx["Amount"]);
                Assert.AreEqual(TestTransactions.Destination, (string)tx["Destination"]);
                Assert.AreEqual(0u, (uint)tx["DestinationTag"]);
                Assert.AreEqual(B16.Encode(Encoding.UTF8.GetBytes(memo)), (string)tx["Memos"][0]["Memo"]["MemoData"]);
                Assert.AreEqual(1, handler.Requests.Count(r => (string)r["method"] == "submit"));
                foreach (var rpc in handler.Requests)
                {
                    Assert.AreEqual(2, rpc.Count);
                    Assert.AreEqual(1, ((JArray)rpc["params"]).Count);
                    Assert.IsFalse(rpc.ToString().Contains(TestTransactions.SecpSeed));
                    Assert.IsFalse(rpc.ToString().Contains(memo));
                    Assert.IsFalse((string)rpc["method"] == "sign");
                }
            }
        }

        [DataTestMethod]
        [DataRow("r\"}],\"method\":\"submit\"")]
        [DataRow("<script>alert(1)</script>")]
        [DataRow("../../secret; DROP TABLE accounts; --")]
        [DataRow("https://example.invalid/?seed=x")]
        public async Task InvalidIdentifiersCannotTriggerNetworkRequests(string input)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected network I/O.") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.IsFalse(RippleClient.ValidateAddress(input));
                Assert.IsFalse(XAddressCodec.IsValid(input));
                Assert.ThrowsException<ArgumentException>(() => XAddressCodec.Encode(input, 0, true));
                Assert.ThrowsException<ArgumentException>(() => XAddressCodec.Decode(input));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountAsync(input));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetTransactionAsync(input));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentsAsync(input));
                var payment = new XrpPaymentRequest { Destination = input, AmountXrp = 1 };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, payment));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.WatchIncomingPaymentsAsync(input,
                    (tx, ct) => Task.CompletedTask));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }

        [TestMethod]
        public async Task UntrustedMarkerAndTypeMetadataCannotChangeTheRpcMethodOrAccount()
        {
            var marker = new JObject { ["method"] = "submit", ["params"] = new JArray(new JObject { ["tx_blob"] = "00" }),
                ["account"] = TestTransactions.Destination, ["endpoint"] = "https://example.invalid/",
                ["$type"] = "Not.A.Real.Type, Missing.Assembly" };
            int calls = 0;
            var handler = new RpcHandler { Respond = (method, args, token) => {
                Assert.AreEqual("account_tx", method);
                Assert.AreEqual(TestTransactions.Account, (string)args["account"]);
                Assert.AreEqual(100, (int)args["ledger_index_min"]);
                Assert.AreEqual(200, (int)args["ledger_index_max"]);
                var result = RpcHandler.History(TestTransactions.Account, 100, 200);
                result["$type"] = "Not.A.Real.Type, Missing.Assembly";
                if (++calls == 1) result["marker"] = marker.DeepClone();
                else Assert.IsTrue(JToken.DeepEquals(marker, args["marker"]));
                return Task.FromResult(result);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var first = await client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { FromLedger = 100, ToLedger = 200 });
                Assert.AreEqual(0, first.Payments.Count);
                Assert.IsNotNull(first.ContinuationToken);
                var last = await client.GetPaymentsAsync(TestTransactions.Account,
                    new PaymentHistoryRequest { ContinuationToken = first.ContinuationToken });
                Assert.AreEqual(0, last.Payments.Count);
                Assert.IsNull(last.ContinuationToken);
                Assert.AreEqual(2, calls);
                Assert.IsTrue(handler.Requests.All(r => r.Count == 2 && ((JArray)r["params"]).Count == 1));
            }
        }
    }
}
