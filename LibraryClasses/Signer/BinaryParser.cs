using Ripple.LibraryClasses.Signer.Fields;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public abstract class BinaryParser
    {
        protected internal int Size;

        protected internal int Cursor;

        public bool End()
        {
            return Cursor >= Size;
        }

        public int Pos()
        {
            return Cursor;
        }

        public int ReadOneInt()
        {
            return ReadOne() & 0xFF;
        }

        public abstract void Skip(int n);

        public abstract byte ReadOne();

        public abstract byte[] Read(int n);

        public Field ReadField()
        {
            int name = ReadFieldCode();
            return Field.Values[name] ?? throw new InvalidOperationException("Couldn't parse field from " + name.ToString("x"));
        }

        public int ReadFieldCode()
        {
            byte num = ReadOne();
            int num2 = (num & 0xFF) >> 4;
            if (num2 == 0)
            {
                num2 = ReadOne();
            }

            int num3 = num & 0xF;
            if (num3 == 0)
            {
                num3 = ReadOne();
            }

            return num2 << 16 | num3;
        }

        public int ReadVlLength()
        {
            int num = ReadOneInt();
            if (num <= 192)
            {
                return num;
            }

            if (num <= 240)
            {
                int num2 = ReadOneInt();
                return 193 + (num - 193) * 256 + num2;
            }

            if (num <= 254)
            {
                int num3 = ReadOneInt();
                int num4 = ReadOneInt();
                return 12481 + (num - 241) * 65536 + num3 * 256 + num4;
            }

            throw new InvalidOperationException("Invalid variable length indicator");
        }

        public bool End(int? customEnd)
        {
            if (Cursor < Size)
            {
                if (customEnd.HasValue)
                {
                    return Cursor >= customEnd;
                }

                return false;
            }

            return true;
        }
    }
}
