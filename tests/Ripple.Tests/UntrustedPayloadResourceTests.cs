using System;
using System.Net.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ripple.Tests
{
    [TestClass]
    public class UntrustedPayloadResourceTests
    {
        private static readonly Func<long> AllocatedBytes = CreateAllocationCounter();

        private static Func<long> CreateAllocationCounter()
        {
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        }

        private static long ReadAllocatedBytes()
        {
            if (AllocatedBytes == null)
                Assert.Inconclusive("Thread-allocation metrics require .NET Framework 4.8 or modern .NET. Public rejection tests run separately on every target.");
            return AllocatedBytes();
        }

        [TestMethod]
        public void ATruncatedReceiptIsRejectedWithoutAllocationMetrics()
        {
            RejectReceipt("73FEFFFF");
        }

        [TestMethod]
        public void AnOversizedMemoIsRejectedWithoutNetworkAccessOrAllocationMetrics()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected network I/O.") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                RejectMemo(client, new string('a', 2 * 1024 * 1024));
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }

        [TestMethod]
        public void ATruncatedReceiptCannotAllocateItsClaimedLargeBlob()
        {
            RejectReceipt("73FEFFFF");
            long before = ReadAllocatedBytes();
            for (int i = 0; i < 8; i++) RejectReceipt("73FEFFFF");
            long allocated = ReadAllocatedBytes() - before;
            Assert.IsTrue(allocated < 2 * 1024 * 1024,
                "A four-byte malformed receipt must not allocate a large buffer for every claimed blob length. Allocated: " + allocated);
        }

        [TestMethod]
        public void AnOversizedMemoIsRejectedBeforeAllocatingItsEntireUtf8Buffer()
        {
            var handler = new RpcHandler { Respond = (m, p, ct) => throw new Exception("Unexpected network I/O.") };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                string memo = new string('a', 2 * 1024 * 1024);
                RejectMemo(client, memo);
                long before = ReadAllocatedBytes();
                for (int i = 0; i < 8; i++) RejectMemo(client, memo);
                long allocated = ReadAllocatedBytes() - before;
                Assert.IsTrue(allocated < 2 * 1024 * 1024,
                    "An invalid memo must not be fully encoded before its existing size limit is checked. Allocated: " + allocated);
                Assert.AreEqual(0, handler.Requests.Count);
            }
        }

        [TestMethod]
        public void MutatedAndTruncatedReceiptsConsistentlyReturnArgumentErrors()
        {
            var receipt = PaymentTests.Receipt();
            string blob = receipt.TxBlob;
            for (int i = 0; i < blob.Length; i += 2)
            {
                RejectReceipt(blob.Substring(0, i));
                RejectReceipt(blob.Substring(0, i) + (blob[i] == '0' ? "F" : "0") + blob.Substring(i + 1));
            }
            var random = new Random(5817);
            for (int i = 0; i < 512; i++)
            {
                var bytes = new byte[random.Next(1, 129)]; random.NextBytes(bytes);
                RejectReceipt(BitConverter.ToString(bytes).Replace("-", ""));
            }
        }

        private static void RejectMemo(XrplClient client, string memo)
        {
            Assert.ThrowsException<ArgumentException>(() => client.SendXrpAsync(TestTransactions.SecpSeed,
                new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1, Memo = memo }).GetAwaiter().GetResult());
        }

        private static void RejectReceipt(string blob)
        {
            Assert.ThrowsException<ArgumentException>(() => new PaymentReceipt(new string('A', 64), TestTransactions.Account,
                1, 101, 104, blob, RpcHandler.Options().RpcEndpoint));
        }
    }
}
