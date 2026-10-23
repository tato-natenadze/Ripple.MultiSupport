namespace Ripple.LibraryClasses.Signer.Hashes
{
    public static class HashPrefixExtensions
    {
        public static byte[] Bytes(this HashPrefix prefix)
        {
            uint v = (uint)prefix;
            // Always return big-endian 4 bytes (matches XRPL library behavior)
            return new[]
            {
                (byte)(v >> 24 & 0xFF),
                (byte)(v >> 16 & 0xFF),
                (byte)(v >> 8 & 0xFF),
                (byte)(v & 0xFF),
            };
        }
    }
}
