using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class SecurityTests
    {
        [TestMethod]
        [Timeout(30000)]
        public async Task SharedSecp256k1KeyProducesTheSameSignaturesUnderConcurrency()
        {
            const int workers = 8, repetitions = 32;
            var key = Seed.FromBase58(TestTransactions.SecpSeed).KeyPair();
            var messages = Enumerable.Range(0, workers).Select(i => new byte[] { 1, 2, 3, (byte)i }).ToArray();
            var expected = messages.Select(message => B16.Encode(key.Sign(message))).ToArray();
            using (var ready = new CountdownEvent(workers))
            using (var start = new ManualResetEventSlim())
            {
                var tasks = Enumerable.Range(0, workers).Select(worker => Task.Factory.StartNew(() => {
                    ready.Signal();
                    if (!start.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Signing workers did not start.");
                    return Enumerable.Range(0, repetitions).Select(_ => B16.Encode(key.Sign(messages[worker]))).ToArray();
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                bool allReady = ready.Wait(TimeSpan.FromSeconds(10));
                start.Set();
                var signatures = await Task.WhenAll(tasks);
                Assert.IsTrue(allReady, "Signing workers were not scheduled together.");
                for (int worker = 0; worker < workers; worker++)
                    foreach (string signature in signatures[worker])
                        Assert.AreEqual(expected[worker], signature, "Concurrent signing changed the deterministic signature.");
            }
        }

    }
}
