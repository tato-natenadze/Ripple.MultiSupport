using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple.Tests
{
    [TestClass]
    public class AddressValidationTests
    {
        [TestMethod]
        public void OversizedBase58InputIsRejectedPromptly()
        {
            Assert.IsTrue(RippleClient.ValidateAddress(TestTransactions.Account));
            string oversized = "r" + new string('p', 29999);
            var timer = Stopwatch.StartNew(); bool valid = RippleClient.ValidateAddress(oversized); timer.Stop();
            Assert.IsFalse(valid);
            Assert.IsTrue(timer.Elapsed < TimeSpan.FromMilliseconds(500), "Impossible address length must be rejected before quadratic Base58 decoding.");
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(24)]
        [DataRow(25)]
        [DataRow(35)]
        [DataRow(36)]
        [DataRow(100000)]
        public void InvalidLengthsAndChecksumsReturnFalse(int length)
        {
            Assert.IsFalse(RippleClient.ValidateAddress(new string('r', length)));
        }

        [TestMethod]
        public void ClassicAddressVectorsIncludingSpecialAndBoundaryPayloadsRemainValid()
        {
            Assert.IsTrue(RippleClient.ValidateAddress(TestTransactions.Account));
            Assert.IsTrue(RippleClient.ValidateAddress(TestTransactions.Destination));
            for (int value = 0; value < 256; value++)
            {
                var bytes = new byte[20]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)value;
                string address = AddressCodec.EncodeAddress(bytes);
                Assert.IsTrue(RippleClient.ValidateAddress(address), address);
            }
            var one = new byte[20]; one[19] = 1;
            Assert.IsTrue(RippleClient.ValidateAddress(AddressCodec.EncodeAddress(one)));
        }

        [DataTestMethod]
        [DataRow("ქ")]
        [DataRow("\ud800")]
        [DataRow("0")]
        [DataRow("O")]
        public void InvalidAlphabetAndUnicodeInputsReturnFalse(string value)
        {
            Assert.IsFalse(RippleClient.ValidateAddress("r" + value + new string('p', 30)));
        }

        [TestMethod]
        public void VeryLargeUnicodeAndSurrogateInputsReturnFalse()
        {
            Assert.IsFalse(RippleClient.ValidateAddress("r" + new string('ქ', 100000)));
            Assert.IsFalse(RippleClient.ValidateAddress("r" + new string('\ud800', 100000)));
        }

        [TestMethod]
        public async Task InvalidAddressNeverReachesTheTransport()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                string invalid = "r" + new string('p', 29999);
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetAccountAsync(invalid));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = invalid, AmountXrp = 1 }));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }
    }
}
