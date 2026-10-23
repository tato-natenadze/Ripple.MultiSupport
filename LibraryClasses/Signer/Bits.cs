using System;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public static class Bits
    {
        public static bool IsLittleEndian { get; set; }

        public static long DoubleToInt64Bits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }

        public static byte[] GetBytes(bool value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(byte value)
        {
            return new byte[1] { value };
        }

        public static byte[] GetBytes(char value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(double value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(float value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(int value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(long value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(short value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(uint value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(ulong value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static byte[] GetBytes(ushort value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.GetBytes(value);
            }

            return BitConverter.GetBytes(value).Reverse().ToArray();
        }

        public static double Int64BitsToDouble(long value)
        {
            return BitConverter.Int64BitsToDouble(value);
        }

        public static bool ToBoolean(byte[] value, int startIndex)
        {
            return BitConverter.ToBoolean(value, startIndex);
        }

        public static char ToChar(byte[] value, int startIndex)
        {
            return (char)ToUInt16(value, startIndex);
        }

        public static double ToDouble(byte[] value, int startIndex)
        {
            return BitConverter.Int64BitsToDouble(ToInt64(value, startIndex));
        }

        public static short ToInt16(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToInt16(value, startIndex);
            }

            return BitConverter.ToInt16(value.Reverse().ToArray(), value.Length - 2 - startIndex);
        }

        public static int ToInt32(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToInt32(value, startIndex);
            }

            return BitConverter.ToInt32(value.Reverse().ToArray(), value.Length - 4 - startIndex);
        }

        public static long ToInt64(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToInt64(value, startIndex);
            }

            return BitConverter.ToInt64(value.Reverse().ToArray(), value.Length - 8 - startIndex);
        }

        public static float ToSingle(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToSingle(value, startIndex);
            }

            return BitConverter.ToSingle(value.Reverse().ToArray(), value.Length - 4 - startIndex);
        }

        public static string ToString(byte[] value)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToString(value);
            }

            return BitConverter.ToString(value.Reverse().ToArray());
        }

        public static string ToString(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToString(value, startIndex);
            }

            return BitConverter.ToString(value.Reverse().ToArray(), startIndex);
        }

        public static string ToString(byte[] value, int startIndex, int length)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToString(value, startIndex, length);
            }

            return BitConverter.ToString(value.Reverse().ToArray(), startIndex, length);
        }

        public static ushort ToUInt16(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToUInt16(value, startIndex);
            }

            return BitConverter.ToUInt16(value.Reverse().ToArray(), value.Length - 2 - startIndex);
        }

        public static uint ToUInt32(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToUInt32(value, startIndex);
            }

            return BitConverter.ToUInt32(value.Reverse().ToArray(), value.Length - 4 - startIndex);
        }

        public static ulong ToUInt64(byte[] value, int startIndex)
        {
            if (IsLittleEndian)
            {
                return BitConverter.ToUInt64(value, startIndex);
            }

            return BitConverter.ToUInt64(value.Reverse().ToArray(), value.Length - 8 - startIndex);
        }
    }
}
