using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Hashes;
using Ripple.LibraryClasses.Signer.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class Vector256 : List<Hash256>, ISerializedType
    {
        private Vector256(IEnumerable<Hash256> enumerable)
            : base(enumerable)
        {
        }

        public Vector256()
        {
        }

        public void ToBytes(IBytesSink sink)
        {
            using (Enumerator enumerator = GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    enumerator.Current.ToBytes(sink);
                }
            }
        }

        public JToken ToJson()
        {
            JArray jArray = new JArray();
            using (Enumerator enumerator = GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    Hash256 current = enumerator.Current;
                    jArray.Add(current.ToJson());
                }
            }

            return jArray;
        }

        public static Vector256 FromJson(JToken token)
        {
            return new Vector256(token.Select(Hash256.FromJson));
        }

        public static Vector256 FromParser(BinaryParser parser, int? hint = null)
        {
            Vector256 vector = new Vector256();
            if (!hint.HasValue)
            {
                hint = parser.Size - parser.Pos();
            }

            for (int i = 0; i < hint / 32; i++)
            {
                vector.Add(Hash256.FromParser(parser));
            }

            return vector;
        }
    }
}
