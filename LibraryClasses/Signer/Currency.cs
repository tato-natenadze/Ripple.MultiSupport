using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Hashes;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public class Currency : Hash160
    {
        public readonly string IsoCode;

        public readonly bool IsNative;

        public static readonly Currency Xrp = new Currency(new byte[20]);

        public Currency(byte[] buffer)
            : base(buffer)
        {
            IsoCode = GetCurrencyCodeFromTlcBytes(buffer, out IsNative);
        }

        public static string GetCurrencyCodeFromTlcBytes(byte[] bytes, out bool isNative)
        {
            bool flag = true;
            bool flag2 = true;
            for (int i = 0; i < 20; i++)
            {
                flag2 = flag2 && bytes[i] == 0;
                flag = flag && (i == 12 || i == 13 || i == 14 || bytes[i] == 0);
            }

            if (flag2)
            {
                isNative = true;
                return "XRP";
            }

            if (flag)
            {
                isNative = false;
                return IsoCodeFromBytesAndOffset(bytes, 12);
            }

            isNative = false;
            return null;
        }

        private static char CharFrom(byte[] bytes, int i)
        {
            return (char)bytes[i];
        }

        private static string IsoCodeFromBytesAndOffset(byte[] bytes, int offset)
        {
            char c = CharFrom(bytes, offset);
            char c2 = CharFrom(bytes, offset + 1);
            char c3 = CharFrom(bytes, offset + 2);
            return c.ToString() + c2 + c3;
        }

        public new static Currency FromJson(JToken token)
        {
            if (token != null)
            {
                return FromString(token.ToString());
            }

            return null;
        }

        public static Currency FromString(string str)
        {
            if (str == "XRP")
            {
                return Xrp;
            }

            if (str.Length == 40)
            {
                return new Currency(B16.Decode(str));
            }
            else if (str.Length == 3)
            {
                return new Currency(EncodeCurrency(str));
            }
            else
            {
                throw new InvalidOperationException("Currency must either be a 3 letter iso code or a 20 byte hash encoded in hexadecimal");
            }
        }

        public static byte[] EncodeCurrency(string currencyCode)
        {
            return new byte[20]
            {
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            (byte)char.ConvertToUtf32(currencyCode, 0),
            (byte)char.ConvertToUtf32(currencyCode, 1),
            (byte)char.ConvertToUtf32(currencyCode, 2),
            0,
            0,
            0,
            0,
            0
            };
        }

        public static implicit operator Currency(string v)
        {
            return FromString(v);
        }

        public static implicit operator Currency(JToken v)
        {
            return FromJson(v);
        }

        public static implicit operator JToken(Currency v)
        {
            return v.ToString();
        }

        public override string ToString()
        {
            if (IsoCode != null)
            {
                return IsoCode;
            }

            return base.ToString();
        }

        public new static Currency FromParser(BinaryParser parser, int? hint = null)
        {
            return new Currency(parser.Read(20));
        }

        public static UnissuedAmount operator /(decimal v, Currency c)
        {
            if (c == Xrp)
            {
                v *= 1000000m;
            }
            return new UnissuedAmount(v, c);
        }

        public static Issue operator /(Currency c, AccountId ac)
        {
            return new Issue();
        }

        public class Issue
        {
        }
    }
}
