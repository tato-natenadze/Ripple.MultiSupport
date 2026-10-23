using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.ValidateAddress;

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
            JObject txJson = JObject.FromObject(txObject);
            string secret = txJson["Seed"]?.ToString();
            txJson.Remove("Seed");
            SignedTx signed = TxSigner.SignJson(txJson, secret);
            return signed.TxBlob;
        }
    }
}
