using System;

namespace Ripple.LibraryClasses.Signer
{
    public class BufferParser : BinaryParser
    {
        protected internal byte[] Bytes;

        public BufferParser(byte[] bytes)
        {
            Size = bytes.Length;
            Bytes = bytes;
        }

        public BufferParser(string hex)
            : this(B16.Decode(hex))
        {
        }

        public override void Skip(int n)
        {
            Cursor += n;
        }

        public override byte ReadOne()
        {
            return Bytes[Cursor++];
        }

        public override byte[] Read(int n)
        {
            byte[] array = new byte[n];
            Array.Copy(Bytes, Cursor, array, 0, n);
            Cursor += n;
            return array;
        }
    }
}
