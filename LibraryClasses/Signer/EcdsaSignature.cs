using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Math;
using Ripple.LibraryClasses.Signer.Keys;
using System;
using System.IO;

namespace Ripple.LibraryClasses.Signer
{
    public class EcdsaSignature
    {
        public BigInteger R;

        public BigInteger S;

        public EcdsaSignature(BigInteger r, BigInteger s)
        {
            R = r;
            S = s;
        }

        public static bool IsStrictlyCanonical(byte[] sig)
        {
            return CheckIsCanonical(sig, strict: true);
        }

        public static bool CheckIsCanonical(byte[] sig, bool strict)
        {
            int num = sig.Length;
            if (num < 8 || num > 72)
            {
                return false;
            }

            if (sig[0] != 48 || sig[1] != num - 2)
            {
                return false;
            }

            int num2 = 4;
            int num3 = sig[num2 - 1];
            if (num3 < 1 || num3 > 33 || num3 + 7 > num)
            {
                return false;
            }

            int num4 = num3 + 6;
            int num5 = sig[num4 - 1];
            if (num5 < 1 || num5 > 33 || num3 + num5 + 6 != num)
            {
                return false;
            }

            if (sig[num2 - 2] != 2 || sig[num4 - 2] != 2)
            {
                return false;
            }

            if ((sig[num2] & 0x80u) != 0)
            {
                return false;
            }

            if (sig[num2] == 0 && num3 == 1)
            {
                return false;
            }

            if (sig[num2] == 0 && (sig[num2 + 1] & 0x80) == 0)
            {
                return false;
            }

            if ((sig[num4] & 0x80u) != 0)
            {
                return false;
            }

            if (sig[num4] == 0 && num5 == 1)
            {
                return false;
            }

            if (sig[num4] == 0 && (sig[num4 + 1] & 0x80) == 0)
            {
                return false;
            }

            byte[] array = new byte[num3];
            byte[] array2 = new byte[num5];
            Array.Copy(sig, num2, array, 0, num3);
            Array.Copy(sig, num4, array2, 0, num5);
            BigInteger bigInteger = new BigInteger(1, array);
            BigInteger bigInteger2 = new BigInteger(1, array2);
            BigInteger bigInteger3 = Secp256K1.Order();
            if (bigInteger.CompareTo(bigInteger3) != -1 || bigInteger2.CompareTo(bigInteger3) != -1)
            {
                return false;
            }

            if (strict)
            {
                return bigInteger3.Subtract(bigInteger2).CompareTo(bigInteger2) != -1;
            }

            return true;
        }

        public byte[] EncodeToDer()
        {
            return DerByteStream().ToArray();
        }

        public static EcdsaSignature DecodeFromDer(byte[] bytes)
        {
            Asn1InputStream asn1InputStream = new Asn1InputStream(bytes);
            DerInteger derInteger;
            DerInteger derInteger2;
            try
            {
                DerSequence obj = (DerSequence)asn1InputStream.ReadObject();
                derInteger = (DerInteger)obj[0];
                derInteger2 = (DerInteger)obj[1];
            }
            catch (InvalidCastException)
            {
                return null;
            }
            finally
            {
                asn1InputStream.Close();
            }

            return new EcdsaSignature(derInteger.PositiveValue, derInteger2.PositiveValue);
        }

        protected internal MemoryStream DerByteStream()
        {
            MemoryStream memoryStream = new MemoryStream(72);
            DerSequenceGenerator derSequenceGenerator = new DerSequenceGenerator(memoryStream);
            derSequenceGenerator.AddObject(new DerInteger(R));
            derSequenceGenerator.AddObject(new DerInteger(S));
            derSequenceGenerator.Close();
            return memoryStream;
        }
    }
}
