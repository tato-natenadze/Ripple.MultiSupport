using System;
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
        [Timeout(10000)]
        public void OversizedSeedsAreRejectedBeforeQuadraticDecoding()
        {
            // Keep the abuse case large; use the test deadline instead of a microbenchmark.
            string input = "s" + new string('p', 999999);
            Assert.ThrowsException<ArgumentException>(() => Seed.FromBase58(input));
        }

        [TestMethod]
        [Timeout(10000)]
        public void OversizedSingleVersionInputIsRejectedPromptly()
        {
            var codec = new B58(AddressCodec.Alphabet);
            string input = new string('p', 1000000);
            Assert.ThrowsException<ArgumentException>(() => codec.Decode(input, B58.Version.With(28, 33)));
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
