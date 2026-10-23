using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public class TxSigner
    {
        public const uint CanonicalSigFlag = 2147483648u;

        private readonly IKeyPair _keyPair;

        private TxSigner(IKeyPair keyPair)
        {
            _keyPair = keyPair;
        }

        private TxSigner(string secret)
            : this(Seed.FromBase58(secret).KeyPair())
        {
        }

        public SignedTx SignJson(JObject tx)
        {
            StObject tx2;
            try
            {
                tx2 = StObject.FromJson(tx, strict: true);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Transaction is not valid.", "tx", ex);
            }

            return SignStObject(tx2);
        }

        public static TxSigner FromKeyPair(IKeyPair keyPair)
        {
            return new TxSigner(keyPair);
        }

        public static TxSigner FromSecret(string secret)
        {
            return new TxSigner(secret);
        }

        public static SignedTx SignJson(JObject tx, string secret)
        {
            return FromSecret(secret).SignJson(tx);
        }

        public SignedTx SignStObject(StObject tx)
        {
            tx.SetFlag(2147483648u);
            tx[Field.SigningPubKey] = _keyPair.CanonicalPubBytes();
            tx[Field.TxnSignature] = _keyPair.Sign(tx.SigningData());
            return ValidateAndEncode(tx);
        }

        public static SignedTx ValidateAndEncode(StObject tx)
        {
            try
            {
                TxFormat.Validate(tx);
            }
            catch (FormatException innerException)
            {
                throw new ArgumentException("Transaction is not valid.", "tx", innerException);
            }

            byte[] array = tx.ToBytes();
            return new SignedTx(Utils.TransactionId(array), B16.Encode(array), tx.ToJsonObject());
        }
    }
}
