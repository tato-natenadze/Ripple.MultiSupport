using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class PreflightResponseValidationTests
    {
        [DataTestMethod]
        [DataRow("authorization", "\"true\"")]
        [DataRow("authorization", "1")]
        [DataRow("authorization", "null")]
        [DataRow("authorization", "{}")]
        [DataRow("authorization", "[]")]
        [DataRow("authorization", "missing")]
        [DataRow("count", "0.1")]
        [DataRow("count", "-1")]
        [DataRow("count", "4294967296")]
        [DataRow("count", "\"0\"")]
        [DataRow("count", "true")]
        [DataRow("count", "null")]
        [DataRow("count", "{}")]
        [DataRow("currentLedger", "100.25")]
        [DataRow("currentLedger", "-1")]
        [DataRow("currentLedger", "4294967296")]
        [DataRow("currentLedger", "\"100\"")]
        [DataRow("currentLedger", "true")]
        [DataRow("currentLedger", "null")]
        [DataRow("currentLedger", "[]")]
        [DataRow("feeLedger", "100.25")]
        [DataRow("feeLedger", "-1")]
        [DataRow("feeLedger", "4294967296")]
        [DataRow("feeLedger", "\"100\"")]
        [DataRow("feeLedger", "true")]
        [DataRow("feeLedger", "null")]
        [DataRow("feeLedger", "{}")]
        [DataRow("queue", "[]")]
        [DataRow("queue", "false")]
        [DataRow("queue", "null")]
        public async Task InvalidTypesAreRejectedBeforePreparationAndSubmission(string field, string json)
        {
            int prepared = 0;
            var server = new PaymentTests.Server { TargetFlags = field == "authorization" ? 0x01000000u : 0 };
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                var result = await server.Respond(m, p, ct).ConfigureAwait(false);
                if (m == "deposit_authorized")
                {
                    if (json == "missing") result.Remove("deposit_authorized");
                    else result["deposit_authorized"] = JToken.Parse(json);
                }
                if (m == "account_info" && (string)p["ledger_index"] == "current")
                {
                    if (field == "count") result["queue_data"]["txn_count"] = JToken.Parse(json);
                    if (field == "currentLedger") result["ledger_current_index"] = JToken.Parse(json);
                    if (field == "queue") result["queue_data"] = JToken.Parse(json);
                }
                if (m == "fee" && field == "feeLedger") result["ledger_current_index"] = JToken.Parse(json);
                return result;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1,
                        OnPreparedAsync = (r, ct) => { prepared++; return Task.CompletedTask; } }));
                Assert.AreEqual(0, prepared); Assert.AreEqual(0, server.Signed.Count);
                Assert.IsFalse(handler.Requests.Any(r => (string)r["method"] == "submit"));
                Assert.IsFalse(handler.Requests.Any(r => r.ToString().Contains(TestTransactions.SecpSeed)));
            }
        }

        [DataTestMethod]
        [DataRow("complete")]
        [DataRow("omitQueue")]
        [DataRow("omitCount")]
        [DataRow("omitLedgers")]
        [DataRow("omitQueueAndLedgers")]
        public async Task ValidTypesAndPreviouslyOptionalFieldsStillWork(string scenario)
        {
            bool omitLedgers = scenario == "omitLedgers" || scenario == "omitQueueAndLedgers";
            var server = new PaymentTests.Server { TargetFlags = 0x01000000 };
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                var result = await server.Respond(m, p, ct).ConfigureAwait(false);
                if (m == "deposit_authorized") result["deposit_authorized"] = true;
                if (m == "fee" && !omitLedgers) { result["ledger_current_index"] = 102; server.Ledger = 102; }
                if (m == "account_info" && (string)p["ledger_index"] == "current")
                {
                    if (!omitLedgers) result["ledger_current_index"] = 101;
                    if (scenario == "omitQueue" || scenario == "omitQueueAndLedgers") result.Remove("queue_data");
                    if (scenario == "omitCount") ((JObject)result["queue_data"]).Remove("txn_count");
                }
                return result;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 });
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status);
                Assert.AreEqual(omitLedgers ? 104u : 106u, result.Receipt.LastLedger);
                Assert.AreEqual(1, server.Signed.Count);
                Assert.AreEqual("12", (string)server.Signed[0]["Fee"]);
            }
        }

        [TestMethod]
        public async Task ValidFalseAuthorizationStillRejectsThePayment()
        {
            var server = new PaymentTests.Server { TargetFlags = 0x01000000 };
            using (var http = new HttpClient(new RpcHandler { Respond = server.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 }));
                Assert.AreEqual(0, server.Signed.Count);
            }
        }
    }
}
