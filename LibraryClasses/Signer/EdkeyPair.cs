using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.Signer.Keys;
using Ripple.LibraryClasses.ValidateAddress;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public class EdKeyPair : IKeyPair
    {
        private byte[] _canonicalisedPubBytes;

        private readonly byte[] _pubBytes;

        private readonly byte[] _privBytes;

        private EdKeyPair(byte[] publicKey, byte[] expandedPrivateKey)
        {
            _pubBytes = publicKey;
            _privBytes = expandedPrivateKey;
            ComputeCanonicalPub();
        }

        public byte[] CanonicalPubBytes()
        {
            return _canonicalisedPubBytes;
        }

        private void ComputeCanonicalPub()
        {
            _canonicalisedPubBytes = new byte[33];
            _canonicalisedPubBytes[0] = 237;
            Array.Copy(_pubBytes, 0, _canonicalisedPubBytes, 1, _pubBytes.Length);
        }

        public byte[] Sign(byte[] message)
        {
            return Chaos.NaCl.Ed25519.Sign(message, _privBytes);
        }

        public bool Verify(byte[] message, byte[] signature)
        {
            return Chaos.NaCl.Ed25519.Verify(signature, message, _pubBytes);
        }

        internal static IKeyPair From128Seed(byte[] seed)
        {
            byte[] privateKeySeed = new Sha512(seed).Finish256();

            Chaos.NaCl.Ed25519.KeyPairFromSeed(out byte[] publicKey, out byte[] expandedPrivateKey, privateKeySeed);
            return new EdKeyPair(publicKey, expandedPrivateKey);
        }

        public string Id()
        {
            return AddressCodec.EncodeAddress(this.PubKeyHash());
        }
    }
}
