using System;

namespace Ripple.LibraryClasses.Signer
{
    public abstract class AmountValue
    {
        public abstract bool IsIou { get; }

        public new abstract string ToString();

        public abstract byte[] ToBytes();

        public static AmountValue FromString(string value, bool native = false)
        {
            if (native)
            {
                return new NativeValue(value);
            }

            return IouValue.FromString(value);
        }

        public static AmountValue FromParser(BinaryParser parser)
        {
            byte[] array = parser.Read(8);
            byte b = array[0];
            byte b2 = array[1];
            bool flag = (b & 0x80) != 0;
            int sign = (b & 0x40u) != 0 ? 1 : -1;
            if (flag)
            {
                array[0] = 0;
                int exponent = ((b & 0x3F) << 2) + ((b2 & 0xFF) >> 6) - 97;
                array[1] &= 63;
                return new IouValue(array, sign, exponent);
            }

            array[0] &= 63;
            return new NativeValue(array, sign);
        }

        internal static ulong ParseMantissa(byte[] mantissa)
        {
            if (mantissa.Length > 8)
            {
                throw new ArgumentException("Encoded mantissa must be only 8 bytes maximum");
            }

            return Bits.ToUInt64(mantissa, 0);
        }
    }
}
