using Newtonsoft.Json.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class PathHop
    {
        public static byte TypeAccount = 1;

        public static byte TypeCurrency = 16;

        public static byte TypeIssuer = 32;

        public readonly AccountId Account;

        public readonly AccountId Issuer;

        public readonly Currency Currency;

        public readonly int Type;

        public PathHop(AccountId account, AccountId issuer, Currency currency)
        {
            Account = account;
            Issuer = issuer;
            Currency = currency;
            Type = SynthesizeType();
        }

        public static PathHop FromJson(JToken json)
        {
            return new PathHop(json["account"], json["issuer"], json["currency"]);
        }

        public bool HasIssuer()
        {
            return Issuer != null;
        }

        public bool HasCurrency()
        {
            return Currency != null;
        }

        public bool HasAccount()
        {
            return Account != null;
        }

        public int SynthesizeType()
        {
            int num = 0;
            if (HasAccount())
            {
                num |= TypeAccount;
            }

            if (HasCurrency())
            {
                num |= TypeCurrency;
            }

            if (HasIssuer())
            {
                num |= TypeIssuer;
            }

            return num;
        }

        public JObject ToJson()
        {
            JObject jObject = new JObject { ["type"] = Type };
            if (HasAccount())
            {
                jObject["account"] = Account;
            }

            if (HasCurrency())
            {
                jObject["currency"] = Currency;
            }

            if (HasIssuer())
            {
                jObject["issuer"] = Issuer;
            }

            return jObject;
        }
    }
}
