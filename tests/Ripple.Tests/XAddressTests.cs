using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple.Tests
{
    [TestClass]
    public class XAddressTests
    {
        private const string OfficialClassic = "rGWrZyQqhTp9Xu7G5Pkayo7bXjH4k4QYpf";

        // Published XLS-5 vectors: https://xls.xrpl.org/xls/XLS-0005-tagged-addresses.html
        [DataTestMethod]
        [DataRow(-1L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtV5fdx1mHp98tDMoQXb")]
        [DataRow(0L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtV8AqEL4xcZj5whKbmc")]
        [DataRow(1L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtV8xvjGQTYPiAx6gwDC")]
        [DataRow(276L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtVoKj3MnFGMXEFMnvJV")]
        [DataRow(65591L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtVozpjdhPQVdt3ghaWw")]
        [DataRow(16781933L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtVqrDUk2vDpkTjPsY73")]
        [DataRow(4294967295L, false, "XVLhHMPHU98es4dbozjVtdWzVrDjtV18pX8yuPT7y4xaEHi")]
        [DataRow(-1L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQn49b3qD26PK7FcGSKE")]
        [DataRow(0L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQnSy8RHqGHoGJ59spi2")]
        [DataRow(1L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQnSz1uDimDdPYXzSpyw")]
        [DataRow(276L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQnTMgJJYfAbsiPsc6Zg")]
        [DataRow(65591L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQn7ryu2W6njw7mT1jmS")]
        [DataRow(16781933L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQnVsw45sDtGHhLi27Qa")]
        [DataRow(4294967295L, true, "TVE26TYGhfLC7tQDno7G8dGtxSkYQnXoy6kSDh6rZzApc69")]
        public void CodecMatchesOfficialVectors(long tagValue, bool test, string expected)
        {
            uint? tag = tagValue < 0 ? (uint?)null : (uint)tagValue;
            Assert.AreEqual(expected, XAddressCodec.Encode(OfficialClassic, tag, test));
            var decoded = XAddressCodec.Decode(expected);
            Assert.AreEqual(OfficialClassic, decoded.ClassicAddress);
            Assert.AreEqual(tag, decoded.DestinationTag);
            Assert.AreEqual(test, decoded.IsTestNetwork);
            Assert.IsTrue(XAddressCodec.IsValid(expected));
            Assert.IsFalse(RippleClient.ValidateAddress(expected), "The existing classic-address validator must retain its meaning.");
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("rGWrZyQqhTp9Xu7G5Pkayo7bXjH4k4QYpf")]
        [DataRow("XVLhHMPHU98es4dbozjVtdWzVrDjtV18pX8yuPT7y4xaEHa")]
        [DataRow("XVLhHMPHU98es4dbozjVtdWzVrDjtV18pX8yuPT7y4xaEH0")]
        public void InvalidInputsFailValidation(string text)
        {
            Assert.IsFalse(XAddressCodec.IsValid(text));
            Assert.ThrowsException<ArgumentException>(() => XAddressCodec.Decode(text));
        }

        [DataTestMethod]
        [DataRow(0, 21)]
        [DataRow(0, 28)]
        [DataRow(1, 25)]
        [DataRow(1, 28)]
        [DataRow(2, -1)]
        [DataRow(255, -1)]
        public void ReservedBitsAreRejectedEvenWithAValidChecksum(int flag, int nonzeroByte)
        {
            var bytes = new byte[29];
            bytes[20] = (byte)flag;
            if (nonzeroByte >= 0) bytes[nonzeroByte] = 1;
            var codec = new B58(AddressCodec.Alphabet);
            string text = codec.Encode(bytes, B58.Version.With(new byte[] { 0x05, 0x44 }, 29));
            Assert.IsFalse(XAddressCodec.IsValid(text));
            Assert.ThrowsException<ArgumentException>(() => XAddressCodec.Decode(text));
        }

        [DataTestMethod]
        [DataRow(0, false)]
        [DataRow(1, true)]
        [DataRow(2, true)]
        public async Task QuoteAndSendExpandTheDestinationAndZeroTag(int networkId, bool test)
        {
            var server = new PaymentTests.Server { TargetFlags = 0x00020000 };
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                var result = await server.Respond(method, args, token);
                if (method == "server_info") result["info"]["network_id"] = networkId;
                return result;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = XAddressCodec.Encode(TestTransactions.Destination, 0, test), AmountXrp = 1m };
                var quote = await client.GetPaymentQuoteAsync(TestTransactions.Account, request);
                Assert.AreEqual(TestTransactions.Destination, quote.Destination);
                Assert.AreEqual(0u, quote.DestinationTag);
                Assert.IsFalse(server.Submitted);
                var sent = await client.SendXrpAsync(TestTransactions.SecpSeed, request);
                Assert.AreEqual(TransactionStatus.Succeeded, sent.Status);
                Assert.AreEqual(TestTransactions.Destination, (string)server.Signed.Single()["Destination"]);
                Assert.AreEqual(0u, (uint)server.Signed.Single()["DestinationTag"]);
                Assert.IsNull(request.DestinationTag, "The caller's request must not be mutated.");
                Assert.IsTrue(request.Destination[0] == (test ? 'T' : 'X'));
            }
        }

        [DataTestMethod]
        [DataRow(0, true)]
        [DataRow(1, false)]
        public async Task MismatchedNetworksAreRejectedBeforeSigningOrSubmission(int networkId, bool test)
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                var result = await server.Respond(method, args, token);
                if (method == "server_info") result["info"]["network_id"] = networkId;
                return result;
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = XAddressCodec.Encode(TestTransactions.Destination, 123, test), AmountXrp = 1m };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, request));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
                Assert.IsFalse(server.Submitted);
                Assert.IsTrue(handler.Requests.All(r => (string)r["method"] == "server_info"));
            }
        }

        [TestMethod]
        public async Task AnUnknownNetworkCannotBeAssumedForAnXAddress()
        {
            var server = new PaymentTests.Server();
            var handler = new RpcHandler { Respond = server.Respond };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                    new XrpPaymentRequest { Destination = XAddressCodec.Encode(TestTransactions.Destination, null, true), AmountXrp = 1m }));
            Assert.IsFalse(server.Submitted);
        }

        [DataTestMethod]
        [DataRow(-1L, 0u)]
        [DataRow(0L, 1u)]
        [DataRow(1L, 0u)]
        public async Task ConflictingTagsAreRejectedBeforeNetworkAccess(long embeddedTag, uint suppliedTag)
        {
            uint? tag = embeddedTag < 0 ? (uint?)null : (uint)embeddedTag;
            var handler = new RpcHandler { Respond = (method, args, token) => throw new Exception("Unexpected I/O") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var request = new XrpPaymentRequest { Destination = XAddressCodec.Encode(TestTransactions.Destination, tag, true),
                    DestinationTag = suppliedTag, AmountXrp = 1m };
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed, request));
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => client.GetPaymentQuoteAsync(TestTransactions.Account, request));
            }
            Assert.AreEqual(0, handler.Requests.Count);
        }
    }
}
