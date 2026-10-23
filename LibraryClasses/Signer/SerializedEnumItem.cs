using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace Ripple.LibraryClasses.Signer
{
    public abstract class SerializedEnumItem<TOrd> : EnumItem, ISerializedType where TOrd : struct, IConvertible
    {
        protected readonly byte[] Bytes;

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Bytes);
        }

        public JToken ToJson()
        {
            return ToString();
        }

        protected SerializedEnumItem(string name, int ordinal)
            : base(name, ordinal)
        {
            switch (Marshal.SizeOf(default(TOrd)))
            {
                case 1:
                    Bytes = new byte[1] { (byte)ordinal };
                    break;
                case 2:
                    Bytes = Bits.GetBytes((ushort)ordinal);
                    break;
            }
        }
    }
}
