using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Globalization;

namespace Ripple.LibraryClasses.Signer.Uints
{
    public abstract class Uint<T> : ISerializedType where T : struct, IConvertible
    {
        public readonly T Value;

        protected Uint(T value)
        {
            Value = value;
        }

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(ToBytes());
        }

        public virtual JToken ToJson()
        {
            return Convert.ToUInt32(Value);
        }

        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture);
        }

        public abstract byte[] ToBytes();
    }
}
