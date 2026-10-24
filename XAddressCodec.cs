using System;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple
{
    /// <summary>XLS-5 Base58Check addresses containing an account, optional 32-bit tag and network flag.</summary>
    public static class XAddressCodec
    {
        private static readonly B58 Codec = new B58(AddressCodec.Alphabet);
        private static readonly B58.Version Classic = B58.Version.With(0, 20);
        private static readonly B58.Version Main = B58.Version.With(new byte[] { 0x05, 0x44 }, 29);
        private static readonly B58.Version Test = B58.Version.With(new byte[] { 0x04, 0x93 }, 29);

        public static string Encode(string classicAddress, uint? destinationTag, bool isTestNetwork)
        {
            if (!RippleClient.ValidateAddress(classicAddress)) throw new ArgumentException("A valid classic address is required.", nameof(classicAddress));
            byte[] account = Codec.Decode(classicAddress, Classic);
            var payload = new byte[29];
            Buffer.BlockCopy(account, 0, payload, 0, 20);
            if (destinationTag.HasValue)
            {
                payload[20] = 1;
                uint tag = destinationTag.Value;
                for (int i = 0; i < 4; i++) payload[21 + i] = (byte)(tag >> (8 * i));
            }
            return Codec.Encode(payload, isTestNetwork ? Test : Main);
        }

        public static XAddressInfo Decode(string xAddress)
        {
            if (xAddress == null || xAddress.Length != 47 || (xAddress[0] != 'X' && xAddress[0] != 'T'))
                throw new ArgumentException("A valid X-address is required.", nameof(xAddress));
            bool test = xAddress[0] == 'T';
            byte[] payload = Codec.Decode(xAddress, test ? Test : Main);
            byte flag = payload[20];
            if (flag > 1) throw new ArgumentException("Unsupported X-address tag flag.", nameof(xAddress));
            for (int i = flag == 0 ? 21 : 25; i < payload.Length; i++)
                if (payload[i] != 0) throw new ArgumentException("X-address reserved bytes must be zero.", nameof(xAddress));
            uint? tag = flag == 0 ? (uint?)null :
                (uint)payload[21] | ((uint)payload[22] << 8) | ((uint)payload[23] << 16) | ((uint)payload[24] << 24);
            var account = new byte[20];
            Buffer.BlockCopy(payload, 0, account, 0, account.Length);
            return new XAddressInfo { ClassicAddress = Codec.Encode(account, Classic), DestinationTag = tag, IsTestNetwork = test };
        }

        public static bool IsValid(string xAddress)
        {
            try { Decode(xAddress); return true; }
            catch (ArgumentException) { return false; }
        }
    }
}
