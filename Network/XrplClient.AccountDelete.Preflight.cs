using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private sealed class AccountDeleteInput
        {
            internal string Destination;
            internal uint? Tag;
            internal bool? TestNetwork;
            internal decimal MaxFee;
            internal Func<AccountDeleteReceipt, CancellationToken, Task> Prepared;
        }

        private static AccountDeleteInput SnapshotAccountDeleteRequest(AccountDeleteRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string destination = request.Destination;
            uint? tag = request.DestinationTag;
            decimal maxFee = request.MaxFeeXrp;
            var prepared = request.OnPreparedAsync;
            bool? network;
            destination = NormalizePaymentDestination(destination, ref tag, out network, nameof(request));
            ValidateClassicAddress(destination, nameof(request.Destination));
            XrpToDrops(maxFee, nameof(request.MaxFeeXrp));
            return new AccountDeleteInput { Destination = destination, Tag = tag, TestNetwork = network,
                MaxFee = maxFee, Prepared = prepared };
        }

        private async Task<AccountDeleteQuote> ReadAccountDeleteQuoteAsync(string sender, AccountDeleteInput input, CancellationToken token)
        {
            var ledger = await GetPaymentLedgerAsync(input.TestNetwork, token, "request").ConfigureAwait(false);
            if (ledger.Index == 0 || ledger.Index == uint.MaxValue || ledger.OwnerReserve <= 0 ||
                decimal.Truncate(ledger.OwnerReserve * 1000000m) != ledger.OwnerReserve * 1000000m)
                throw new InvalidDataException("The server returned an invalid AccountDelete ledger or fee.");
            JObject sourceData = null;
            var source = await ReadAccountAsync(sender, ledger, token, data => sourceData = data).ConfigureAwait(false);
            if (!source.Exists) throw new InvalidOperationException("The account to delete is not funded.");
            if (source.Sequence == 0) throw new InvalidDataException("The source account has an invalid sequence.");
            var target = await ReadAccountAsync(input.Destination, ledger, token).ConfigureAwait(false);
            var reasons = new List<string>();
            if (source.MasterKeyDisabled) reasons.Add("The account's master key is disabled; this method requires the master seed.");
            if (!target.Exists) reasons.Add("AccountDelete requires an already funded destination account.");
            if (target.RequiresDestinationTag && !input.Tag.HasValue) reasons.Add("The destination requires a DestinationTag.");
            if (target.Exists && target.DepositAuthorization)
            {
                var authorization = await RpcAsync("deposit_authorized", new JObject { ["source_account"] = sender,
                    ["destination_account"] = input.Destination, ["ledger_index"] = ledger.Index }, token).ConfigureAwait(false);
                if (authorization["deposit_authorized"]?.Type != JTokenType.Boolean)
                    throw new InvalidDataException("Invalid deposit authorization response.");
                if (!(bool)authorization["deposit_authorized"])
                    reasons.Add("The destination has not preauthorized this account's deletion transfer.");
            }
            uint minted = ResponseUInt(sourceData["MintedNFTokens"], "MintedNFTokens") ?? 0;
            uint burned = ResponseUInt(sourceData["BurnedNFTokens"], "BurnedNFTokens") ?? 0;
            uint firstNft = ResponseUInt(sourceData["FirstNFTokenSequence"], "FirstNFTokenSequence") ?? 0;
            if (minted != burned) reasons.Add("The account has outstanding issued NFTs.");
            foreach (string name in new[] { "SponsoringOwnerCount", "SponsoringAccountCount" })
            {
                if (sourceData[name] == null) continue;
                RequiredUInt(sourceData, name);
                reasons.Add("The account has outstanding reserve sponsorship obligations.");
                break;
            }
            if (sourceData["Sponsor"] != null && ResponseAddress(sourceData, "Sponsor") != input.Destination)
                reasons.Add("A sponsored account's remaining XRP must be sent to its sponsor.");

            var current = await RpcAsync("account_info", new JObject { ["account"] = sender,
                ["ledger_index"] = "current", ["queue"] = true }, token).ConfigureAwait(false);
            var currentData = current["account_data"] as JObject;
            if (currentData == null || (string)currentData["Account"] != sender)
                throw new InvalidDataException("Server returned different current account data.");
            var queue = current["queue_data"];
            if (queue != null && !(queue is JObject)) throw new InvalidDataException("Invalid account queue data.");
            uint queued = ResponseUInt(queue?["txn_count"], "queue_data.txn_count") ?? 0;
            if (RequiredUInt(currentData, "Sequence") != source.Sequence || queued != 0)
                reasons.Add("The account has pending or external transactions. Wait for validation before deletion.");
            uint observed = Math.Max(ledger.Index, ResponseUInt(current["ledger_current_index"], "ledger_current_index") ?? ledger.Index);
            // rippled AccountDelete::preclaim: account/NFT sequence + 255 <= current ledger.
            // https://github.com/XRPLF/rippled/blob/release/3.4.x/src/libxrpl/tx/transactors/account/AccountDelete.cpp
            // UInt64 arithmetic prevents either sequence bound from wrapping.
            ulong earliest = Math.Max((ulong)source.Sequence + 255UL, (ulong)firstNft + minted + 255UL);
            ulong remaining = earliest > observed ? earliest - observed : 0;
            if (remaining != 0) reasons.Add("The account/NFT sequence is too recent for AccountDelete. Wait " + remaining + " more ledger(s).");
            if (earliest > uint.MaxValue || (ulong)observed + _options.LastLedgerOffset > uint.MaxValue)
                reasons.Add("The account deletion cannot fit a valid signed ledger range.");

            int blockers = await CountAccountDeleteObjectsAsync(sender, ledger, true, token).ConfigureAwait(false);
            if (blockers != 0) reasons.Add("The account has ledger objects that prevent deletion.");
            else if (await CountAccountDeleteObjectsAsync(sender, ledger, false, token).ConfigureAwait(false) > 1000)
                reasons.Add("AccountDelete cannot automatically remove more than 1000 linked ledger objects.");
            decimal fee = ledger.OwnerReserve;
            if (fee > input.MaxFee) reasons.Add("The AccountDelete fee exceeds MaxFeeXrp.");
            if (source.BalanceXrp < fee) reasons.Add("The account balance cannot cover the AccountDelete fee.");
            return new AccountDeleteQuote { Sender = sender, Destination = input.Destination, DestinationTag = input.Tag,
                LedgerIndex = ledger.Index, ObservedLedgerIndex = observed, Sequence = source.Sequence,
                EarliestLedgerIndex = earliest, LedgersUntilEligible = remaining, BalanceXrp = source.BalanceXrp,
                EstimatedReserveXrp = source.EstimatedReserveXrp, EstimatedFeeXrp = fee,
                EstimatedReturnedXrp = Math.Max(0m, source.BalanceXrp - fee), MaxFeeXrp = input.MaxFee,
                BlockingReasons = reasons.AsReadOnly() };
        }

        private async Task<int> CountAccountDeleteObjectsAsync(string account, LedgerSnapshot ledger, bool blockersOnly, CancellationToken token)
        {
            JToken marker = null;
            var markers = new HashSet<string>(StringComparer.Ordinal);
            var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int page = 0; page < 128; page++)
            {
                var args = new JObject { ["account"] = account, ["ledger_index"] = ledger.Index,
                    ["deletion_blockers_only"] = blockersOnly, ["limit"] = 400 };
                if (marker != null) args["marker"] = marker.DeepClone();
                var response = await RpcAsync("account_objects", args, token).ConfigureAwait(false);
                RequireValidated(response);
                if ((string)response["account"] != account || RequiredUInt(response, "ledger_index") != ledger.Index)
                    throw new InvalidDataException("Server returned different account objects or ledger.");
                var objects = response["account_objects"] as JArray ?? throw new InvalidDataException("Missing account objects.");
                if (objects.Count > 400) throw new InvalidDataException("Account object page exceeds its requested limit.");
                foreach (var item in objects)
                {
                    var entry = item as JObject ?? throw new InvalidDataException("Invalid account object.");
                    string index = ResponseString(entry, "index");
                    try { ValidateHash(index); }
                    catch (ArgumentException ex) { throw new InvalidDataException("Invalid account object index.", ex); }
                    if (!indexes.Add(index)) throw new InvalidDataException("Account object pagination repeated an entry.");
                    if (blockersOnly || indexes.Count > 1000) return indexes.Count;
                }
                marker = response["marker"];
                if (marker == null || marker.Type == JTokenType.Null) return indexes.Count;
                string serialized = marker.ToString(Formatting.None);
                if (serialized.Length > 65536 || !markers.Add(serialized))
                    throw new InvalidDataException("Account object pagination returned an invalid or repeated marker.");
            }
            throw new InvalidDataException("The server returned too many account object pages to establish deletion eligibility.");
        }
    }
}
