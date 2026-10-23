using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Ripple.LibraryClasses.Signer.Keys
{
    public class Secp256K1
    {
        private static readonly ECDomainParameters EcParams;

        static Secp256K1()
        {
            X9ECParameters byName = SecNamedCurves.GetByName("secp256k1");
            EcParams = new ECDomainParameters(byName.Curve, byName.G, byName.N, byName.H);
        }

        public static ECDomainParameters Parameters()
        {
            return EcParams;
        }

        public static BigInteger Order()
        {
            return EcParams.N;
        }

        public static ECCurve Curve()
        {
            return EcParams.Curve;
        }

        public static ECPoint BasePoint()
        {
            return EcParams.G;
        }
    }
}
