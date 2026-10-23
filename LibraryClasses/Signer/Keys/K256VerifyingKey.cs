using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC;

namespace Ripple.LibraryClasses.Signer.Keys
{
    public class K256VerifyingKey
    {
        protected ECPoint PubKey;

        protected ECDsaSigner Verifier;

        protected byte[] PubKeyBytes;

        public K256VerifyingKey(ECPoint pub)
        {
            PubKey = pub;
            PubKeyBytes = pub.GetEncoded(compressed: true);
            SetVerifier(pub);
        }

        public byte[] CanonicalPubBytes()
        {
            return PubKeyBytes;
        }

        public bool Verify(byte[] message, byte[] signature)
        {
            byte[] data = Sha512.Half(message);
            return VerifyHash(data, signature);
        }

        private bool VerifyHash(byte[] data, byte[] signature)
        {
            EcdsaSignature ecdsaSignature = EcdsaSignature.DecodeFromDer(signature);
            if (ecdsaSignature != null)
            {
                return Verifier.VerifySignature(data, ecdsaSignature.R, ecdsaSignature.S);
            }

            return false;
        }

        protected void SetVerifier(ECPoint pub)
        {
            Verifier = new ECDsaSigner();
            ECPublicKeyParameters parameters = new ECPublicKeyParameters(pub, Secp256K1.Parameters());
            Verifier.Init(forSigning: false, parameters);
        }
    }
}
