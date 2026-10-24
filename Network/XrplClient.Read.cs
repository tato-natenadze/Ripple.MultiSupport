using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            if (ledger["age"] != null && (double)ledger["age"] > 120)
                throw new InvalidDataException("Server's validated ledger is stale.");
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

        private async Task<TransactionInfo> ReadTransactionAsync(string hash, uint? from, uint? to, CancellationToken token)
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
                return tx;
            }
            catch (XrplRpcException ex) when (ex.Code == "txnNotFound")
            {
                return new TransactionInfo { Hash = hash, Status = TransactionStatus.Unknown,
                    SearchedAll = (bool?)ex.Response?["searched_all"] == true };
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
                var transaction = ParseTransaction((JObject)entry);
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
            var tx = envelope["tx_json"] as JObject ?? envelope["tx"] as JObject ?? envelope;
            var meta = envelope["meta"] as JObject;
            bool validated = (bool?)envelope["validated"] == true;
            string hash = (string)envelope["hash"] ?? (string)tx["hash"];
            ValidateHash(hash);
            string code = (string)meta?["TransactionResult"];
            if (validated && code == null) throw new InvalidDataException("Validated transaction has no final result.");
            if (validated && envelope["ledger_index"] == null && tx["ledger_index"] == null)
                throw new InvalidDataException("Validated transaction has no ledger index.");
            var amount = tx["DeliverMax"] ?? tx["Amount"];
            bool xrpPayment = (string)tx["TransactionType"] == "Payment" && amount?.Type == JTokenType.String;
            var delivered = meta?["delivered_amount"];
            decimal? deliveredXrp = null;
            if (validated && code == "tesSUCCESS" && xrpPayment && delivered?.Type == JTokenType.String && (string)delivered != "unavailable")
                deliveredXrp = DropsToXrp(delivered);
            return new TransactionInfo
            {
                Hash = hash.ToUpperInvariant(), Found = true, Validated = validated,
                Status = !validated ? TransactionStatus.Pending : code == "tesSUCCESS" ? TransactionStatus.Succeeded : TransactionStatus.Failed,
                ResultCode = code, TransactionType = (string)tx["TransactionType"], Sender = (string)tx["Account"],
                Destination = (string)tx["Destination"], DestinationTag = (uint?)tx["DestinationTag"],
                LedgerIndex = (uint?)(envelope["ledger_index"] ?? tx["ledger_index"]), TransactionIndex = (uint?)meta?["TransactionIndex"],
                IsXrpPayment = xrpPayment, AmountXrp = xrpPayment ? (decimal?)DropsToXrp(amount) : null,
                DeliveredAmountXrp = deliveredXrp, FeeXrp = tx["Fee"] == null ? null : (decimal?)DropsToXrp(tx["Fee"])
            };
        }

        private static void RequireValidated(JObject response)
        {
            if ((bool?)response["validated"] != true) throw new InvalidDataException("Server returned unvalidated data.");
        }

        private static uint RequiredUInt(JObject obj, string name)
        {
            uint value;
            if (obj[name] == null || !uint.TryParse(obj[name].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("Missing or invalid " + name + ".");
            return value;
        }

        private static decimal RequiredDecimal(JObject obj, string name)
        {
            decimal value;
            if (obj[name] == null || !decimal.TryParse((string)obj[name], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0)
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
