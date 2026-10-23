using Newtonsoft.Json.Linq;

using System;

namespace Ripple.LibraryClasses.Signer.Hashes
{
    public class Hash160 : Hash
    {
        public Hash160(byte[] buffer)
            : base(buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length != 20) throw new ArgumentException("Hash160 must contain exactly 20 bytes.", nameof(buffer));
        }

        public static Hash160 FromJson(JToken token)
        {
            return new Hash160(B16.Decode(token.ToString()));
        }

        public static Hash160 FromParser(BinaryParser parser, int? hint = null)
        {
            return new Hash160(parser.Read(20));
        }
    }
}
