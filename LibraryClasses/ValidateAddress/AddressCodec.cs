using Ripple.LibraryClasses.Signer;

namespace Ripple.LibraryClasses.ValidateAddress
{
    public class AddressCodec
    {
        public class DecodedSeed
        {
            public readonly string Type;

            public readonly byte[] Bytes;

            public DecodedSeed(string type, byte[] payload)
            {
                Type = type;
                Bytes = payload;
            }
        }

        public const string Alphabet = "rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz";

        public static B58.Version AccountId;

        public static B58.Version NodePublic;

        public static B58.Version NodePrivate;

        public static B58.Version AccountPublic;

        public static B58.Version AccountPrivate;

        public static B58.Version FamilyGenerator;

        public static B58.Version K256Seed;

        public static B58.Version Ed25519Seed;

        public static B58.Versions AnySeed;

        private static readonly B58 B58;

        static AddressCodec()
        {
            AccountId = B58.Version.With(0, 20);
            NodePublic = B58.Version.With(28, 33);
            NodePrivate = B58.Version.With(32, 32);
            AccountPublic = B58.Version.With(35, 33);
            AccountPrivate = B58.Version.With(34, 32);
            FamilyGenerator = B58.Version.With(41, 33);
            K256Seed = B58.Version.With(33, 16);
            Ed25519Seed = B58.Version.With(new byte[3] { 1, 225, 75 }, 16);
            AnySeed = B58.Versions.With("secp256k1", K256Seed).And("ed25519", Ed25519Seed);
            B58 = new B58("rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz");
        }

        public static DecodedSeed DecodeSeed(string seed)
        {
            B58.Decoded decoded = B58.Decode(seed, AnySeed);
            return new DecodedSeed(decoded.Type, decoded.Payload);
        }

        public static string EncodeSeed(byte[] bytes, string type)
        {
            return B58.Encode(bytes, type, AnySeed);
        }

        public static byte[] DecodeK256Seed(string seed)
        {
            return B58.Decode(seed, K256Seed);
        }

        public static string EncodeK256Seed(byte[] bytes)
        {
            return B58.Encode(bytes, K256Seed);
        }

        public static byte[] DecodeEdSeed(string base58)
        {
            return B58.Decode(base58, Ed25519Seed);
        }

        public static string EncodeEdSeed(byte[] bytes)
        {
            return B58.Encode(bytes, Ed25519Seed);
        }

        public static string EncodeAddress(byte[] bytes)
        {
            return B58.Encode(bytes, AccountId);
        }

        public static string EncodeNodePublic(byte[] bytes)
        {
            return B58.Encode(bytes, NodePublic);
        }

        public static byte[] DecodeNodePublic(string publicKey)
        {
            return B58.Decode(publicKey, NodePublic);
        }

        public static byte[] DecodeAddress(string address)
        {
            return B58.Decode(address, AccountId);
        }

        public static bool IsValidAddress(string address)
        {
            if (address == null || address.Length < 25 || address.Length > 35 || address[0] != 'r') return false;
            return B58.IsValid(address, AccountId);
        }

        public static bool IsValidNodePublic(string nodePublic)
        {
            return B58.IsValid(nodePublic, NodePublic);
        }

        public static bool IsValidSeed(string seed)
        {
            return B58.IsValid(seed, AnySeed);
        }

        public static bool IsValidEdSeed(string edSeed)
        {
            return B58.IsValid(edSeed, Ed25519Seed);
        }

        public static bool IsValidK256Seed(string k256Seed)
        {
            return B58.IsValid(k256Seed, K256Seed);
        }
    }
}
