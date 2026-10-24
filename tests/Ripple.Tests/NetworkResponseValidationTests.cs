using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class NetworkResponseValidationTests
    {
        [DataTestMethod]
        [DataRow("TransactionType")]
        [DataRow("Account")]
        [DataRow("Fee")]
        [DataRow("Sequence")]
        [DataRow("Destination")]
        [DataRow("DeliverMax")]
        public async Task MissingPaymentFieldsAreInvalidResponses(string field)
        {
            var response = RpcHandler.Payment(); ((JObject)response["tx_json"]).Remove(field);
            using (var http = new HttpClient(new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(new string('A', 64)));
        }

        [DataTestMethod]
        [DataRow("body")]
        [DataRow("validated")]
        [DataRow("hash")]
        [DataRow("ledger")]
        [DataRow("index")]
        [DataRow("code")]
        [DataRow("tag")]
        [DataRow("amount")]
        [DataRow("sequence")]
        public async Task MistypedOrContradictoryResponseFieldsAreRejected(string scenario)
        {
            var response = RpcHandler.Payment(); var tx = (JObject)response["tx_json"];
            switch (scenario)
            {
                case "body": response["tx_json"] = new JArray(); break;
                case "validated": response["validated"] = "true"; break;
                case "hash": response["hash"] = "invalid"; break;
                case "ledger": response["ledger_index"] = "100"; break;
                case "index": response["meta"]["TransactionIndex"] = -1; break;
                case "code": response["meta"]["TransactionResult"] = "tefFAILURE"; break;
                case "tag": tx["DestinationTag"] = 0.5m; break;
                case "amount": tx["Amount"] = "2000000"; break;
                case "sequence": tx["Sequence"] = 4294967296L; break;
            }
            using (var http = new HttpClient(new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(new string('A', 64)));
        }

        [DataTestMethod]
        [DataRow("Destination")]
        [DataRow("Amount")]
        [DataRow("Sequence")]
        [DataRow("Fee")]
        [DataRow("LastLedgerSequence")]
        [DataRow("TxnSignature")]
        [DataRow("SigningPubKey")]
        [DataRow("delivered_amount")]
        public async Task AReceiptCannotBeFinalizedByAMismatchingTransaction(string field)
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                var response = await server.Respond(m, p, ct).ConfigureAwait(false);
                if (m == "tx")
                {
                    var tx = (JObject)response["tx_json"];
                    switch (field)
                    {
                        case "Destination": tx[field] = TestTransactions.Account; break;
                        case "Amount": tx[field] = "2000000"; break;
                        case "Sequence": tx[field] = 99; break;
                        case "Fee": tx[field] = "99"; break;
                        case "LastLedgerSequence": tx[field] = 999; break;
                        case "TxnSignature": tx[field] = "00"; break;
                        case "SigningPubKey": tx.Remove(field); break;
                        case "delivered_amount": response["meta"][field] = "500000"; break;
                    }
                }
                return response;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 });
                Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(result.Receipt));
                Assert.AreEqual(1, server.Signed.Count);
            }
        }

        [TestMethod]
        public async Task ApiV2DeliverMaxAndLedgerMetadataMatchTheSignedReceipt()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (m, p, ct) => {
                var response = await server.Respond(m, p, ct).ConfigureAwait(false);
                if (m == "tx")
                {
                    var tx = (JObject)response["tx_json"];
                    tx["DeliverMax"] = tx["Amount"].DeepClone(); tx.Remove("Amount");
                    tx["ledger_index"] = response["ledger_index"]; tx["date"] = 800000000;
                }
                return response;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 })).Status);
        }

        [DataTestMethod]
        [DataRow("OfferCreate")]
        [DataRow("FutureTransactionType")]
        [DataRow("SetFee")]
        public async Task NonPaymentTransactionsCanStillBeQueried(string type)
        {
            var response = RpcHandler.Payment(); var tx = (JObject)response["tx_json"];
            tx["TransactionType"] = type; tx.Remove("Destination"); tx.Remove("DeliverMax");
            if (type == "SetFee") { tx["Account"] = "rrrrrrrrrrrrrrrrrrrrrhoLvTp"; tx["Fee"] = "0"; tx["Sequence"] = 0; }
            using (var http = new HttpClient(new RpcHandler { Respond = (m, p, ct) => Task.FromResult(response) }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.GetTransactionAsync(new string('A', 64));
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status); Assert.IsFalse(result.IsXrpPayment);
            }
        }

        [TestMethod]
        public async Task MalformedHistoryCannotReachAnIncomingPaymentCallback()
        {
            var valid = RpcHandler.Payment(ledger: 101); var invalid = RpcHandler.Payment(new string('B', 64), 101);
            invalid["tx_json"] = new JObject();
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(101) :
                RpcHandler.History(TestTransactions.Destination, 101, 101, valid, invalid)) };
            var options = RpcHandler.Options(); options.WebSocketEndpoint = new Uri("wss://xrpl.example/");
            int received = 0;
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(options, http))
            using (var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                client.SocketFactory = () => new PaymentWatchTests.Socket();
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.WatchIncomingPaymentsAsync(TestTransactions.Destination,
                    (p, ct) => { received++; return Task.CompletedTask; }, new PaymentWatchOptions { FromLedger = 101 }, cancel.Token));
            }
            Assert.AreEqual(0, received);
        }

        [TestMethod]
        public async Task AStringSearchedAllCannotProvePaymentExpiry()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => Task.FromResult(m == "server_info" ? RpcHandler.Ledger(105) :
                JObject.Parse("{'error':'txnNotFound','searched_all':'true'}")) };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetTransactionAsync(PaymentTests.Receipt()));
        }

        [DataTestMethod]
        [DataRow("account")]
        [DataRow("history")]
        [DataRow("age")]
        [DataRow("flags")]
        public async Task MalformedAccountAndHistoryProofFieldsAreRejected(string scenario)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => {
                if (m == "server_info")
                {
                    var ledger = RpcHandler.Ledger(); if (scenario == "age") ledger["info"]["validated_ledger"]["age"] = "NaN";
                    return Task.FromResult(ledger);
                }
                var result = m == "account_tx" ? RpcHandler.History(TestTransactions.Account, 90, 100) : RpcHandler.Account(TestTransactions.Account);
                if (scenario == "account" || scenario == "history") result["validated"] = "true";
                if (scenario == "flags") result["account_data"]["Flags"] = 0.5m;
                return Task.FromResult(result);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                if (scenario == "history") await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetPaymentsAsync(TestTransactions.Account));
                else await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetAccountAsync(TestTransactions.Account));
            }
        }
    }
}
