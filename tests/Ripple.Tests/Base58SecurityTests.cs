using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.CreateAddress;
using Ripple.LibraryClasses.Signer;
using Ripple.LibraryClasses.ValidateAddress;

namespace Ripple.Tests
{
    [TestClass]
    public class Base58SecurityTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void OversizedSeedsAreRejectedBeforeQuadraticDecoding()
        {
            string input = "s" + new string('p', 29999);
            var timer = Stopwatch.StartNew();
            Assert.ThrowsException<ArgumentException>(() => Seed.FromBase58(input));
            timer.Stop();
            Assert.IsTrue(timer.Elapsed < TimeSpan.FromMilliseconds(500), "Impossible seed length must be rejected before Base58 conversion.");
        }

        [TestMethod]
        public void OversizedSingleVersionInputIsRejectedPromptly()
        {
            var codec = new B58(AddressCodec.Alphabet);
            string input = new string('p', 30000);
            var timer = Stopwatch.StartNew();
            Assert.ThrowsException<ArgumentException>(() => codec.Decode(input, B58.Version.With(28, 33)));
            timer.Stop();
            Assert.IsTrue(timer.Elapsed < TimeSpan.FromMilliseconds(500));
        }

        [TestMethod]
        public void CheckedAddressAndSeedVersionsKeepAllPayloadBoundaries()
        {
            var codec = new B58(AddressCodec.Alphabet);
            foreach (var version in new[] { B58.Version.With(0, 20), B58.Version.With(33, 16),
                B58.Version.With(new byte[] { 1, 225, 75 }, 16), B58.Version.With(28, 33),
                B58.Version.With(new byte[] { 0, 0, 0 }, 256) })
            {
                for (int value = 0; value <= 255; value++)
                {
                    byte[] payload = Enumerable.Repeat((byte)value, version.ExpectedLength).ToArray();
                    string encoded = codec.Encode(payload, version);
                    CollectionAssert.AreEqual(payload, codec.Decode(encoded, version));
                    CollectionAssert.AreEqual(payload, codec.Decode(encoded, B58.Versions.With("custom", version)).Payload);
                }
            }
        }
    }
}
