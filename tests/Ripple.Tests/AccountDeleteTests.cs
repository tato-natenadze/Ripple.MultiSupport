using System;
using System.Collections.Generic;
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
    public class AccountDeleteTests
    {
        internal sealed class Node
        {
            internal uint Ledger = 1000, Current = 1001, Sequence = 100, SourceFlags, TargetFlags;
            internal string Balance = "1000000", TargetBalance = "3000000";
            internal decimal ReserveFee = 0.2m;
            internal bool DestinationExists = true, Authorized = true, Queued, DifferentSequence, LoseSubmit, NeverFind, SearchedAll;
            internal string ResultCode = "tesSUCCESS";
            internal int Submissions;
            internal JObject Signed;
            internal Action<JObject> SourceChange, ObjectChange;
            internal Func<string, JObject, CancellationToken, Task<JObject>> Override;

            internal async Task<JObject> Respond(string method, JObject args, CancellationToken token)
            {
                if (Override != null)
                {
                    var overridden = await Override(method, args, token).ConfigureAwait(false);
                    if (overridden != null) return overridden;
                }
                switch (method)
                {
                    case "server_info":
                        var ledger = RpcHandler.Ledger(Ledger);
                        ledger["info"]["network_id"] = 1;
                        ledger["info"]["validated_ledger"]["reserve_inc_xrp"] = ReserveFee;
                        return ledger;
                    case "account_info":
                        string account = (string)args["account"];
                        bool source = account == TestTransactions.Account;
                        if (!source && !DestinationExists) return new JObject { ["error"] = "actNotFound" };
                        var response = RpcHandler.Account(account, Ledger, Sequence, source ? SourceFlags : TargetFlags,
                            source ? Balance : TargetBalance);
                        response["account_data"]["OwnerCount"] = 0;
                        if (source) SourceChange?.Invoke((JObject)response["account_data"]);
                        if ((string)args["ledger_index"] == "current")
                        {
                            response.Remove("ledger_index"); response["validated"] = false;
                            response["ledger_current_index"] = Current;
                            response["queue_data"] = new JObject { ["txn_count"] = Queued ? 1 : 0 };
                            if (DifferentSequence) response["account_data"]["Sequence"] = Sequence + 1;
                        }
                        return response;
                    case "deposit_authorized": return new JObject { ["deposit_authorized"] = Authorized };
                    case "account_objects":
                        var objects = new JObject { ["validated"] = true, ["account"] = TestTransactions.Account,
                            ["ledger_index"] = Ledger, ["account_objects"] = new JArray() };
                        ObjectChange?.Invoke(objects);
                        return objects;
                    case "submit":
                        Interlocked.Increment(ref Submissions);
                        Assert.IsTrue((bool)args["fail_hard"], "AccountDelete must use fail_hard.");
                        Signed = StObject.FromHex((string)args["tx_blob"]).ToJsonObject();
                        if (LoseSubmit) throw new HttpRequestException("untrusted text " + TestTransactions.SecpSeed);
                        return new JObject { ["engine_result"] = ResultCode };
                    case "tx":
                        if (NeverFind || Signed == null) return new JObject { ["error"] = "txnNotFound", ["searched_all"] = SearchedAll };
                        var transaction = Transaction(Signed, Utils.TransactionId(B16.Decode(StObject.FromJson(Signed).ToHex())), Ledger + 2,
                            Balance, TargetBalance, ResultCode);
                        return transaction;
                    default: throw new InvalidOperationException("Unexpected test RPC: " + method);
                }
            }
        }

        internal static AccountDeleteRequest Request() => new AccountDeleteRequest { Destination = TestTransactions.Destination, DestinationTag = 0 };

        internal static JObject Transaction(JObject signed, string hash, uint ledger, string sourceBalance = "1000000",
            string destinationBalance = "3000000", string code = "tesSUCCESS")
        {
            long fee = long.Parse((string)signed["Fee"], System.Globalization.CultureInfo.InvariantCulture);
            long before = long.Parse(sourceBalance, System.Globalization.CultureInfo.InvariantCulture);
            long target = long.Parse(destinationBalance, System.Globalization.CultureInfo.InvariantCulture);
            long returned = before - fee;
            var meta = new JObject { ["TransactionResult"] = code, ["TransactionIndex"] = 0 };
            if (code == "tesSUCCESS")
            {
                meta["delivered_amount"] = returned.ToString(System.Globalization.CultureInfo.InvariantCulture);
                meta["AffectedNodes"] = new JArray(
                    new JObject { ["DeletedNode"] = new JObject { ["LedgerEntryType"] = "AccountRoot", ["LedgerIndex"] = new string('A', 64),
                        ["PreviousFields"] = new JObject { ["Balance"] = sourceBalance },
                        ["FinalFields"] = new JObject { ["Account"] = signed["Account"].DeepClone(), ["Balance"] = "0" } } },
                    new JObject { ["ModifiedNode"] = new JObject { ["LedgerEntryType"] = "AccountRoot", ["LedgerIndex"] = new string('B', 64),
                        ["PreviousFields"] = new JObject { ["Balance"] = destinationBalance },
                        ["FinalFields"] = new JObject { ["Account"] = signed["Destination"].DeepClone(),
                            ["Balance"] = (target + returned).ToString(System.Globalization.CultureInfo.InvariantCulture) } } });
            }
            return new JObject { ["hash"] = hash, ["validated"] = true, ["ledger_index"] = ledger,
                ["tx_json"] = signed.DeepClone(), ["meta"] = meta };
        }

        internal static AccountDeleteReceipt Receipt(uint first = 1001, uint last = 1005, bool ed = false)
        {
            var tx = new JObject { ["TransactionType"] = "AccountDelete", ["Account"] = ed ? TestTransactions.Destination : TestTransactions.Account,
                ["Destination"] = ed ? TestTransactions.Account : TestTransactions.Destination, ["DestinationTag"] = 0,
                ["Sequence"] = 100, ["Fee"] = "200000", ["LastLedgerSequence"] = last };
            var signed = TxSigner.SignJson(tx, ed ? TestTransactions.EdSeed : TestTransactions.SecpSeed);
            return new AccountDeleteReceipt(signed.Hash, (string)tx["Account"], 100, first, last, signed.TxBlob, RpcHandler.Options().RpcEndpoint);
        }

        [TestMethod]
        public async Task QuoteReturnsTheWholeBalanceMinusTheNetworkDeletionFeeWithoutSigning()
        {
            var node = new Node(); int prepared = 0;
            var request = Request(); request.OnPreparedAsync = (r, ct) => { prepared++; return Task.CompletedTask; };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, request);
                Assert.IsTrue(quote.CanDelete); Assert.AreEqual(1m, quote.BalanceXrp);
                Assert.AreEqual(1m, quote.EstimatedReserveXrp); Assert.AreEqual(0.2m, quote.EstimatedFeeXrp);
                Assert.AreEqual(0.8m, quote.EstimatedReturnedXrp); Assert.AreEqual(0u, quote.DestinationTag);
                Assert.AreEqual(355UL, quote.EarliestLedgerIndex); Assert.AreEqual(0UL, quote.LedgersUntilEligible);
                Assert.AreEqual(0, prepared); Assert.AreEqual(0, node.Submissions);
            }
        }

        [DataTestMethod]
        [DataRow("tooSoon")]
        [DataRow("feeLimit")]
        [DataRow("notFunded")]
        [DataRow("tagRequired")]
        [DataRow("depositAuth")]
        [DataRow("masterDisabled")]
        [DataRow("queued")]
        [DataRow("externalSequence")]
        [DataRow("issuedNft")]
        [DataRow("nftSequence")]
        [DataRow("sponsoring")]
        [DataRow("wrongSponsor")]
        [DataRow("obligation")]
        [DataRow("balance")]
        [DataRow("sequenceOverflow")]
        public async Task IneligibleDeletionNeverSignsPersistsOrSubmits(string condition)
        {
            var node = new Node(); var request = Request(); int prepared = 0;
            request.OnPreparedAsync = (r, ct) => { prepared++; return Task.CompletedTask; };
            switch (condition)
            {
                case "tooSoon": node.Sequence = 800; break;
                case "feeLimit": request.MaxFeeXrp = 0.1m; break;
                case "notFunded": node.DestinationExists = false; break;
                case "tagRequired": node.TargetFlags = 0x00020000; request.DestinationTag = null; break;
                case "depositAuth": node.TargetFlags = 0x01000000; node.TargetBalance = "500000"; node.Authorized = false; break;
                case "masterDisabled": node.SourceFlags = 0x00100000; break;
                case "queued": node.Queued = true; break;
                case "externalSequence": node.DifferentSequence = true; break;
                case "issuedNft": node.SourceChange = data => data["MintedNFTokens"] = 1; break;
                case "nftSequence": node.SourceChange = data => { data["FirstNFTokenSequence"] = 900; data["MintedNFTokens"] = 2; data["BurnedNFTokens"] = 2; }; break;
                case "sponsoring": node.SourceChange = data => data["SponsoringOwnerCount"] = 1; break;
                case "wrongSponsor": node.SourceChange = data => data["Sponsor"] = TestTransactions.Account; break;
                case "obligation": node.ObjectChange = objects => ((JArray)objects["account_objects"]).Add(new JObject {
                    ["LedgerEntryType"] = "RippleState", ["index"] = new string('C', 64) }); break;
                case "balance": node.Balance = "199999"; break;
                case "sequenceOverflow": node.Sequence = uint.MaxValue; break;
            }
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, request);
                Assert.IsFalse(quote.CanDelete); Assert.IsTrue(quote.BlockingReasons.Count > 0);
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, request));
                Assert.AreEqual(0, prepared); Assert.AreEqual(0, node.Submissions);
            }
        }

        [DataTestMethod]
        // rippled AccountDelete::preclaim uses Sequence + 255 <= current ledger.
        [DataRow(353u, 354u, false, 1UL)]
        [DataRow(354u, 355u, true, 0UL)]
        [DataRow(355u, 356u, true, 0UL)]
        public async Task DocumentedSequenceBoundaryDoesNotWrapOrAcceptTooSoon(uint ledger, uint current, bool eligible, ulong remaining)
        {
            var node = new Node { Ledger = ledger, Current = current };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request());
                Assert.AreEqual(eligible, quote.CanDelete); Assert.AreEqual(remaining, quote.LedgersUntilEligible);
            }
        }

        [DataTestMethod]
        [DataRow(1155u, 1156u, false, 1UL)]
        [DataRow(1156u, 1157u, true, 0UL)]
        [DataRow(1157u, 1158u, true, 0UL)]
        public async Task NftSequenceBoundaryMatchesTheOfficialInclusiveLimit(uint ledger, uint current, bool eligible, ulong remaining)
        {
            var node = new Node { Ledger = ledger, Current = current, SourceChange = data => {
                data["FirstNFTokenSequence"] = 900;
                data["MintedNFTokens"] = 2;
                data["BurnedNFTokens"] = 2;
            } };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request());
                Assert.AreEqual(1157UL, quote.EarliestLedgerIndex);
                Assert.AreEqual(eligible, quote.CanDelete);
                Assert.AreEqual(remaining, quote.LedgersUntilEligible);
                Assert.AreEqual(0, node.Submissions);
            }
        }

        [DataTestMethod]
        [DataRow("1000000", "0.8")]
        [DataRow("1000001", "0.800001")]
        [DataRow("200000", "0")]
        public async Task ValidatedDeletionReturnsExactXrpIncludingZeroRemainder(string balance, string expected)
        {
            var node = new Node { Balance = balance }; AccountDeleteReceipt persisted = null;
            var request = Request(); request.OnPreparedAsync = (receipt, ct) => {
                Assert.AreEqual(0, node.Submissions); persisted = receipt; return Task.CompletedTask; };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.DeleteAccountAsync(TestTransactions.SecpSeed, request);
                Assert.AreEqual(TransactionStatus.Succeeded, result.Status); Assert.AreSame(persisted, result.Receipt);
                Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.ReturnedAmountXrp);
                Assert.AreEqual(0.2m, result.Transaction.FeeXrp); Assert.AreEqual("AccountDelete", result.Transaction.TransactionType);
                Assert.IsFalse(result.Transaction.IsXrpPayment); Assert.IsNull(result.Transaction.DeliveredAmountXrp);
                Assert.AreEqual(1, node.Submissions);
                Assert.IsFalse(JsonConvert.SerializeObject(result).Contains(TestTransactions.SecpSeed));
            }
        }

        [TestMethod]
        public async Task ValidatedFailureMayChargeFeeButNeverReportsReturnedXrp()
        {
            var node = new Node { ResultCode = "tecHAS_OBLIGATIONS" };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.DeleteAccountAsync(TestTransactions.SecpSeed, Request());
                Assert.AreEqual(TransactionStatus.Failed, result.Status); Assert.IsTrue(result.Transaction.Validated);
                Assert.AreEqual("tecHAS_OBLIGATIONS", result.Transaction.ResultCode);
                Assert.AreEqual(0.2m, result.Transaction.FeeXrp); Assert.IsNull(result.ReturnedAmountXrp);
            }
        }

        [TestMethod]
        public async Task FailedPersistenceAndPreSubmissionCancellationDoNotReserveOrBroadcast()
        {
            var node = new Node();
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = Request(); request.OnPreparedAsync = (r, ct) => throw new IOException("persistence failed");
                await Assert.ThrowsExceptionAsync<IOException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, request));
                using (var cancel = new CancellationTokenSource())
                {
                    request.OnPreparedAsync = (r, ct) => { cancel.Cancel(); return Task.CompletedTask; };
                    await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, request, cancel.Token));
                }
                Assert.AreEqual(0, node.Submissions);
                Assert.IsTrue((await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request())).CanDelete);
            }
        }

        [TestMethod]
        public async Task LostSubmitResponseBlocksPaymentsAndDeletionsUntilTheReceiptIsResolved()
        {
            var node = new Node { LoseSubmit = true, NeverFind = true };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.DeleteAccountAsync(TestTransactions.SecpSeed, Request());
                Assert.AreEqual(TransactionStatus.Unknown, result.Status); Assert.IsNotNull(result.Receipt);
                Assert.AreEqual(PaymentErrorKind.Transport, result.Error.Kind); Assert.AreEqual("submit", result.Error.Method);
                Assert.IsFalse(JsonConvert.SerializeObject(result).Contains(TestTransactions.SecpSeed));
                var blocked = await Assert.ThrowsExceptionAsync<PendingAccountDeleteException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m }));
                Assert.AreEqual(result.Receipt.Hash, blocked.Receipt.Hash);
                await Assert.ThrowsExceptionAsync<PendingAccountDeleteException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account,
                    new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1m }));
                await Assert.ThrowsExceptionAsync<PendingAccountDeleteException>(() => client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request()));
                await Assert.ThrowsExceptionAsync<PendingAccountDeleteException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, Request()));
                Assert.AreEqual(1, node.Submissions);
                node.NeverFind = false;
                var recovered = await client.GetAccountDeleteAsync(result.Receipt);
                Assert.AreEqual(TransactionStatus.Succeeded, recovered.Status); Assert.AreEqual(0.8m, recovered.ReturnedAmountXrp);
                Assert.AreEqual(1, node.Submissions, "Recovery must never resubmit.");
            }
        }

        [TestMethod]
        public async Task AnUnresolvedPaymentAlsoBlocksAccountDeletion()
        {
            var payment = PaymentRecoveryTests.Receipt(1005);
            var node = new Node { NeverFind = true };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                Assert.AreEqual(TransactionStatus.Unknown, (await client.GetTransactionAsync(payment)).Status);
                var error = await Assert.ThrowsExceptionAsync<PendingPaymentException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, Request()));
                Assert.AreEqual(payment.Hash, error.Receipt.Hash); Assert.AreEqual(0, node.Submissions);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExpiryRequiresCompleteHistoryAndDoesNotReopenAFinalReceipt(bool searchedAll)
        {
            var receipt = Receipt(); var node = new Node { Ledger = receipt.LastLedger, Current = receipt.LastLedger + 1,
                NeverFind = true, SearchedAll = searchedAll };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var result = await client.GetAccountDeleteAsync(receipt);
                Assert.AreEqual(searchedAll ? TransactionStatus.Expired : TransactionStatus.Unknown, result.Status);
                if (searchedAll)
                {
                    node.SearchedAll = false;
                    await client.GetAccountDeleteAsync(receipt);
                    Assert.IsTrue((await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request())).CanDelete);
                }
                Assert.AreEqual(0, node.Submissions);
            }
        }

        [DataTestMethod]
        [DataRow("hash")]
        [DataRow("body")]
        [DataRow("ledger")]
        [DataRow("delivered")]
        [DataRow("destinationDelta")]
        [DataRow("sourceBalance")]
        [DataRow("missingSource")]
        public async Task RecoveryRejectsInconsistentBodiesAndBalanceProofs(string corruption)
        {
            var receipt = Receipt(); var signed = StObject.FromHex(receipt.TxBlob).ToJsonObject();
            var envelope = Transaction(signed, receipt.Hash, 1002);
            switch (corruption)
            {
                case "hash": envelope["hash"] = new string('F', 64); break;
                case "body": envelope["tx_json"]["DestinationTag"] = 1; break;
                case "ledger": envelope["ledger_index"] = 1006; break;
                case "delivered": envelope["meta"]["delivered_amount"] = "800001"; break;
                case "destinationDelta": envelope["meta"]["AffectedNodes"][1]["ModifiedNode"]["FinalFields"]["Balance"] = "3800001"; break;
                case "sourceBalance": envelope["meta"]["AffectedNodes"][0]["DeletedNode"]["FinalFields"]["Balance"] = "1"; break;
                case "missingSource": ((JArray)envelope["meta"]["AffectedNodes"]).RemoveAt(0); break;
            }
            var node = new Node { Override = (m, p, ct) => Task.FromResult(m == "tx" ? envelope : null) };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetAccountDeleteAsync(receipt));
        }

        [DataTestMethod]
        [DataRow("validated")]
        [DataRow("account")]
        [DataRow("ledger")]
        [DataRow("objects")]
        [DataRow("index")]
        [DataRow("marker")]
        public async Task MalformedObjectProofsCannotAuthorizeDeletion(string corruption)
        {
            var node = new Node { ObjectChange = data => {
                switch (corruption)
                {
                    case "validated": data["validated"] = false; break;
                    case "account": data["account"] = TestTransactions.Destination; break;
                    case "ledger": data["ledger_index"] = 999; break;
                    case "objects": data["account_objects"] = new JObject(); break;
                    case "index": data["account_objects"] = new JArray(new JObject { ["index"] = "bad" }); break;
                    case "marker": data["marker"] = new string('A', 64) + "," + new string('B', 64); break;
                }
            } };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.GetAccountDeleteQuoteAsync(TestTransactions.Account, Request()));
                Assert.AreEqual(0, node.Submissions);
            }
        }

        [TestMethod]
        public async Task XAddressTagZeroIsPreservedAndWrongNetworkOrConflictingTagsAreRejected()
        {
            var node = new Node { TargetFlags = 0x00020000 };
            using (var http = new HttpClient(new RpcHandler { Respond = node.Respond }))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new AccountDeleteRequest { Destination = XAddressCodec.Encode(TestTransactions.Destination, 0, true) };
                var quote = await client.GetAccountDeleteQuoteAsync(TestTransactions.Account, request);
                Assert.IsTrue(quote.CanDelete); Assert.AreEqual(0u, quote.DestinationTag); Assert.AreEqual(TestTransactions.Destination, quote.Destination);
                request.DestinationTag = 1;
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountDeleteQuoteAsync(TestTransactions.Account, request));
                request.DestinationTag = null; request.Destination = XAddressCodec.Encode(TestTransactions.Destination, 0, false);
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountDeleteQuoteAsync(TestTransactions.Account, request));
                Assert.AreEqual(0, node.Submissions);
            }
        }

        [TestMethod]
        public async Task InvalidInputCannotReachTheNetwork()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new AssertFailedException("Unexpected RPC") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, null));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DeleteAccountAsync("bad-seed", Request()));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountDeleteQuoteAsync("<script>", Request()));
                var request = Request(); request.Destination = TestTransactions.Account;
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, request));
                request.Destination = TestTransactions.Destination; request.MaxFeeXrp = 0.0000001m;
                await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => client.DeleteAccountAsync(TestTransactions.SecpSeed, request));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }
    }
}
