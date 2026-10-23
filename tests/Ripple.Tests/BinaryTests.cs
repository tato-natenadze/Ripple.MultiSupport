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
                foreach (double value in new[] { 0d, -0d, 1.5d, -12345.6789d, double.PositiveInfinity, double.NaN })
                {
                    long expectedBits = BitConverter.DoubleToInt64Bits(value);
                    Assert.AreEqual(expectedBits, Bits.DoubleToInt64Bits(value));
                    Assert.AreEqual(expectedBits, BitConverter.DoubleToInt64Bits(Bits.Int64BitsToDouble(expectedBits)));
                    byte[] data = new byte[10]; Array.Copy(Bits.GetBytes(value), 0, data, 1, 8);
                    Assert.AreEqual(expectedBits, BitConverter.DoubleToInt64Bits(Bits.ToDouble(data, 1)));
                }
                byte[] character = new byte[4]; Array.Copy(Bits.GetBytes('ქ'), 0, character, 1, 2);
                Assert.AreEqual('ქ', Bits.ToChar(character, 1));
                Assert.IsTrue(Bits.ToBoolean(new byte[] { 0, 1, 0 }, 1));
                Assert.IsFalse(Bits.ToBoolean(new byte[] { 0, 1, 0 }, 2));
            }
            finally { Bits.IsLittleEndian = original; }
        }
    }
}
