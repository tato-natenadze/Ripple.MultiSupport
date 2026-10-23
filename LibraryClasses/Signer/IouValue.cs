using System;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Ripple.LibraryClasses.Signer
{
    public class IouValue : AmountValue
    {
        public readonly ulong Mantissa;

        public readonly bool IsNegative;

        public readonly int Exponent;

        public readonly int Precision;

        public const int MinExponent = -96;

        public const int MaxExponent = 80;

        public const int MaxPrecision = 16;

        public const string ValueRegex = "\\A(?=[-+]?(?:[0-9]|\\.[0-9]))([-+])?([0-9]+)?(\\.([0-9]+))?([eE]([+-]?[0-9]+))?\\z";

        public static readonly ulong MinMantissa = Ul("1000,0000,0000,0000");

        public static readonly ulong MaxMantissa = Ul("9999,9999,9999,9999");

        public const string IllegalOfferMantissaString = "1000000000000000100";

        public static readonly IouValue IllegalOffer = new IouValue(ulong.Parse("1000000000000000100"), 0, isNegative: false, 17, normalise: false);

        public bool IsZero => Mantissa == 0;

        public override bool IsIou => true;

        public IouValue(ulong mantissa, int exponent, bool isNegative, int? precision = null, bool normalise = true)
        {
            Mantissa = mantissa;
            Exponent = exponent;
            if (normalise)
            {
                Normalize(ref Mantissa, ref Exponent);
            }

            IsNegative = isNegative;
            Precision = precision ?? (IsZero ? 1 : Mantissa.ToString().Trim('0').Length);
        }

        public IouValue(byte[] mantissa, int sign, int exponent = 0)
            : this(ParseMantissa(mantissa), exponent, sign == -1)
        {
        }

        public static IouValue FromString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            Match match = Regex.Match(value, ValueRegex);
            if (!match.Success)
            {
                throw new ArgumentException("Value must contain a valid decimal number.", nameof(value));
            }

            Group group = match.Groups[1];
            Group group2 = match.Groups[2];
            Group group3 = match.Groups[4];
            Group group4 = match.Groups[6];
            int num = 0;
            string text = group2.Success ? group2.Value : "";
            if (group3.Success)
            {
                string value2 = group3.Value;
                text += value2;
                num -= value2.Length;
            }

            if (group4.Success)
            {
                num = checked(num + int.Parse(group4.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }

            text = text.TrimStart('0');
            string text2 = text.TrimEnd('0');
            if (text2.Length == 0)
            {
                text2 = "0";
                text = "0";
            }

            num = checked(num + text.Length - text2.Length);
            int length = text2.Length;
            bool isNegative = group.Success && group.Value == "-";
            if (length > 16)
            {
                throw new ArgumentException("IOU amounts support at most 16 significant digits.", nameof(value));
            }
            ulong mantissa = ulong.Parse(text2, CultureInfo.InvariantCulture);
            return new IouValue(mantissa, num, isNegative, length);
        }

        public override string ToString()
        {
            if (Mantissa == 0L)
            {
                return "0";
            }

            int i = 16 - Precision + Exponent;
            string text = Mantissa.ToString().Substring(0, Precision);
            while (i > 0)
            {
                text += 0;
                i--;
            }

            int num = 0;
            for (; i <= 0; i++)
            {
                num++;
            }

            while (num > text.Length)
            {
                text = "0" + text;
            }

            if (num != 0)
            {
                int num2 = text.Length + 1 - num;
                if (num2 != text.Length)
                {
                    text = text.Insert(num2, ".");
                }
            }

            if (IsNegative)
            {
                text = "-" + text;
            }

            return text;
        }

        private static void Normalize(ref ulong mantissa, ref int exponent)
        {
            if (mantissa != 0L)
            {
                while (mantissa < MinMantissa)
                {
                    mantissa *= 10uL;
                    exponent = checked(exponent - 1);
                }

                while (mantissa > MaxMantissa)
                {
                    mantissa /= 10uL;
                    exponent = checked(exponent + 1);
                }

                if (exponent > 80 || exponent < -96)
                {
                    throw new ArgumentException();
                }
            }
        }

        private static ulong Ul(string s)
        {
            return ulong.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
        }

        public override byte[] ToBytes()
        {
            bool flag = !IsNegative;
            byte[] array = MantissaBytes();
            array[0] |= 128;
            if (IsZero)
            {
                return array;
            }

            if (flag)
            {
                array[0] |= 64;
            }

            int exponent = Exponent;
            int num = 97 + exponent;
            array[0] |= (byte)(num >> 2);
            array[1] |= (byte)((num & 3) << 6);
            return array;
        }

        public byte[] MantissaBytes()
        {
            return Bits.GetBytes(Mantissa);
        }
    }
}
