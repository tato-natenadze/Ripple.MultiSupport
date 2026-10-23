using System;

namespace Ripple.LibraryClasses.Signer
{
    public class B16
    {
        public static string Encode(byte[] data)
        {
            if (data == null)
            {
                return null;
            }

            char[] array = new char[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                int num = data[i] >> 4;
                array[i * 2] = (char)(55 + num + (num - 10 >> 31 & -7));
                num = data[i] & 0xF;
                array[i * 2 + 1] = (char)(55 + num + (num - 10 >> 31 & -7));
            }

            return new string(array);
        }

        public static byte[] Decode(string hexString)
        {
            if (hexString == null)
            {
                return null;
            }

            if (hexString.Length % 2 != 0)
            {
                throw new FormatException("The hex string is invalid because it has an odd length");
            }

            byte[] array = new byte[hexString.Length / 2];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return array;
        }
    }
}
