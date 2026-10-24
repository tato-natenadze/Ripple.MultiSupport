using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        internal sealed class LedgerSnapshot
        {
            internal uint Index;
            internal decimal BaseReserve;
            internal decimal OwnerReserve;
        }

        private async Task<LedgerSnapshot> GetLedgerAsync(CancellationToken token)
        {
            var result = await RpcAsync("server_info", null, token).ConfigureAwait(false);
            var info = result["info"] as JObject;
            var ledger = info?["validated_ledger"] as JObject;
            if (ledger == null || ledger["seq"] == null || ledger["reserve_base_xrp"] == null || ledger["reserve_inc_xrp"] == null)
                throw new InvalidDataException("Server has no validated ledger/reserve information.");
            // A stale server must not be used for funding or expiry decisions.
            if (ledger["age"] != null)
            {
                double age;
                if ((ledger["age"].Type != JTokenType.Integer && ledger["age"].Type != JTokenType.Float) ||
                    !double.TryParse((string)ledger["age"], NumberStyles.Float, CultureInfo.InvariantCulture, out age) ||
                    double.IsNaN(age) || double.IsInfinity(age) || age < 0 || age > 120)
                    throw new InvalidDataException("Server's validated ledger age is invalid or stale.");
            }
            return new LedgerSnapshot
            {
                Index = RequiredUInt(ledger, "seq"),
                BaseReserve = RequiredDecimal(ledger, "reserve_base_xrp"),
                OwnerReserve = RequiredDecimal(ledger, "reserve_inc_xrp")
            };
        }

        public async Task<AccountInfo> GetAccountAsync(string address, CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateClassicAddress(address, nameof(address));
            var ledger = await GetLedgerAsync(cancellationToken).ConfigureAwait(false);
            return await ReadAccountAsync(address, ledger, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AccountInfo> ReadAccountAsync(string address, LedgerSnapshot ledger, CancellationToken token)
        {
            JObject response;
            try
            {
                response = await RpcAsync("account_info", new JObject { ["account"] = address, ["ledger_index"] = ledger.Index }, token).ConfigureAwait(false);
            }
            catch (XrplRpcException ex) when (ex.Code == "actNotFound")
            {
                return new AccountInfo { Address = address, LedgerIndex = ledger.Index, EstimatedReserveXrp = ledger.BaseReserve };
            }
            RequireValidated(response);
            if (RequiredUInt(response, "ledger_index") != ledger.Index)
                throw new InvalidDataException("Server returned a different account ledger.");
            var data = response["account_data"] as JObject ?? throw new InvalidDataException("Missing account data.");
            if ((string)data["Account"] != address) throw new InvalidDataException("Server returned a different account.");
            uint flags = RequiredUInt(data, "Flags");
            uint owners = RequiredUInt(data, "OwnerCount");
            decimal balance = DropsToXrp(data["Balance"]);
            decimal reserve = ledger.BaseReserve + ledger.OwnerReserve * owners;
            return new AccountInfo
            {
                Address = address, Exists = true, LedgerIndex = ledger.Index, BalanceXrp = balance,
                OwnerCount = owners, Sequence = RequiredUInt(data, "Sequence"), EstimatedReserveXrp = reserve,
                EstimatedSpendableXrp = Math.Max(0, balance - reserve), RegularKey = (string)data["RegularKey"],
                RequiresDestinationTag = (flags & 0x00020000) != 0,
                DepositAuthorization = (flags & 0x01000000) != 0,
                MasterKeyDisabled = (flags & 0x00100000) != 0
            };
        }

        public Task<TransactionInfo> GetTransactionAsync(string hash, CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateHash(hash);
            return ReadTransactionAsync(hash.ToUpperInvariant(), null, null, cancellationToken);
        }

        private async Task<TransactionInfo> ReadTransactionAsync(string hash, uint? from, uint? to, CancellationToken token, PaymentReceipt receipt = null)
        {
            var args = new JObject { ["transaction"] = hash, ["binary"] = false };
            if (from.HasValue) args["min_ledger"] = from.Value;
            if (to.HasValue) args["max_ledger"] = to.Value;
            try
            {
                var result = await RpcAsync("tx", args, token).ConfigureAwait(false);
                var tx = ParseTransaction(result);
                if (!string.Equals(tx.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Server returned a different transaction hash.");
                if (receipt != null) ValidateReceiptTransaction(result, tx, receipt);
                return tx;
            }
            catch (XrplRpcException ex) when (ex.Code == "txnNotFound")
            {
                var searched = ex.Response?["searched_all"];
                if (searched != null && searched.Type != JTokenType.Boolean) throw new InvalidDataException("Invalid searched_all proof.");
                return new TransactionInfo { Hash = hash, Status = TransactionStatus.Unknown,
                    SearchedAll = searched != null && (bool)searched };
            }
        }

        public async Task<PaymentHistoryPage> GetPaymentsAsync(string address, PaymentHistoryRequest request = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateClassicAddress(address, nameof(address));
            request = request ?? new PaymentHistoryRequest();
            if (request.Limit < 1 || request.Limit > 400) throw new ArgumentOutOfRangeException(nameof(request.Limit));
            long from = request.FromLedger.HasValue ? (long)request.FromLedger.Value : -1L;
            uint to;
            JToken marker = null;
            if (request.ContinuationToken != null)
            {
                JObject cursor;
                try { cursor = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(request.ContinuationToken))); }
                catch (Exception ex) when (ex is FormatException || ex is JsonException)
                { throw new ArgumentException("Invalid history continuation token.", nameof(request), ex); }
                if ((string)cursor["account"] != address || (string)cursor["endpoint"] != _options.RpcEndpoint.AbsoluteUri || (int?)cursor["version"] != 1)
                    throw new ArgumentException("History token belongs to a different account/server.", nameof(request));
                from = (long?)cursor["from"] ?? throw new ArgumentException("Missing history range.", nameof(request));
                to = RequiredUInt(cursor, "to");
                marker = cursor["marker"];
                if (marker == null || marker.Type == JTokenType.Null || from < -1 || from > to)
                    throw new ArgumentException("Invalid history token range/marker.", nameof(request));
                if ((request.FromLedger.HasValue && request.FromLedger.Value != from) || (request.ToLedger.HasValue && request.ToLedger.Value != to))
                    throw new ArgumentException("Cannot change the range while continuing history.", nameof(request));
            }
            else to = request.ToLedger ?? (await GetLedgerAsync(cancellationToken).ConfigureAwait(false)).Index;
            if (from > to) throw new ArgumentException("FromLedger must not exceed ToLedger.", nameof(request));
            var args = new JObject { ["account"] = address, ["ledger_index_min"] = from, ["ledger_index_max"] = to,
                ["forward"] = true, ["binary"] = false, ["limit"] = request.Limit };
            if (marker != null) args["marker"] = marker.DeepClone();
            var response = await RpcAsync("account_tx", args, cancellationToken).ConfigureAwait(false);
            RequireValidated(response);
            if ((string)response["account"] != address) throw new InvalidDataException("Server returned a different account history.");
            uint actualFrom = RequiredUInt(response, "ledger_index_min");
            uint actualTo = RequiredUInt(response, "ledger_index_max");
            if (actualFrom > actualTo || actualTo > to || (from >= 0 && actualFrom < from))
                throw new InvalidDataException("Server returned an invalid history range.");
            var entries = response["transactions"] as JArray ?? throw new InvalidDataException("Missing transaction history.");
            var payments = new List<TransactionInfo>();
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var transaction = ParseTransaction(entry as JObject);
                if (!transaction.Validated || !transaction.LedgerIndex.HasValue || transaction.LedgerIndex < actualFrom || transaction.LedgerIndex > actualTo)
                    throw new InvalidDataException("History contains a transaction outside its validated range.");
                // account_tx also includes trades and other transactions that merely affect owned objects.
                if (transaction.IsXrpPayment && (transaction.Sender == address || transaction.Destination == address) && hashes.Add(transaction.Hash))
                    payments.Add(transaction);
            }
            string next = null;
            if (response["marker"] != null && response["marker"].Type != JTokenType.Null)
            {
                var cursor = new JObject { ["version"] = 1, ["endpoint"] = _options.RpcEndpoint.AbsoluteUri,
                    ["account"] = address, ["from"] = from, ["to"] = to, ["marker"] = response["marker"].DeepClone() };
                next = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor.ToString(Formatting.None)));
            }
            return new PaymentHistoryPage { Payments = payments.AsReadOnly(), ContinuationToken = next,
                FromLedger = actualFrom, ToLedger = actualTo,
                RequestedRangeAvailable = actualTo >= to && (from == -1 || actualFrom <= from) };
        }

        private static TransactionInfo ParseTransaction(JObject envelope)
        {
            var tx = TransactionBody(envelope);
            var meta = envelope["meta"] as JObject;
            if (envelope["validated"]?.Type != JTokenType.Boolean) throw new InvalidDataException("Missing or invalid validated flag.");
            bool validated = (bool)envelope["validated"];
            string hash = ResponseString(envelope, "hash", false) ?? ResponseString(tx, "hash");
            try { ValidateHash(hash); }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid transaction hash in response.", ex); }
            if (tx["hash"] != null && !string.Equals(hash, ResponseString(tx, "hash"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Conflicting transaction hashes.");
            if (envelope["meta"] != null && meta == null) throw new InvalidDataException("Invalid transaction metadata.");
            string code = meta == null ? null : ResponseString(meta, "TransactionResult", false);
            if (validated && (code == null || (code != "tesSUCCESS" && !code.StartsWith("tec", StringComparison.Ordinal))))
                throw new InvalidDataException("Validated transaction has no valid final result.");
            uint? ledger = ResponseUInt(envelope["ledger_index"], "ledger_index") ?? ResponseUInt(tx["ledger_index"], "ledger_index");
            if (validated && (!ledger.HasValue || ledger == 0))
                throw new InvalidDataException("Validated transaction has no ledger index.");
            if (envelope["ledger_index"] != null && tx["ledger_index"] != null && ledger != ResponseUInt(tx["ledger_index"], "ledger_index"))
                throw new InvalidDataException("Conflicting transaction ledger indexes.");
            string type = ResponseString(tx, "TransactionType");
            string sender = ResponseAddress(tx, "Account");
            if (!ResponseUInt(tx["Sequence"], "Sequence").HasValue) throw new InvalidDataException("Missing transaction sequence.");
            decimal fee = DropsToXrp(ResponseString(tx, "Fee"));
            var amount = tx["DeliverMax"] ?? tx["Amount"];
            if (tx["DeliverMax"] != null && tx["Amount"] != null && !JToken.DeepEquals(tx["DeliverMax"], tx["Amount"]))
                throw new InvalidDataException("Conflicting Payment amount fields.");
            string destination = type == "Payment" ? ResponseAddress(tx, "Destination") : ResponseString(tx, "Destination", false);
            if (type == "Payment" && (amount == null || (amount.Type != JTokenType.String && !(amount is JObject))))
                throw new InvalidDataException("Missing or invalid Payment amount.");
            if (type == "Payment" && amount is JObject issued &&
                (issued["value"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)issued["value"]) ||
                !((issued["currency"]?.Type == JTokenType.String && issued["issuer"]?.Type == JTokenType.String) || issued["mpt_issuance_id"]?.Type == JTokenType.String)))
                throw new InvalidDataException("Invalid issued Payment amount.");
            bool xrpPayment = type == "Payment" && amount.Type == JTokenType.String;
            decimal? amountXrp = xrpPayment ? (decimal?)DropsToXrp(amount) : null;
            if (amountXrp == 0) throw new InvalidDataException("Payment amount must be positive.");
            var delivered = meta?["delivered_amount"];
            decimal? deliveredXrp = null;
            if (validated && code == "tesSUCCESS" && xrpPayment && delivered != null)
            {
                if (delivered.Type != JTokenType.String) throw new InvalidDataException("Invalid XRP delivered_amount.");
                if ((string)delivered != "unavailable")
                {
                    deliveredXrp = DropsToXrp(delivered);
                    if (deliveredXrp <= 0 || deliveredXrp > amountXrp) throw new InvalidDataException("Inconsistent XRP delivered_amount.");
                }
            }
            return new TransactionInfo
            {
                Hash = hash.ToUpperInvariant(), Found = true, Validated = validated,
                Status = !validated ? TransactionStatus.Pending : code == "tesSUCCESS" ? TransactionStatus.Succeeded : TransactionStatus.Failed,
                ResultCode = code, TransactionType = type, Sender = sender,
                Destination = destination, DestinationTag = ResponseUInt(tx["DestinationTag"], "DestinationTag"),
                LedgerIndex = ledger, TransactionIndex = ResponseUInt(meta?["TransactionIndex"], "TransactionIndex"),
                IsXrpPayment = xrpPayment, AmountXrp = amountXrp, DeliveredAmountXrp = deliveredXrp, FeeXrp = fee
            };
        }

        private static JObject TransactionBody(JObject envelope)
        {
            if (envelope == null) throw new InvalidDataException("Invalid transaction entry.");
            if (envelope["tx_json"] != null) return envelope["tx_json"] as JObject ?? throw new InvalidDataException("Invalid tx_json.");
            if (envelope["tx"] != null) return envelope["tx"] as JObject ?? throw new InvalidDataException("Invalid tx.");
            return envelope;
        }

        private static string ResponseString(JObject obj, string name, bool required = true)
        {
            var value = obj[name];
            if (value == null && !required) return null;
            if (value?.Type != JTokenType.String || string.IsNullOrEmpty((string)value)) throw new InvalidDataException("Missing or invalid " + name + ".");
            return (string)value;
        }

        private static string ResponseAddress(JObject obj, string name)
        {
            string value = ResponseString(obj, name);
            if (!RippleClient.ValidateAddress(value)) throw new InvalidDataException("Invalid " + name + " address.");
            return value;
        }

        private static uint? ResponseUInt(JToken token, string name)
        {
            if (token == null) return null;
            uint value;
            if (token.Type != JTokenType.Integer || !uint.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("Invalid " + name + ".");
            return value;
        }

        private static void ValidateReceiptTransaction(JObject envelope, TransactionInfo tx, PaymentReceipt receipt)
        {
            if (tx.Validated && (tx.LedgerIndex < receipt.FirstLedger || tx.LedgerIndex > receipt.LastLedger))
                throw new InvalidDataException("Payment was returned outside its signed ledger range.");
            var body = (JObject)TransactionBody(envelope).DeepClone();
            if (body["DeliverMax"] != null) { body["Amount"] = body["DeliverMax"].DeepClone(); body.Remove("DeliverMax"); }
            try
            {
                if (!string.Equals(StObject.FromJson(body).ToHex(), receipt.TxBlob, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Transaction body does not match the saved signed payment.");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidOperationException || ex is KeyNotFoundException)
            { throw new InvalidDataException("Invalid signed payment response.", ex); }
            if (tx.Status == TransactionStatus.Succeeded && tx.DeliveredAmountXrp != tx.AmountXrp)
                throw new InvalidDataException("Delivered XRP does not match the saved direct payment.");
        }

        private static void RequireValidated(JObject response)
        {
            if (response["validated"]?.Type != JTokenType.Boolean || !(bool)response["validated"])
                throw new InvalidDataException("Server returned unvalidated or invalid data.");
        }

        private static uint RequiredUInt(JObject obj, string name)
        {
            return ResponseUInt(obj[name], name) ?? throw new InvalidDataException("Missing " + name + ".");
        }

        private static decimal RequiredDecimal(JObject obj, string name)
        {
            decimal value;
            var token = obj[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float && token.Type != JTokenType.String) ||
                !decimal.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0 || value > 100000000000m)
                throw new InvalidDataException("Missing or invalid " + name + ".");
            return value;
        }

        private static decimal DropsToXrp(JToken token)
        {
            ulong drops;
            if (token == null || !ulong.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out drops) || drops > 100000000000000000UL)
                throw new InvalidDataException("Invalid XRP drops amount.");
            return drops / 1000000m;
        }

        private static void ValidateClassicAddress(string address, string name)
        {
            if (!RippleClient.ValidateAddress(address)) throw new ArgumentException("A valid classic XRPL address is required.", name);
        }

        private static void ValidateHash(string hash)
        {
            if (hash == null || hash.Length != 64) throw new ArgumentException("Transaction hash must contain 64 hexadecimal characters.", nameof(hash));
            foreach (char c in hash) if (!Uri.IsHexDigit(c)) throw new ArgumentException("Invalid transaction hash.", nameof(hash));
        }
    }
}
