using System;

using System.Globalization;

namespace Ripple.LibraryClasses.Signer
{
    public class NativeValue : AmountValue
    {
        public const ulong MaximumDrops = 100000000000000000UL;

        public bool IsNegative;

        public ulong Mantissa;

        public override bool IsIou => false;

        public NativeValue(string value)
        {
            long num = long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (num < -(long)MaximumDrops || num > (long)MaximumDrops)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "XRP amount exceeds the maximum supply in drops.");
            }
            IsNegative = num < 0;
            Mantissa = (ulong)Math.Abs(num);
        }

        public NativeValue(byte[] mantissa, int sign)
        {
            Mantissa = ParseMantissa(mantissa);
            ValidateMantissa();
            IsNegative = sign == -1 && Mantissa != 0;
        }

        public override string ToString()
        {
            string text = Mantissa.ToString(CultureInfo.InvariantCulture);
            if (IsNegative)
            {
                return "-" + text;
            }

            return text;
        }

        public override byte[] ToBytes()
        {
            ValidateMantissa();
            bool flag = !IsNegative || Mantissa == 0;
            byte[] bytes = Bits.GetBytes(Mantissa);
            bytes[0] |= (byte)(flag ? 64 : 0);
            return bytes;
        }

        private void ValidateMantissa()
        {
            if (Mantissa > MaximumDrops)
            {
                throw new ArgumentOutOfRangeException(nameof(Mantissa), "XRP amount exceeds the maximum supply in drops.");
            }
        }
    }
}
