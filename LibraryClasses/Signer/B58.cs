using Ripple.LibraryClasses.Signer.Hashes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ripple.LibraryClasses.Signer
{
    public class B58
    {
        public class Decoded
        {
            public readonly byte[] Payload;

            public readonly string Type;

            public readonly byte[] VersionBytes;

            public Decoded(byte[] version, byte[] payload, string typeName)
            {
                VersionBytes = version;
                Payload = payload;
                Type = typeName;
            }
        }

        public class Version
        {
            public int ExpectedLength;

            public byte[] VersionBytes;

            public Version(byte[] versionBytes, int expectedLength)
            {
                VersionBytes = versionBytes;
                ExpectedLength = expectedLength;
            }

            public static Version With(byte versionByte, int expectedLength)
            {
                return With(new byte[1] { versionByte }, expectedLength);
            }

            public static Version With(byte[] versionBytes, int expectedLength)
            {
                return new Version(versionBytes, expectedLength);
            }
        }

        public class Versions
        {
            public string[] NamesArray;

            public Version[] VersionsArray;

            public Versions(Version[] versionsArray, string[] namesArray)
            {
                VersionsArray = versionsArray;
                NamesArray = namesArray;
            }

            public static Versions With(string typeName, Version version)
            {
                return new Versions(new Version[1] { version }, new string[1] { typeName });
            }

            public Versions And(string typeName, Version version)
            {
                return new Versions(new Version[1] { version }.Concat(VersionsArray).ToArray(), new string[1] { typeName }.Concat(NamesArray).ToArray());
            }

            public Version Find(string sought)
            {
                for (int i = 0; i < NamesArray.Length; i++)
                {
                    string text = NamesArray[i];
                    if (sought == text)
                    {
                        return VersionsArray[i];
                    }
                }

                throw new InvalidOperationException("Can't find version with name " + sought);
            }
        }

        private char[] _mAlphabet;

        private int[] _mIndexes;

        public B58(string alphabet)
        {
            SetAlphabet(alphabet);
            BuildIndexes();
        }

        public byte[] Decode(string input, Version version)
        {
            byte[] buffer = DecodeAndCheck(input);
            return ExtractPayload(version, buffer);
        }

        private static byte[] ExtractPayload(Version version, byte[] buffer)
        {
            int expectedLength = version.ExpectedLength;
            int num = version.VersionBytes.Length;
            int num2 = expectedLength + num;
            int num3 = buffer.Length - 4;
            if (num2 == num3)
            {
                byte[] array = CopyOfRange(buffer, 0, num);
                if (ArrayEquals((IReadOnlyCollection<byte>)(object)array, version.VersionBytes))
                {
                    return CopyOfRange(buffer, array.Length, num3);
                }

                throw new ArgumentException("Version invalid");
            }

            throw new ArgumentException($"Expected version + payload length was {num2} " + $"but actual length was {num3}");
        }

        public Decoded Decode(string input, Versions versions)
        {
            byte[] array = DecodeAndCheck(input);
            for (int i = 0; i < versions.VersionsArray.Length; i++)
            {
                string typeName = versions.NamesArray[i];
                Version version = versions.VersionsArray[i];
                int expectedLength = version.ExpectedLength;
                int num = version.VersionBytes.Length;
                int num2 = expectedLength + num;
                int num3 = array.Length - 4;
                if (num2 == num3)
                {
                    byte[] array2 = CopyOfRange(array, 0, num);
                    if (ArrayEquals((IReadOnlyCollection<byte>)(object)array2, version.VersionBytes))
                    {
                        byte[] payload = CopyOfRange(array, array2.Length, num3);
                        return new Decoded(version.VersionBytes, payload, typeName);
                    }
                }
            }

            throw new ArgumentException("No version matched amongst " + string.Join(", ", versions.NamesArray));
        }

        public byte[] Decode(string input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (input.Length == 0)
            {
                return new byte[0];
            }

            byte[] array = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                int num = -1;
                if (c >= '\0' && c < '\u0080')
                {
                    num = _mIndexes[(uint)c];
                }

                if (num < 0)
                {
                    throw new ArgumentException("Illegal character " + c + " at " + i);
                }

                array[i] = (byte)num;
            }

            int j;
            for (j = 0; j < array.Length && array[j] == 0; j++)
            {
            }

            byte[] array2 = new byte[input.Length];
            int k = array2.Length;
            int num2 = j;
            while (num2 < array.Length)
            {
                byte b = DivMod256(array, num2);
                if (array[num2] == 0)
                {
                    num2++;
                }

                array2[--k] = b;
            }

            for (; k < array2.Length && array2[k] == 0; k++)
            {
            }

            return CopyOfRange(array2, k - j, array2.Length);
        }

        public string Encode(byte[] buffer, Version version)
        {
            CheckLength(version, buffer);
            return EncodeToStringChecked(buffer, version.VersionBytes);
        }

        public string Encode(byte[] buffer, string versionName, Versions versions)
        {
            return Encode(buffer, versions.Find(versionName));
        }

        public byte[] EncodeToBytes(byte[] input)
        {
            if (input.Length == 0)
            {
                return new byte[0];
            }

            input = CopyOfRange(input, 0, input.Length);
            int i;
            for (i = 0; i < input.Length && input[i] == 0; i++)
            {
            }

            byte[] array = new byte[input.Length * 2];
            int j = array.Length;
            int num = i;
            while (num < input.Length)
            {
                byte b = DivMod58(input, num);
                if (input[num] == 0)
                {
                    num++;
                }

                array[--j] = (byte)_mAlphabet[b];
            }

            for (; j < array.Length && array[j] == _mAlphabet[0]; j++)
            {
            }

            while (--i >= 0)
            {
                array[--j] = (byte)_mAlphabet[0];
            }

            return CopyOfRange(array, j, array.Length);
        }

        public byte[] EncodeToBytesChecked(byte[] input, int version)
        {
            return EncodeToBytesChecked(input, new byte[1] { (byte)version });
        }

        public byte[] EncodeToBytesChecked(byte[] input, byte[] version)
        {
            byte[] array = new byte[input.Length + version.Length];
            Array.Copy(version, 0, array, 0, version.Length);
            Array.Copy(input, 0, array, version.Length, input.Length);
            byte[] array2 = CopyOfRange(HashUtils.DoubleDigest(array), 0, 4);
            byte[] array3 = new byte[array.Length + array2.Length];
            Array.Copy(array, 0, array3, 0, array.Length);
            Array.Copy(array2, 0, array3, array.Length, array2.Length);
            return EncodeToBytes(array3);
        }

        public string EncodeToString(byte[] input)
        {
            byte[] bytes = EncodeToBytes(input);
            return Encoding.ASCII.GetString(bytes);
        }

        public string EncodeToStringChecked(byte[] input, int version)
        {
            return EncodeToStringChecked(input, new byte[1] { (byte)version });
        }

        public string EncodeToStringChecked(byte[] input, byte[] version)
        {
            return Encoding.ASCII.GetString(EncodeToBytesChecked(input, version));
        }

        public byte[] FindPrefix(int payLoadLength, string desiredPrefix)
        {
            int num = payLoadLength + 4;
            int times = (int)Math.Ceiling(Math.Log(Math.Pow(256.0, num)) / Math.Log(58.0) + 0.2);
            int num2 = _mAlphabet.Length / 2 - 1;
            char repeated = _mAlphabet[num2];
            string input = desiredPrefix + Repeat(times, repeated);
            byte[] array = Decode(input);
            return CopyOfRange(array, 0, array.Length - num);
        }

        public bool IsValid(string input, Version version)
        {
            try
            {
                Decode(input, version);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public bool IsValid(string input, Versions version)
        {
            try
            {
                Decode(input, version);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static bool ArrayEquals(IReadOnlyCollection<byte> a, IReadOnlyList<byte> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            return !a.Where((t, i) => t != b[i]).Any();
        }

        private static void CheckLength(Version version, byte[] buffer)
        {
            if (version.ExpectedLength != buffer.Length)
            {
                throw new ArgumentException("version has expected " + $"length of {version.ExpectedLength}");
            }
        }

        private static byte[] CopyOfRange(byte[] source, int from_, int to)
        {
            byte[] array = new byte[to - from_];
            Array.Copy(source, from_, array, 0, array.Length);
            return array;
        }

        private static byte DivMod256(IList<byte> number58, int startAt)
        {
            int num = 0;
            for (int i = startAt; i < number58.Count; i++)
            {
                int num2 = number58[i] & 0xFF;
                int num3 = num * 58 + num2;
                number58[i] = (byte)(num3 / 256);
                num = num3 % 256;
            }

            return (byte)num;
        }

        private static byte DivMod58(IList<byte> number, int startAt)
        {
            int num = 0;
            for (int i = startAt; i < number.Count; i++)
            {
                int num2 = number[i] & 0xFF;
                int num3 = num * 256 + num2;
                number[i] = (byte)(num3 / 58);
                num = num3 % 58;
            }

            return (byte)num;
        }

        private static string Repeat(int times, char repeated)
        {
            char[] array = new char[times];
            for (int i = 0; i < times; i++)
            {
                array[i] = repeated;
            }

            return new string(array);
        }

        private void BuildIndexes()
        {
            _mIndexes = new int[128];
            for (int i = 0; i < _mIndexes.Length; i++)
            {
                _mIndexes[i] = -1;
            }

            for (int j = 0; j < _mAlphabet.Length; j++)
            {
                _mIndexes[(uint)_mAlphabet[j]] = j;
            }
        }

        private byte[] DecodeAndCheck(string input)
        {
            byte[] array = Decode(input);
            if (array.Length < 4)
            {
                throw new ArgumentException("Input too short");
            }

            byte[] b = CopyOfRange(HashUtils.DoubleDigest(CopyOfRange(array, 0, array.Length - 4)), 0, 4);
            if (!ArrayEquals((IReadOnlyCollection<byte>)(object)CopyOfRange(array, array.Length - 4, array.Length), b))
            {
                throw new ArgumentException("Checksum does not validate");
            }

            return array;
        }

        private void SetAlphabet(string alphabet)
        {
            _mAlphabet = alphabet.ToCharArray();
        }
    }
}
