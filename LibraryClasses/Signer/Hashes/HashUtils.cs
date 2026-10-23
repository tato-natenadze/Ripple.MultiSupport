using System.Security.Cryptography;

namespace Ripple.LibraryClasses.Signer.Hashes
{
    public class HashUtils
    {
        public static byte[] DoubleDigest(byte[] buffer)
        {
            return Sha256(Sha256(buffer));
        }

        public static byte[] Sha256(byte[] buffer)
        {
            using (SHA256 digest = SHA256.Create())
            {
                return digest.ComputeHash(buffer);
            }
        }

        public static byte[] PublicKeyHash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            using (RIPEMD160 ripemd = RIPEMD160.Create())
            {
                bytes = sha.ComputeHash(bytes, 0, bytes.Length);
                return ripemd.ComputeHash(bytes, 0, bytes.Length);
            }
        }
    }
}
