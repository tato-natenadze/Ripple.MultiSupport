using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ripple.LibraryClasses.Signer
{
    public class BytesList : IBytesSink
    {
        private readonly List<byte[]> _buffer = new List<byte[]>();

        private int _len;

        public static string[] HexLookup;

        public void Add(BytesList bl)
        {
            foreach (byte[] item in bl.RawList())
            {
                Put(item);
            }
        }

        public void Put(byte aByte)
        {
            Put(new byte[1] { aByte });
        }

        public void Put(byte[] bytes)
        {
            _len += bytes.Length;
            _buffer.Add(bytes);
        }

        public byte[] Bytes()
        {
            byte[] array = new byte[BytesLength()];
            AddBytes(array, 0);
            return array;
        }

        static BytesList()
        {
            HexLookup = new string[256];
            for (int i = 0; i < 256; i++)
            {
                string text = i.ToString("x").ToUpper();
                if (text.Length == 1)
                {
                    text = "0" + text;
                }

                HexLookup[i] = text;
            }
        }

        public string BytesHex()
        {
            StringBuilder stringBuilder = new StringBuilder(_len * 2);
            foreach (byte item in _buffer.SelectMany((buf) => buf))
            {
                stringBuilder.Append(HexLookup[item & 0xFF]);
            }

            return stringBuilder.ToString();
        }

        public int BytesLength()
        {
            return _len;
        }

        private int AddBytes(byte[] bytes, int destPos)
        {
            foreach (byte[] item in _buffer)
            {
                Array.Copy(item, 0, bytes, destPos, item.Length);
                destPos += item.Length;
            }

            return destPos;
        }

        public List<byte[]> RawList()
        {
            return _buffer;
        }
    }
}
