using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.Signer.Keys;
using Ripple.LibraryClasses.ValidateAddress;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Ripple.LibraryClasses.CreateAddress
{
    public class Seed
    {
        private readonly byte[] _seedBytes;

        private string _keyType = "secp256k1";

        private bool _isNodeKey;

        public Seed(byte[] seedBytes)
            : this("secp256k1", seedBytes)
        {
        }

        public Seed(string version, byte[] seedBytes)
        {
            _seedBytes = seedBytes;
            _keyType = version;
        }

        public override string ToString()
        {
            return AddressCodec.EncodeSeed(_seedBytes, _keyType);
        }

        public byte[] Bytes()
        {
            return _seedBytes;
        }

        public Seed SetEd25519()
        {
            _keyType = "ed25519";
            return this;
        }

        public Seed SetNodeKey()
        {
            _isNodeKey = true;
            return this;
        }

        public IKeyPair KeyPair()
        {
            return KeyPair(0);
        }

        public IKeyPair RootKeyPair()
        {
            return KeyPair(-1);
        }

        public IKeyPair KeyPair(int pairIndex)
        {
            if (_keyType == "ed25519")
            {
                if (pairIndex != 0)
                {
                    throw new ArgumentException("`account` is ignored for ed25519");
                }

                return EdKeyPair.From128Seed(_seedBytes);
            }

            pairIndex = _isNodeKey ? -1 : pairIndex;
            K256KeyPair k256KeyPair = K256KeyGenerator.From128Seed(_seedBytes, pairIndex);
            if (_isNodeKey)
            {
                k256KeyPair.SetNodeKey();
            }

            return k256KeyPair;
        }

        public static Seed FromBase58(string b58)
        {
            AddressCodec.DecodedSeed decodedSeed = AddressCodec.DecodeSeed(b58);
            return new Seed(decodedSeed.Type, decodedSeed.Bytes);
        }

        public static Seed FromPassPhrase(string passPhrase)
        {
            return new Seed(PassPhraseToSeedBytes(passPhrase));
        }

        public static Seed FromRandom()
        {
            using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
            {
                byte[] array = new byte[16];
                randomNumberGenerator.GetBytes(array);
                return new Seed(array);
            }
        }

        private static byte[] PassPhraseToSeedBytes(string phrase)
        {
            return new Sha512(Encoding.UTF8.GetBytes(phrase.ToCharArray())).Finish128();
        }
    }
}