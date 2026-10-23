using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;

namespace Ripple.Tests
{
    [TestClass]
    public class AmountTests
    {
        [DataTestMethod]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("fr-FR")]
        [DataRow("ka-GE")]
        public void ArithmeticUsesInvariantDecimalValues(string culture)
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var amount = new Amount("1.5", Currency.FromString("USD"));
                Assert.AreEqual(1.5m, amount.DecimalValue());
                Assert.AreEqual("3", (amount * 2m).Value.ToString());
                Assert.IsTrue(1m < amount);
                Assert.IsTrue(2m > amount);
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("1")]
        [DataRow("100000000000000000")]
        [DataRow("-1")]
        [DataRow("-100000000000000000")]
        public void NativeValuesWithinRangeRoundTrip(string value)
        {
            var amount = new NativeValue(value);
            Assert.AreEqual(value, AmountValue.FromParser(new BufferParser(amount.ToBytes())).ToString());
        }

        [DataTestMethod]
        [DataRow("100000000000000001")]
        [DataRow("-100000000000000001")]
        [DataRow("4611686018427387905")]
        [DataRow("-9223372036854775808")]
        public void OutOfRangeNativeValuesAreRejected(string value)
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new NativeValue(value));
            var tx = TestTransactions.Payment(); tx["Amount"] = value;
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void MutableNativeMantissaCannotBypassTheRangeCheck()
        {
            var amount = new NativeValue("1") { Mantissa = ulong.MaxValue };
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => amount.ToBytes());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("+")]
        [DataRow("-")]
        [DataRow(".")]
        [DataRow("e10")]
        [DataRow("+e10")]
        [DataRow("1,5")]
        [DataRow("1\n")]
        [DataRow("12345678901234567")]
        public void MalformedIouValuesAreRejectedThroughThePublicSigner(string value)
        {
            Assert.ThrowsException<ArgumentException>(() => IouValue.FromString(value));
            var tx = TestTransactions.Payment(); tx["Amount"] = TestTransactions.Iou(value);
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("1")]
        [DataRow("0.1")]
        [DataRow(".5")]
        [DataRow("+1.5")]
        [DataRow("-1.5")]
        [DataRow("1e-81")]
        [DataRow("9999999999999999e80")]
        [DataRow("1234567890.123456")]
        public void ValidIouValuesKeepTheirWireRepresentation(string value)
        {
            var tx = TestTransactions.Payment(); tx["Amount"] = TestTransactions.Iou(value);
            string blob = RippleClient.SignTransaction(tx);
            StObject parsed = StObject.FromHex(blob);
            Assert.AreEqual(blob, parsed.ToHex());
            Assert.AreEqual(IouValue.FromString(value).ToString(), parsed[Field.Amount].Value.ToString());
        }
    }
}
