using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class Path : List<PathHop>
    {
        public Path()
        {
        }

        public Path(IEnumerable<PathHop> enumerable)
            : base(enumerable)
        {
        }

        public static Path FromJson(JToken json)
        {
            return new Path(json.Select(PathHop.FromJson));
        }

        public JArray ToJson()
        {
            JArray jArray = new JArray();
            using (Enumerator enumerator = GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    PathHop current = enumerator.Current;
                    jArray.Add(current.ToJson());
                }
            }

            return jArray;
        }
    }
}
