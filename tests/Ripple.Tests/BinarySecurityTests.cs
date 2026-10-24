using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class BinarySecurityTests
    {
        // rippled permits STObject depth 0..10; an array and its item each add a level.
        // https://github.com/XRPLF/rippled/blob/develop/src/libxrpl/protocol/STObject.cpp
        [DataTestMethod]
        [DataRow(false, 10)]
        [DataRow(true, 5)]
        public void ProtocolDepthBoundaryKeepsExactBinaryBytes(bool arrays, int levels)
        {
            string hex = Nested(arrays, levels);
            Assert.AreEqual(hex, StObject.FromHex(hex).ToHex());
        }

        [DataTestMethod]
        [DataRow(false, 11)]
        [DataRow(true, 6)]
        public void ExcessiveBinaryDepthIsRejected(bool arrays, int levels)
        {
            var parser = new BufferParser(Nested(arrays, levels));
            Assert.ThrowsException<FormatException>(() => StObject.FromParser(parser));
            parser.Skip(Nested(arrays, levels).Length / 2 - parser.Pos());
            Assert.IsNotNull(StObject.FromParser(parser), "The parser depth must unwind after a failed nested read.");
        }

        [TestMethod]
        public void DuplicateBinaryFieldsCannotSilentlyReplaceEarlierValues()
        {
            Assert.ThrowsException<FormatException>(() => StObject.FromHex("24000000012400000002"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void VeryDeepUntrustedReceiptsFailBeforeRecursiveResourceExhaustion(bool arrays)
        {
            string hex = Nested(arrays, 4096);
            Assert.ThrowsException<FormatException>(() => StObject.FromHex(hex));
            var error = Assert.ThrowsException<ArgumentException>(() => new PaymentReceipt(new string('A', 64),
                TestTransactions.Account, 1, 101, 104, hex, RpcHandler.Options().RpcEndpoint));
            Assert.AreEqual("txBlob", error.ParamName);
        }

        private static string Nested(bool arrays, int levels)
        {
            return string.Concat(Enumerable.Repeat(arrays ? "F9EA" : "EA", levels)) +
                string.Concat(Enumerable.Repeat(arrays ? "E1F1" : "E1", levels));
        }
    }
}
