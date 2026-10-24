using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentTests
    {
        internal sealed class Server
        {
            internal uint Ledger = 100, Sequence = 1, TargetFlags;
            internal string Balance = "100000000", Fee = "12", FinalCode = "tesSUCCESS";
            internal bool Submitted, LoseSubmitResponse, NeverFind, SearchedAll;
            internal Action AfterSubmit;
            internal readonly List<JObject> Signed = new List<JObject>();
            private string _hash;
            private JObject _signed;
            internal Task<JObject> Respond(string method, JObject args, CancellationToken token)
            {
                switch (method)
                {
                    case "server_info": return Task.FromResult(RpcHandler.Ledger(Ledger));
                    case "account_info":
                        bool source = (string)args["account"] == TestTransactions.Account;
                        var account = RpcHandler.Account((string)args["account"], Ledger, Sequence, source ? 0u : TargetFlags, source ? Balance : "100000000");
                        account["queue_data"] = new JObject { ["txn_count"] = 0 }; return Task.FromResult(account);
                    case "fee": return Task.FromResult(new JObject { ["drops"] = new JObject { ["open_ledger_fee"] = Fee, ["base_fee"] = "10" } });
                    case "deposit_authorized": return RpcHandler.Json("{'deposit_authorized':false}");
                    case "submit":
                        Submitted = true; string blob = (string)args["tx_blob"];
                        _hash = Utils.TransactionId(B16.Decode(blob)); _signed = StObject.FromHex(blob).ToJsonObject(); Signed.Add(_signed);
                        AfterSubmit?.Invoke();
                        if (LoseSubmitResponse) throw new HttpRequestException("response lost");
                        return RpcHandler.Json("{'engine_result':'tesSUCCESS','accepted':true}");
                    case "tx":
                        if (NeverFind || !Submitted) return Task.FromResult(new JObject { ["error"] = "txnNotFound", ["searched_all"] = SearchedAll });
                        var payment = RpcHandler.Payment(_hash, ++Ledger, FinalCode);
                        payment["tx_json"] = _signed.DeepClone();
                        payment["meta"]["delivered_amount"] = _signed["Amount"].DeepClone();
                        Sequence = (uint)_signed["Sequence"] + 1; return Task.FromResult(payment);
                    default: throw new InvalidOperationException(method);
                }
            }
        }

        internal static PaymentReceipt Receipt()
        {
            var tx = TestTransactions.Payment(); tx.Remove("Seed"); tx["LastLedgerSequence"] = 104;
            var signed = TxSigner.SignJson(tx, TestTransactions.SecpSeed);
            return new PaymentReceipt(signed.Hash, TestTransactions.Account, 1, 101, 104, signed.TxBlob, RpcHandler.Options().RpcEndpoint);
        }

        [DataTestMethod]
        [DataRow(TestTransactions.SecpSeed, TestTransactions.Destination)]
        [DataRow(TestTransactions.EdSeed, TestTransactions.Account)]
        public async Task SendSignsLocallyAndWaitsForValidatedResult(string seed, string destination)
        {
            var server = new Server(); var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                PaymentReceipt saved = null;
                var result = await client.SendXrpAsync(seed, new XrpPaymentRequest { Destination = destination, AmountXrp = 1.25m,
                    DestinationTag = 0, Memo = "გადახდა", OnPreparedAsync = (receipt, ct) => {
                        Assert.IsFalse(server.Submitted);
                        saved = JsonConvert.DeserializeObject<PaymentReceipt>(JsonConvert.SerializeObject(receipt));
                        return Task.CompletedTask;
                    } });
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status); Assert.IsNotNull(saved);
                Assert.AreEqual(result.Receipt.Hash, saved.Hash); Assert.AreEqual(1.25m, result.Transaction.DeliveredAmountXrp);
                Assert.IsFalse(handler.Requests.Any(r => r.ToString().Contains(seed)));
                var tx = server.Signed.Single(); Assert.AreEqual("1250000", (string)tx["Amount"]);
                Assert.AreEqual("12", (string)tx["Fee"]); Assert.AreEqual(104u, (uint)tx["LastLedgerSequence"]);
                Assert.AreEqual(B16.Encode(System.Text.Encoding.UTF8.GetBytes("გადახდა")), (string)tx["Memos"][0]["Memo"]["MemoData"]);
            }
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("-1")]
        [DataRow("0.0000001")]
        [DataRow("100000000001")]
        public async Task InvalidAmountsAreRejectedBeforeIO(string text)
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) }));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task FeeLimitAndReserveBlockBroadcast()
        {
            var server = new Server { Fee = "101" }; var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                server.Fee = "12"; server.Balance = "2000000";
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task MissingRequiredTagAndUnauthorizedDepositAreRejected()
        {
            var server = new Server { TargetFlags = 0x00020000 }; var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                server.TargetFlags = 0x01000000;
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task FailedPersistenceCallbackPreventsSubmission()
        {
            var server = new Server(); var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1,
                        OnPreparedAsync = (r, ct) => throw new InvalidOperationException("storage offline") }));
                Assert.IsFalse(server.Submitted);
            }
        }

        [TestMethod]
        public async Task LostSubmitResponseReturnsReceiptAndBlocksReplacement()
        {
            var server = new Server { LoseSubmitResponse = true, NeverFind = true }; var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                var first = await client.SendXrpAsync(TestTransactions.SecpSeed, payment);
                Assert.AreEqual(TransactionStatus.Unknown, first.Status); Assert.IsNotNull(first.Receipt);
                var ex = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                Assert.AreEqual(first.Receipt.Hash, ex.Receipt.Hash);
                Assert.AreEqual(1, handler.Requests.Count(r => (string)r["method"] == "submit"));
                server.NeverFind = false;
                Assert.AreEqual(TransactionStatus.Succeeded, (await client.GetTransactionAsync(first.Receipt)).Status);
            }
        }

        [DataTestMethod]
        [DataRow(true, TransactionStatus.Expired)]
        [DataRow(false, TransactionStatus.Unknown)]
        public async Task ExpiryRequiresCompleteHistoryProof(bool searchedAll, TransactionStatus expected)
        {
            var server = new Server { NeverFind = true, SearchedAll = searchedAll, Ledger = 105 };
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                Assert.AreEqual(expected, (await client.GetTransactionAsync(Receipt())).Status);
        }

        [TestMethod]
        public async Task ParallelSendsFromOneClientUseSequentialAccountSequences()
        {
            var server = new Server(); var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                var results = await Task.WhenAll(client.SendXrpAsync(TestTransactions.SecpSeed, payment), client.SendXrpAsync(TestTransactions.SecpSeed, payment));
                Assert.IsTrue(results.All(r => r.Status == TransactionStatus.Succeeded));
                CollectionAssert.AreEqual(new uint[] { 1, 2 }, server.Signed.Select(t => (uint)t["Sequence"]).ToArray());
            }
        }

        [TestMethod]
        public async Task ValidatedFeeClaimingFailureIsReturnedAsFailed()
        {
            var server = new Server { FinalCode = "tecUNFUNDED_PAYMENT" }; var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.SendXrpAsync(TestTransactions.SecpSeed, new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 });
                Assert.AreEqual(TransactionStatus.Failed, result.Status); Assert.AreEqual("tecUNFUNDED_PAYMENT", result.Transaction.ResultCode);
                Assert.IsNull(result.Transaction.DeliveredAmountXrp); Assert.AreEqual(0.000012m, result.Transaction.FeeXrp);
            }
        }

        [TestMethod]
        public async Task CancellationAfterBroadcastReturnsAnIndeterminateReceipt()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var server = new Server { AfterSubmit = cancel.Cancel }; var handler = new RpcHandler { Respond = server.Respond };
                using (var http = new HttpClient(handler))
                using (var client = new XrplClient(RpcHandler.Options(), http))
                {
                    var result = await client.SendXrpAsync(TestTransactions.SecpSeed,
                        new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 }, cancel.Token);
                    Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
                }
            }
        }

        [TestMethod]
        public void ReceiptRejectsTamperedHashOrSequence()
        {
            var r = Receipt();
            Assert.ThrowsException<ArgumentException>(() => new PaymentReceipt(new string('A', 64), r.Sender, r.Sequence, r.FirstLedger, r.LastLedger, r.TxBlob, r.RpcEndpoint));
            Assert.ThrowsException<ArgumentException>(() => new PaymentReceipt(r.Hash, r.Sender, r.Sequence + 1, r.FirstLedger, r.LastLedger, r.TxBlob, r.RpcEndpoint));
        }
    }
}
