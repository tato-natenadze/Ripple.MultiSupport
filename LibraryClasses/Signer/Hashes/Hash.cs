using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Linq;

namespace Ripple.LibraryClasses.Signer.Hashes
{
    public abstract class Hash : ISerializedType, IEquatable<Hash>
    {
        public readonly byte[] Buffer;

        protected Hash(byte[] buffer)
        {
            Buffer = buffer;
        }

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Buffer);
        }

        public JToken ToJson()
        {
            return ToString();
        }

        public bool Equals(Hash other)
        {
            return other.Buffer.SequenceEqual(Buffer);
        }

        public override string ToString()
        {
            return B16.Encode(Buffer);
        }

        public static explicit operator string(Hash h)
        {
            return h.ToHex();
        }
    }
}
