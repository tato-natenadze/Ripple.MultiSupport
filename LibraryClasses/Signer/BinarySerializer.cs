using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public class BinarySerializer : IBytesSink
    {
        private readonly IBytesSink _sink;

        public BinarySerializer(IBytesSink sink)
        {
            _sink = sink;
        }

        public void Put(byte[] n)
        {
            _sink.Put(n);
        }

        public void AddLengthEncoded(byte[] n)
        {
            Put(EncodeVl(n.Length));
            Put(n);
        }

        public static byte[] EncodeVl(int length)
        {
            byte[] array = new byte[4];
            if (length <= 192)
            {
                array[0] = (byte)length;
                return TakeSome(array, 1);
            }

            if (length <= 12480)
            {
                length -= 193;
                array[0] = (byte)(193uL + (ulong)(length >> 8));
                array[1] = (byte)((uint)length & 0xFFu);
                return TakeSome(array, 2);
            }

            if (length <= 918745)
            {
                length -= 12481;
                array[0] = (byte)(241uL + (ulong)(length >> 16));
                array[1] = (byte)((uint)(length >> 8) & 0xFFu);
                array[2] = (byte)((uint)length & 0xFFu);
                return TakeSome(array, 3);
            }

            throw new InvalidOperationException($"length must <= 918745, was {length}");
        }

        private static byte[] TakeSome(byte[] buffer, int n)
        {
            byte[] array = new byte[n];
            Array.Copy(buffer, 0, array, 0, n);
            return array;
        }

        public void Add(BytesList bl)
        {
            foreach (byte[] item in bl.RawList())
            {
                _sink.Put(item);
            }
        }

        public int AddFieldHeader(Field f)
        {
            if (!f.IsSerialised)
            {
                throw new InvalidOperationException($"Field {f} is a discardable field");
            }

            byte[] header = f.Header;
            Put(header);
            return header.Length;
        }

        public void Put(byte type)
        {
            _sink.Put(type);
        }

        public void AddLengthEncoded(BytesList bytes)
        {
            Put(EncodeVl(bytes.BytesLength()));
            Add(bytes);
        }

        public void Add(Field field, ISerializedType value)
        {
            AddFieldHeader(field);
            if (field.IsVlEncoded)
            {
                AddLengthEncoded(value);
                return;
            }

            value.ToBytes(_sink);
            if (field.Type == FieldType.StObject)
            {
                AddFieldHeader(Field.ObjectEndMarker);
            }
            else if (field.Type == FieldType.StArray)
            {
                AddFieldHeader(Field.ArrayEndMarker);
            }
        }

        public void AddLengthEncoded(ISerializedType value)
        {
            BytesList bytesList = new BytesList();
            value.ToBytes(bytesList);
            AddLengthEncoded(bytesList);
        }
    }
}
