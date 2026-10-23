using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Ripple.LibraryClasses.Signer.Keys
{
    public class K256KeyGenerator
    {
        public static K256KeyPair From128Seed(byte[] seedBytes, int keyIndex)
        {
            BigInteger bigInteger = ComputePrivateGen(seedBytes);
            if (keyIndex == -1)
            {
                return new K256KeyPair(bigInteger);
            }

            return new K256KeyPair(ComputeSecretKey(bigInteger, (uint)keyIndex));
        }

        public static BigInteger ComputePrivateGen(byte[] seedBytes)
        {
            return ComputeScalar(seedBytes, null);
        }

        public static BigInteger ComputeSecretKey(BigInteger privateGen, uint accountNumber)
        {
            return ComputeScalar(ComputePublicGenerator(privateGen).GetEncoded(compressed: true), accountNumber).Add(privateGen).Mod(Secp256K1.Order());
        }

        public static ECPoint ComputePublicGenerator(BigInteger privateGen)
        {
            return ComputePublicKey(privateGen);
        }

        public static byte[] ComputePublicKey(byte[] publicGenBytes, uint accountNumber)
        {
            ECPoint eCPoint = Secp256K1.Curve().DecodePoint(publicGenBytes);
            BigInteger b = ComputeScalar(publicGenBytes, accountNumber);
            ECPoint b2 = Secp256K1.BasePoint().Multiply(b);
            return eCPoint.Add(b2).GetEncoded(compressed: true);
        }

        public static BigInteger ComputeScalar(byte[] seedBytes, uint? discriminator)
        {
            BigInteger bigInteger = null;
            for (uint num = 0u; num <= 4294967295L; num++)
            {
                Sha512 sha = new Sha512(seedBytes);
                if (discriminator.HasValue)
                {
                    sha.AddU32(discriminator.Value);
                }

                sha.AddU32(num);
                bigInteger = Misc.UBigInt(sha.Finish256());
                if (bigInteger.CompareTo(BigInteger.Zero) == 1 && bigInteger.CompareTo(Secp256K1.Order()) == -1)
                {
                    break;
                }
            }

            return bigInteger;
        }

        public static ECPoint ComputePublicKey(BigInteger secretKey)
        {
            return Secp256K1.BasePoint().Multiply(secretKey);
        }
    }
}
