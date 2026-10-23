using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System.Text;

namespace Ripple.LibraryClasses.Signer
{
    public class Blob : ISerializedType
    {
        public readonly byte[] Buffer;

        private Blob(byte[] decode)
        {
            Buffer = decode;
        }

        public static Blob FromHex(string value)
        {
            return B16.Decode(value);
        }

        public static implicit operator Blob(byte[] value)
        {
            return new Blob(value);
        }

        public static implicit operator Blob(JToken token)
        {
            return FromJson(token);
        }

        public static Blob FromJson(JToken token)
        {
            return FromHex(token.ToString());
        }

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Buffer);
        }

        public JToken ToJson()
        {
            return ToString();
        }

        public override string ToString()
        {
            return B16.Encode(Buffer);
        }

        public static Blob FromParser(BinaryParser parser, int? hint = null)
        {
            return parser.Read(hint.Value);
        }

        public static Blob FromString(string blob, Encoding encoding)
        {
            return new Blob(encoding.GetBytes(blob));
        }

        public static Blob FromAscii(string blob)
        {
            return FromString(blob, Encoding.ASCII);
        }
    }
}
