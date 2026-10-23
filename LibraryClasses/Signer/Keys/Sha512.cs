using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Security.Cryptography;

namespace Ripple.LibraryClasses.Signer.Keys
{
    public class Sha512 : IBytesSink
    {
        private readonly SHA512 _digest;

        public Sha512()
        {
            _digest = SHA512.Create();
            _digest.Initialize();
        }

        public Sha512(byte[] start)
            : this()
        {
            Add(start);
        }

        public Sha512(uint prefix)
            : this()
        {
            AddU32(prefix);
        }

        public Sha512 Add(byte[] bytes)
        {
            _digest.TransformBlock(bytes, 0, bytes.Length, null, 0);
            return this;
        }

        public Sha512 Add(byte one)
        {
            return Add(new byte[1] { one });
        }

        public Sha512 AddU32(uint i)
        {
            Add(new byte[4]
            {
            (byte)(i >> 24),
            (byte)(i >> 16),
            (byte)(i >> 8),
            (byte)i
            });
            return this;
        }

        private byte[] FinishTaking(int size)
        {
            byte[] sourceArray = Finish();
            byte[] array = new byte[size];
            Array.Copy(sourceArray, 0, array, 0, size);
            return array;
        }

        public byte[] Finish()
        {
            _digest.TransformFinalBlock(new byte[0], 0, 0);
            byte[] hash = _digest.Hash;
            _digest.Dispose();
            return hash;
        }

        public byte[] Finish128()
        {
            return FinishTaking(16);
        }

        public byte[] Finish256()
        {
            return FinishTaking(32);
        }

        public static byte[] Half(byte[] input, uint? prefix = null)
        {
            Sha512 sha = new Sha512();
            if (prefix.HasValue)
            {
                sha.AddU32(prefix.Value);
            }

            sha.Add(input);
            return sha.Finish256();
        }

        public static byte[] Quarter(byte[] input)
        {
            return new Sha512(input).Finish128();
        }

        public void Put(byte aByte)
        {
            Add(aByte);
        }

        public void Put(byte[] bytes)
        {
            Add(bytes);
        }
    }
}