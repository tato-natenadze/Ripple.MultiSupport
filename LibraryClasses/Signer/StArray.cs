using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class StArray : List<StObject>, ISerializedType
    {
        public StArray(IEnumerable<StObject> collection)
            : base(collection)
        {
        }

        public StArray()
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
                    StObject current = enumerator.Current;
                    jArray.Add(current.ToJson());
                }
            }

            return jArray;
        }

        public static StArray FromJson(JToken token)
        {
            return new StArray(token.Select(StObject.FromJson));
        }

        public static StArray FromParser(BinaryParser parser, int? hint = null)
        {
            StArray stArray = new StArray();
            while (!parser.End())
            {
                Field field = parser.ReadField();
                if (field == Field.ArrayEndMarker)
                {
                    break;
                }

                StObject item = new StObject { [(StObjectField)field] = StObject.FromParser(parser) };
                stArray.Add(item);
            }

            return stArray;
        }
    }
}
