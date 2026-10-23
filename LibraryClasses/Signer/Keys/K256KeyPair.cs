using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple.LibraryClasses.Signer.Keys
{
    public class K256KeyPair : K256VerifyingKey, IKeyPair
    {
        private readonly BigInteger _privKey;

        private ECDsaSigner _signer;

        private bool _isNodeKey;

        public K256KeyPair(BigInteger priv)
            : this(priv, K256KeyGenerator.ComputePublicKey(priv))
        {
        }

        internal K256KeyPair(BigInteger priv, ECPoint pub)
            : base(pub)
        {
            _privKey = priv;
            InitSigner(priv);
        }

        private void InitSigner(BigInteger priv)
        {
            _signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
            ECPrivateKeyParameters parameters = new ECPrivateKeyParameters(priv, Secp256K1.Parameters());
            _signer.Init(forSigning: true, parameters);
        }

        internal K256KeyPair SetNodeKey()
        {
            _isNodeKey = true;
            return this;
        }

        public BigInteger Priv()
        {
            return _privKey;
        }

        public byte[] Sign(byte[] message)
        {
            return SignHash(Sha512.Half(message));
        }

        private byte[] SignHash(byte[] bytes)
        {
            return CreateEcdsaSignature(bytes).EncodeToDer();
        }

        private EcdsaSignature CreateEcdsaSignature(byte[] hash)
        {
            BigInteger[] array = _signer.GenerateSignature(hash);
            BigInteger r = array[0];
            BigInteger bigInteger = array[1];
            BigInteger bigInteger2 = Secp256K1.Order().Subtract(bigInteger);
            if (bigInteger.CompareTo(bigInteger2) == 1)
            {
                bigInteger = bigInteger2;
            }

            return new EcdsaSignature(r, bigInteger);
        }

        public string Id()
        {
            if (_isNodeKey)
            {
                return AddressCodec.EncodeNodePublic(CanonicalPubBytes());
            }

            return AddressCodec.EncodeAddress(this.PubKeyHash());
        }
    }
}
