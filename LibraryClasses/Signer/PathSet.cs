using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class PathSet : List<Path>, ISerializedType
    {
        public static byte PathSeparatorByte = byte.MaxValue;

        public static byte PathsetEndByte = 0;

        private PathSet()
        {
        }

        public PathSet(IEnumerable<Path> collection)
            : base(collection)
        {
        }

        public void ToBytes(IBytesSink buffer)
        {
            int num = 0;
            using (Enumerator enumerator = GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    Path current = enumerator.Current;
                    if (num++ != 0)
                    {
                        buffer.Put(PathSeparatorByte);
                    }

                    foreach (PathHop item in current)
                    {
                        buffer.Put((byte)item.Type);
                        if (item.HasAccount())
                        {
                            buffer.Put(item.Account.Buffer);
                        }

                        if (item.HasCurrency())
                        {
                            buffer.Put(item.Currency.Buffer);
                        }

                        if (item.HasIssuer())
                        {
                            buffer.Put(item.Issuer.Buffer);
                        }
                    }
                }
            }

            buffer.Put(PathsetEndByte);
        }

        public JToken ToJson()
        {
            JArray jArray = new JArray();
            using (Enumerator enumerator = GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    Path current = enumerator.Current;
                    jArray.Add(current.ToJson());
                }
            }

            return jArray;
        }

        public static PathSet FromJson(JToken token)
        {
            return new PathSet(token.Select(Path.FromJson));
        }

        public static PathSet FromParser(BinaryParser parser, int? hint = null)
        {
            PathSet pathSet = new PathSet();
            Path path = null;
            while (!parser.End())
            {
                byte b = parser.ReadOne();
                if (b == PathsetEndByte)
                {
                    break;
                }

                if (path == null)
                {
                    path = new Path();
                    pathSet.Add(path);
                }

                if (b == PathSeparatorByte)
                {
                    path = null;
                    continue;
                }

                AccountId account = null;
                AccountId issuer = null;
                Currency currency = null;
                if ((b & PathHop.TypeAccount) != 0)
                {
                    account = AccountId.FromParser(parser);
                }

                if ((b & PathHop.TypeCurrency) != 0)
                {
                    currency = Currency.FromParser(parser);
                }

                if ((b & PathHop.TypeIssuer) != 0)
                {
                    issuer = AccountId.FromParser(parser);
                }

                PathHop item = new PathHop(account, issuer, currency);
                path.Add(item);
            }

            return pathSet;
        }
    }
}
