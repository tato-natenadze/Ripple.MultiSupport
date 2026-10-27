using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ripple.LibraryClasses.Signer;

namespace Ripple.Tests
{
    [TestClass]
    public class BinaryTests
    {
        [TestMethod]
        public void EnumOrdinalsReadAllBigEndianBytes()
        {
            Assert.AreEqual(256, TransactionType.Values.ReadOrdinal(new BufferParser(new byte[] { 1, 0 })));
            Assert.AreEqual(65535, TransactionType.Values.ReadOrdinal(new BufferParser(new byte[] { 255, 255 })));
        }

        [TestMethod]
        public void UnknownTransactionTypeCannotBeMisreadAsPayment()
        {
            Assert.ThrowsException<System.Collections.Generic.KeyNotFoundException>(() => StObject.FromHex("120100"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void PreviouslyUnimplementedConversionsWorkInBothByteOrders(bool littleEndian)
        {
            bool original = Bits.IsLittleEndian;
            try
            {
                Bits.IsLittleEndian = littleEndian;
                // Fixed IEEE 754 binary64 patterns, independent of Bits encoding/decoding.
                foreach (string bigEndianHex in new[] {
                    "0000000000000000", "8000000000000000", // positive/negative zero
                    "3FF8000000000000", "C0C81CD6E631F8A1", // 1.5 and -12345.6789
                    "7FF0000000000000", "FFF0000000000000", // positive/negative infinity
                    "7FF8000000000000", "FFF8000000000000", // explicit quiet NaN payloads
                    "0000000000000001", "7FEFFFFFFFFFFFFF"  // smallest subnormal and largest finite
                })
                {
                    long expectedBits = unchecked((long)Convert.ToUInt64(bigEndianHex, 16));
                    double value = BitConverter.Int64BitsToDouble(expectedBits);
                    var expectedBytes = new byte[8];
                    for (int i = 0; i < expectedBytes.Length; i++)
                        expectedBytes[i] = Convert.ToByte(bigEndianHex.Substring(i * 2, 2), 16);
                    if (littleEndian) Array.Reverse(expectedBytes);
                    Assert.AreEqual(expectedBits, Bits.DoubleToInt64Bits(value));
                    Assert.AreEqual(expectedBits, BitConverter.DoubleToInt64Bits(Bits.Int64BitsToDouble(expectedBits)));
                    CollectionAssert.AreEqual(expectedBytes, Bits.GetBytes(value), bigEndianHex);
                    byte[] data = new byte[10]; Array.Copy(expectedBytes, 0, data, 1, 8);
                    Assert.AreEqual(expectedBits, BitConverter.DoubleToInt64Bits(Bits.ToDouble(data, 1)));
                }
                byte[] expectedCharacter = littleEndian ? new byte[] { 0xE5, 0x10 } : new byte[] { 0x10, 0xE5 };
                CollectionAssert.AreEqual(expectedCharacter, Bits.GetBytes('ქ'));
                byte[] character = new byte[4]; Array.Copy(expectedCharacter, 0, character, 1, 2);
                Assert.AreEqual('ქ', Bits.ToChar(character, 1));
                Assert.IsTrue(Bits.ToBoolean(new byte[] { 0, 1, 0 }, 1));
                Assert.IsFalse(Bits.ToBoolean(new byte[] { 0, 1, 0 }, 2));
            }
            finally { Bits.IsLittleEndian = original; }
        }
    }
}
