using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple.Tests
{
    [TestClass]
    public class AccountGateSecurityTests
    {
        [DataTestMethod]
        [DataRow("unfunded")]
        [DataRow("transport")]
        [DataRow("cancelled")]
        [DataRow("success")]
        public void CompletedQuotesDoNotRetainEveryCallerSuppliedAccount(string outcome)
        {
            var handler = new RpcHandler { Respond = (method, args, token) => {
                if (outcome == "transport") throw new HttpRequestException("Connection unavailable.");
                if (method == "server_info") return Task.FromResult(RpcHandler.Ledger());
                if (method == "account_info")
                {
                    if (outcome == "unfunded") return RpcHandler.Json("{'error':'actNotFound'}");
                    return Task.FromResult(RpcHandler.Account((string)args["account"]));
                }
                if (method == "fee") return RpcHandler.Json("{'drops':{'base_fee':'10','open_ledger_fee':'10'}}");
                throw new InvalidOperationException(method);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            {
                var accounts = Enumerable.Range(1, 64).Select(i => Quote(client, i, outcome)).ToArray();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Assert.IsTrue(accounts.Count(account => account.IsAlive) <= 1,
                    "Completed quote calls must not indefinitely retain every distinct account in a shared client.");
                GC.KeepAlive(client);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference Quote(XrplClient client, int index, string outcome)
        {
            var bytes = new byte[20]; bytes[0] = (byte)index;
            string account = AddressCodec.EncodeAddress(bytes);
            var reference = new WeakReference(account);
            using (var cancellation = new CancellationTokenSource())
            {
                if (outcome == "cancelled") cancellation.Cancel();
                try
                {
                    client.GetPaymentQuoteAsync(account, new XrpPaymentRequest {
                        Destination = TestTransactions.Destination, AmountXrp = 1 }, cancellation.Token).GetAwaiter().GetResult();
                    Assert.AreEqual("success", outcome);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is HttpRequestException || ex is OperationCanceledException)
                { Assert.AreNotEqual("success", outcome); }
            }
            return reference;
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task CancellingAWaiterCannotUnlockAnotherQuoteForTheSameSender()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var server = new PaymentTests.Server(); int reads = 0;
            var handler = new RpcHandler { Respond = async (method, args, token) => {
                if (method == "server_info" && Interlocked.Increment(ref reads) == 1)
                { entered.SetResult(true); await TestAwait.UntilAsync(release.Task).ConfigureAwait(false); }
                return await server.Respond(method, args, token).ConfigureAwait(false);
            } };
            using (var http = new HttpClient(handler))
            using (var client = new XrplClient(RpcHandler.Options(), http))
            using (var waiting = new CancellationTokenSource())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var payment = new XrpPaymentRequest { Destination = TestTransactions.Destination, AmountXrp = 1 };
                var first = client.GetPaymentQuoteAsync(TestTransactions.Account, payment, deadline.Token);
                await TestAwait.UntilAsync(entered.Task);
                var second = client.GetPaymentQuoteAsync(TestTransactions.Account, payment, waiting.Token);
                waiting.Cancel();
                try { await second; Assert.Fail("The waiting quote was not cancelled."); } catch (OperationCanceledException) { }
                var third = client.GetPaymentQuoteAsync(TestTransactions.Account, payment, deadline.Token);
                Assert.AreEqual(1, reads, "A cancelled waiter must not remove the active sender's gate.");
                release.SetResult(true);
                await Task.WhenAll(first, third);
                Assert.AreEqual(2, reads);
                Assert.IsFalse(handler.Requests.Any(r => (string)r["method"] == "submit"));
            }
        }
    }
}
