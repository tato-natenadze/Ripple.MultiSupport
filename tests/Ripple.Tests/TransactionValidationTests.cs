using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Hashes;

namespace Ripple.Tests
{
    [TestClass]
    public class TransactionValidationTests
    {
        [DataTestMethod]
        [DataRow((string)null)]
        [DataRow("")]
        [DataRow("abc")]
        [DataRow("აბგ")]
        [DataRow("rU6K7V3Po4snVhBBaU29sesqs2qTQJWDw2")]
        public void InvalidAddressesReturnFalse(string value) => Assert.IsFalse(RippleClient.ValidateAddress(value));

        [DataTestMethod]
        [DataRow("NFTokenBuyOffer")]
        [DataRow("NFTokenSellOffer")]
        public void DirectNftOfferAcceptanceCanBeSigned(string offerField)
        {
            var tx = TestTransactions.Create("NFTokenAcceptOffer"); tx[offerField] = new string('A', 64);
            var parsed = StObject.FromHex(RippleClient.SignTransaction(tx));
            Assert.AreEqual(new string('A', 64), parsed[offerField == "NFTokenBuyOffer" ? Field.NFTokenBuyOffer : Field.NFTokenSellOffer].ToJson().ToString());
        }

        [DataTestMethod]
        [DataRow("NFTokenBrokerFee")]
        [DataRow("BrokerFee")]
        public void BrokeredAcceptanceUsesTheCanonicalWireFieldAndKeepsTheLegacyName(string feeField)
        {
            var tx = TestTransactions.Create("NFTokenAcceptOffer");
            tx["NFTokenBuyOffer"] = new string('A', 64); tx["NFTokenSellOffer"] = new string('B', 64); tx[feeField] = "100";
            string blob = RippleClient.SignTransaction(tx);
            var parsed = StObject.FromHex(blob);
            Assert.AreEqual("100", parsed[Field.NFTokenBrokerFee].ToJson().ToString());
            Assert.AreEqual(blob, parsed.ToHex());
        }

        [TestMethod]
        public void NftAcceptanceWithoutAnOfferIsRejected()
        {
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(TestTransactions.Create("NFTokenAcceptOffer")));
        }

        [TestMethod]
        public void BrokerFeeRequiresBothOffers()
        {
            var tx = TestTransactions.Create("NFTokenAcceptOffer"); tx["NFTokenSellOffer"] = new string('A', 64); tx["NFTokenBrokerFee"] = "1";
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void LegacyBrokerFeeMetadataAndDecoderNameRemainAvailable()
        {
            Assert.AreEqual("BrokerFee", Field.BrokerFee.Name);
            Assert.AreSame(Field.BrokerFee, Field.Values[Field.BrokerFee.Ordinal]);
            var original = StObject.FromJson(new Newtonsoft.Json.Linq.JObject { ["BrokerFee"] = "100" });
            var decoded = StObject.FromHex(original.ToHex()).ToJson();
            Assert.AreEqual("100", (string)decoded["BrokerFee"]);
        }

        [TestMethod]
        public void ConflictingBrokerFeeAliasesAreRejected()
        {
            var tx = TestTransactions.Create("NFTokenAcceptOffer");
            tx["NFTokenBuyOffer"] = new string('A', 64); tx["NFTokenSellOffer"] = new string('B', 64);
            tx["BrokerFee"] = "1"; tx["NFTokenBrokerFee"] = "2";
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void TicketCountIsRequired()
        {
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(TestTransactions.Create("TicketCreate")));
        }

        [DataTestMethod]
        [DataRow(1)]
        [DataRow(250)]
        public void ValidTicketCreationCanBeSigned(int count)
        {
            var tx = TestTransactions.Create("TicketCreate"); tx["TicketCount"] = count;
            var parsed = StObject.FromHex(RippleClient.SignTransaction(tx));
            Assert.AreEqual((uint)count, (uint)parsed[Field.TicketCount]);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(251)]
        public void InvalidTicketCountsAreRejected(int count)
        {
            var tx = TestTransactions.Create("TicketCreate"); tx["TicketCount"] = count;
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void PaymentCanUseATicketWithZeroSequence()
        {
            var tx = TestTransactions.Payment(); tx["Sequence"] = 0; tx["TicketSequence"] = 123;
            var parsed = StObject.FromHex(RippleClient.SignTransaction(tx));
            Assert.AreEqual(0u, (uint)parsed[Field.Sequence]); Assert.AreEqual(123u, (uint)parsed[Field.TicketSequence]);
        }

        [TestMethod]
        public void PaymentCannotUseBothANonzeroSequenceAndATicket()
        {
            var tx = TestTransactions.Payment(); tx["TicketSequence"] = 123;
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("AA")]
        [DataRow("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void WrongLengthInvoiceIdsAreRejectedBeforeSigning(string value)
        {
            var tx = TestTransactions.Payment(); tx["InvoiceID"] = value;
            Assert.ThrowsException<ArgumentException>(() => RippleClient.SignTransaction(tx));
        }

        [TestMethod]
        public void AllFixedWidthHashesValidateTheirLengths()
        {
            Assert.ThrowsException<ArgumentException>(() => new Hash128(new byte[15]));
            Assert.ThrowsException<ArgumentException>(() => new Hash160(new byte[21]));
            Assert.ThrowsException<ArgumentException>(() => new Hash256(new byte[31]));
            Assert.ThrowsException<ArgumentNullException>(() => new Hash256(null));
        }
    }
}
