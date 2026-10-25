using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.ValidateAddress;
using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Ripple
{
    public class RippleClient
    {
        /// <summary>Generates a new XRPL address, seed, and pubkey.</summary>
        public static AddressData GenerateAddress()
        {
            Seed seed = Seed.FromRandom();
            IKeyPair keyPair = seed.KeyPair();

            return new AddressData
            {
                Address = keyPair.Id(),
                Seed = seed.ToString()
            };
        }

        /// <summary>Validates XRPL address format.</summary>
        public static bool ValidateAddress(string address)
        {
            return AddressCodec.IsValidAddress(address);
        }

        /// <summary>Signs a transaction with seed (secret) and returns tx_blob.
        /// Example Transaction is

        /// var txObject = new
        ///{
        ///    TransactionType = "string",     type for example "Payment"
        ///    Account = "string",             address from
        ///    Destination = "string",         Address to
        ///    Amount = "string",              in drops (1 XRP = 1,000,000 drops)
        ///    Fee = "string",                 in drops
        ///    Sequence = int,                 account sequence number
        ///    DestinationTag = int,           optional
        ///    Seed = "string"                 seed comes when creating address
        ///};

        /// </summary>
        public static string SignTransaction(object txObject)
        {
            JObject txJson;
            if (txObject == null) throw new ArgumentNullException(nameof(txObject));
            using (var writer = new SigningJsonWriter())
            {
                JsonSerializer.CreateDefault().Serialize(writer, txObject);
                txJson = writer.Token as JObject ?? throw new ArgumentException("A transaction object is required.", nameof(txObject));
            }
            ValidateSigningInput(txJson, 0);
            string secret = txJson["Seed"]?.ToString();
            txJson.Remove("Seed");
            SignedTx signed = TxSigner.SignJson(txJson, secret);
            return signed.TxBlob;
        }

        private static void ValidateSigningInput(JObject json, int depth)
        {
            // Match rippled's STObject limit, including the array-item wrapper level.
            if (depth > 10) throw new ArgumentException("Maximum transaction nesting depth exceeded.", "txObject");
            foreach (var property in json.Properties())
            {
                if (!Field.Values.Has(property.Name)) continue;
                Field field = Field.Values[property.Name];
                JToken value = property.Value;
                if (field.Type == FieldType.Uint8 || field.Type == FieldType.Uint16 || field.Type == FieldType.Uint32)
                {
                    decimal number;
                    if (value.Type == JTokenType.Boolean || (value.Type == JTokenType.Float &&
                        (!decimal.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                         decimal.Truncate(number) != number)))
                        throw new ArgumentException("Unsigned transaction fields require whole numbers.", "txObject");
                }
                if (field.Type == FieldType.StObject && value is JObject nested)
                    ValidateSigningInput(nested, depth + 1);
                else if (field.Type == FieldType.StArray && value is JArray array)
                    foreach (var item in array)
                        if (item is JObject entry) ValidateSigningInput(entry, depth + 1);
            }
        }

        // Bound serialization before any recursive protocol conversion, including
        // unknown JSON fields. Valid protocol objects fit below this JSON limit.
        private sealed class SigningJsonWriter : JTokenWriter
        {
            private void CheckDepth()
            {
                if (Top >= 64) throw new ArgumentException("Maximum transaction JSON depth exceeded.", "txObject");
            }

            public override void WriteStartObject() { CheckDepth(); base.WriteStartObject(); }
            public override void WriteStartArray() { CheckDepth(); base.WriteStartArray(); }
            public override void WriteStartConstructor(string name)
            { throw new ArgumentException("JSON constructors are not transaction data.", "txObject"); }
        }
    }
}
