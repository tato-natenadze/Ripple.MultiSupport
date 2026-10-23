using Ripple.LibraryClasses.Signer.Hashes;
using Ripple.LibraryClasses.Signer.Interfaces;

namespace Ripple.LibraryClasses.Signer
{
    public static class KeyPairExtensions
    {
        public static byte[] PubKeyHash(this IKeyPair pair)
        {
            return HashUtils.PublicKeyHash(pair.CanonicalPubBytes());
        }
    }
}
