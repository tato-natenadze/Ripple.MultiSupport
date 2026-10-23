using Newtonsoft.Json.Linq;

using System;

namespace Ripple.LibraryClasses.Signer.Hashes
{
    public class Hash128 : Hash
    {
        public Hash128(byte[] buffer)
            : base(buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length != 16) throw new ArgumentException("Hash128 must contain exactly 16 bytes.", nameof(buffer));
        }

        public static Hash128 FromJson(JToken token)
        {
            return new Hash128(B16.Decode(token.ToString()));
        }

        public static Hash128 FromParser(BinaryParser parser, int? hint = null)
        {
            return new Hash128(parser.Read(16));
        }
    }
}
