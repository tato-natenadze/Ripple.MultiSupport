using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Globalization;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class Amount : ISerializedType
    {
        public readonly AccountId Issuer;

        public readonly Currency Currency;

        public AmountValue Value;

        public const int MaximumIouPrecision = 16;

        public bool IsNative => Currency.IsNative;

        public Amount(AmountValue value, Currency currency = null, AccountId issuer = null)
        {
            Currency = currency ?? Currency.Xrp;
            Issuer = issuer ?? (Currency.IsNative ? AccountId.Zero : AccountId.Neutral);
            Value = value;
        }

        public Amount(string v = "0", Currency c = null, AccountId i = null)
            : this(AmountValue.FromString(v, c?.IsNative ?? true), c, i)
        {
        }

        public Amount(decimal value, Currency currency, AccountId issuer = null)
            : this(value.ToString(CultureInfo.InvariantCulture), currency, issuer)
        {
        }

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Value.ToBytes());
            if (!IsNative)
            {
                Currency.ToBytes(sink);
                Issuer.ToBytes(sink);
            }
        }

        public JToken ToJson()
        {
            if (IsNative)
            {
                return Value.ToString();
            }

            return new JObject
            {
                ["value"] = Value.ToString(),
                ["currency"] = Currency,
                ["issuer"] = Issuer
            };
        }

        public static Amount FromJson(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return (ulong)token;
                case JTokenType.String:
                    return new Amount(token.ToString());
                case JTokenType.Object:
                    {
                        JToken jToken = token["value"];
                        JToken jToken2 = token["currency"];
                        JToken jToken3 = token["issuer"];
                        if (jToken == null)
                        {
                            throw new ArgumentException("Amount object must contain property `value`.");
                        }

                        if (jToken2 == null)
                        {
                            throw new ArgumentException("Amount object must contain property `currency`.");
                        }

                        if (jToken3 == null)
                        {
                            throw new ArgumentException("Amount object must contain property `issuer`.");
                        }

                        if (token.Children().Count() > 3)
                        {
                            throw new ArgumentException("Amount object has too many properties.");
                        }

                        if (jToken.Type != JTokenType.String)
                        {
                            throw new ArgumentException("Property `value` must be string.");
                        }

                        if (jToken2.Type != JTokenType.String)
                        {
                            throw new ArgumentException("Property `currency` must be string.");
                        }

                        if (jToken3.Type != JTokenType.String)
                        {
                            throw new ArgumentException("Property `issuer` must be string.");
                        }

                        return new Amount((string)jToken, (string)jToken2, (string)jToken3);
                    }
                default:
                    throw new ArgumentException("Can not create Amount from `{token}`");
            }
        }

        public static implicit operator Amount(ulong a)
        {
            return new Amount(a.ToString("D"));
        }

        public static implicit operator Amount(string v)
        {
            return new Amount(v);
        }

        public static Amount FromParser(BinaryParser parser, int? hint = null)
        {
            AmountValue amountValue = AmountValue.FromParser(parser);
            if (!amountValue.IsIou)
            {
                return new Amount(amountValue);
            }

            Currency currency = Currency.FromParser(parser);
            AccountId issuer = AccountId.FromParser(parser);
            return new Amount(amountValue, currency, issuer);
        }

        public decimal DecimalValue()
        {
            return decimal.Parse(Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public static Amount operator *(Amount a, decimal b)
        {
            return new Amount((a.DecimalValue() * b).ToString(CultureInfo.InvariantCulture), a.Currency, a.Issuer);
        }

        public static bool operator <(decimal a, Amount b)
        {
            return a < b.DecimalValue();
        }

        public static bool operator >(decimal a, Amount b)
        {
            return a > b.DecimalValue();
        }

        public Amount NewValue(decimal @decimal)
        {
            return new Amount(@decimal, Currency, Issuer);
        }
    }
}
