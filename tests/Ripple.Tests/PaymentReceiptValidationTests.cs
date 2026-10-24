using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class PaymentReceiptValidationTests
    {
        [DataTestMethod]
        [DataRow("73FF")]
        [DataRow("71FF")]
        [DataRow("74FF")]
        [DataRow("73FF00")]
        [DataRow("73FF0000")]
        [DataRow("00")]
        [DataRow("FF")]
        [DataRow("F1")]
        [DataRow("GG")]
        [DataRow("ABC")]
        public void InvalidBinaryReceiptsConsistentlyReportTheBlobArgument(string blob)
        {
            var error = Assert.ThrowsException<ArgumentException>(() => new PaymentReceipt(new string('A', 64),
                TestTransactions.Account, 1, 101, 104, blob, RpcHandler.Options().RpcEndpoint));
            Assert.AreEqual("txBlob", error.ParamName);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ValidReceiptRoundTripsPreserveEveryField(bool ed)
        {
            var receipt = PaymentRecoveryTests.Receipt(104, ed: ed);
            string json = JsonConvert.SerializeObject(receipt);
            var restored = JsonConvert.DeserializeObject<PaymentReceipt>(json);
            Assert.AreEqual(receipt.Hash, restored.Hash); Assert.AreEqual(receipt.Sender, restored.Sender);
            Assert.AreEqual(receipt.Sequence, restored.Sequence); Assert.AreEqual(receipt.FirstLedger, restored.FirstLedger);
            Assert.AreEqual(receipt.LastLedger, restored.LastLedger); Assert.AreEqual(receipt.TxBlob, restored.TxBlob);
            Assert.AreEqual(receipt.RpcEndpoint, restored.RpcEndpoint);
            Assert.IsFalse(json.Contains(TestTransactions.SecpSeed)); Assert.IsFalse(json.Contains(TestTransactions.EdSeed));
        }

        [TestMethod]
        public void PublicLowLevelParserKeepsItsExistingExceptionBehavior()
        {
            Assert.ThrowsException<InvalidOperationException>(() => StObject.FromHex("73FF"));
        }
    }
}
