using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private static void ValidateAccountDeleteReceiptTransaction(JObject envelope, TransactionInfo tx, AccountDeleteReceipt receipt)
        {
            if (tx.TransactionType != "AccountDelete" || tx.Sender != receipt.Sender || tx.Destination != receipt.Destination ||
                tx.DestinationTag != receipt.DestinationTag || tx.FeeXrp != receipt.FeeXrp)
                throw new InvalidDataException("Transaction does not match the saved AccountDelete.");
            if (tx.Validated && (tx.LedgerIndex < receipt.FirstLedger || tx.LedgerIndex > receipt.LastLedger))
                throw new InvalidDataException("AccountDelete was returned outside its signed ledger range.");
            try
            {
                if (!string.Equals(StObject.FromJson(TransactionBody(envelope)).ToHex(), receipt.TxBlob, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Transaction body does not match the saved AccountDelete.");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException ||
                ex is InvalidOperationException || ex is KeyNotFoundException)
            { throw new InvalidDataException("Invalid signed AccountDelete response.", ex); }
        }

        private static decimal ReadAccountDeleteReturnedXrp(JObject envelope, AccountDeleteReceipt receipt)
        {
            var meta = envelope["meta"] as JObject ?? throw new InvalidDataException("Missing AccountDelete metadata.");
            var nodes = meta["AffectedNodes"] as JArray ?? throw new InvalidDataException("Missing AccountDelete balance metadata.");
            JObject source = null, destination = null;
            foreach (var item in nodes)
            {
                var wrapper = item as JObject ?? throw new InvalidDataException("Invalid affected ledger node.");
                foreach (string kind in new[] { "DeletedNode", "ModifiedNode", "CreatedNode" })
                {
                    var node = wrapper[kind] as JObject;
                    if (node == null || (string)node["LedgerEntryType"] != "AccountRoot") continue;
                    var fields = node[kind == "CreatedNode" ? "NewFields" : "FinalFields"] as JObject;
                    string account = fields == null ? null : ResponseString(fields, "Account", false);
                    if (account == receipt.Sender)
                    {
                        if (kind != "DeletedNode" || source != null) throw new InvalidDataException("AccountDelete did not uniquely delete its source account.");
                        source = node;
                    }
                    if (account == receipt.Destination)
                    {
                        if (kind != "ModifiedNode" || destination != null) throw new InvalidDataException("Invalid AccountDelete destination balance change.");
                        destination = node;
                    }
                }
            }
            if (source == null || DropsToXrp(source["FinalFields"]?["Balance"]) != 0)
                throw new InvalidDataException("Successful AccountDelete did not remove a zero-balance source account.");
            decimal? sourceReturn = null;
            if (source["PreviousFields"]?["Balance"] != null)
            {
                decimal before = DropsToXrp(source["PreviousFields"]["Balance"]);
                if (before < receipt.FeeXrp) throw new InvalidDataException("AccountDelete fee exceeds its source balance.");
                sourceReturn = before - receipt.FeeXrp;
            }
            decimal? delivered = null;
            if (meta["delivered_amount"] != null)
            {
                if (meta["delivered_amount"].Type != JTokenType.String)
                    throw new InvalidDataException("Invalid AccountDelete delivered_amount.");
                if ((string)meta["delivered_amount"] != "unavailable") delivered = DropsToXrp(meta["delivered_amount"]);
            }
            decimal returned;
            if (destination != null)
            {
                decimal after = DropsToXrp(destination["FinalFields"]?["Balance"]);
                decimal before = destination["PreviousFields"]?["Balance"] == null ? after :
                    DropsToXrp(destination["PreviousFields"]["Balance"]);
                if (after < before) throw new InvalidDataException("AccountDelete destination lost XRP.");
                returned = after - before;
            }
            else if (sourceReturn == 0 || delivered == 0) returned = 0;
            else throw new InvalidDataException("AccountDelete has no destination XRP credit proof.");
            if ((sourceReturn.HasValue && sourceReturn != returned) || (delivered.HasValue && delivered != returned))
                throw new InvalidDataException("AccountDelete returned XRP disagrees with its validated balance changes.");
            return returned;
        }
    }
}
