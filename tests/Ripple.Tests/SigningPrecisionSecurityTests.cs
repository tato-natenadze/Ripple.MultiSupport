using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Ripple.Tests
{
    [TestClass]
    public class SigningPrecisionSecurityTests
    {
        [DataTestMethod]
        [DataRow("1e-100")]
        [DataRow("-1e-100")]
        [DataRow("5e-324")]
        [DataRow("-5e-324")]
        public void SubnormalAndUnderflowingTagsCannotBeSignedAsZero(string json)
        {
            var tx = TestTransactions.Payment(); tx["DestinationTag"] = JToken.Parse(json);
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void SmallestDecimalFractionCannotBeSignedAsZero()
        {
            var tx = TestTransactions.Payment(); tx["DestinationTag"] = 0.0000000000000000000000000001m;
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }
    }
}
