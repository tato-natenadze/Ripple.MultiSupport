using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ripple.LibraryClasses.Signer
{
    public abstract class SerializedEnumeration<TEnum, TOrd> : Enumeration<TEnum> where TEnum : SerializedEnumItem<TOrd> where TOrd : struct, IConvertible
    {
        public int Width { get; }

        protected SerializedEnumeration()
        {
            Width = Marshal.SizeOf(default(TOrd));
        }

        public TEnum FromParser(BinaryParser parser, int? hint = null)
        {
            return base[ReadOrdinal(parser)];
        }

        public TEnum FromJson(JToken value)
        {
            if (value.Type != JTokenType.String)
            {
                return base[(int)value];
            }

            return base[value.ToString()];
        }

        public int ReadOrdinal(BinaryParser parser)
        {
            return parser.Read(Width).Aggregate(0, (a, b) => (a << 8) | b);
        }
    }

}
