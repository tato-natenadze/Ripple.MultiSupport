using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    internal static class TestTransactions
    {
        // Public XRPL test vectors. Never use these keys for funded accounts.
        internal const string SecpSeed = "sp5fghtJtpUorTwvof1NpDXAzNwf5";
        internal const string EdSeed = "sEdSKaCy2JT7JaM7v95H9SxkhP9wS2r";
        internal const string Account = "rU6K7V3Po4snVhBBaU29sesqs2qTQJWDw1";
        internal const string Destination = "rLUEXYuLiQptky37CqLcm9USQpPiz5rkpD";

        internal static JObject Create(string type = "Payment")
        {
            return new JObject
            {
                ["TransactionType"] = type,
                ["Account"] = Account,
                ["Fee"] = "12",
                ["Sequence"] = 1,
                ["Seed"] = SecpSeed
            };
        }

        internal static JObject Payment()
        {
            JObject tx = Create();
            tx["Destination"] = Destination;
            tx["Amount"] = "1000000";
            return tx;
        }

        internal static JObject Iou(string value)
        {
            return new JObject
            {
                ["value"] = value,
                ["currency"] = "USD",
                ["issuer"] = Account
            };
        }
    }
}
