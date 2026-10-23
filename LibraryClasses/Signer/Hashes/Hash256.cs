using Newtonsoft.Json.Linq;

using System;

namespace Ripple.LibraryClasses.Signer.Hashes
{
    public class Hash256 : Hash
    {
        public static readonly Hash256 Zero = new Hash256(new byte[32]);

        public Hash256(byte[] buffer)
            : base(buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length != 32) throw new ArgumentException("Hash256 must contain exactly 32 bytes.", nameof(buffer));
        }

        public static Hash256 FromJson(JToken token)
        {
            return FromHex((string)token);
        }

        public static Hash256 FromHex(string token)
        {
            return new Hash256(B16.Decode(token));
        }

        public static Hash256 FromParser(BinaryParser parser, int? hint = null)
        {
            return new Hash256(parser.Read(32));
        }

        public int Nibblet(int depth)
        {
            int num = depth > 0 ? depth / 2 : 0;
            int num2 = Buffer[num];
            if (depth % 2 == 0)
            {
                return (num2 & 0xF0) >> 4;
            }

            return num2 & 0xF;
        }
    }
}
