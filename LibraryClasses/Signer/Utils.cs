using Ripple.LibraryClasses.Signer.Keys;

namespace Ripple.LibraryClasses.Signer
{
    public static class Utils
    {
        internal static string TransactionId(byte[] txBlob)
        {
            return B16.Encode(TransactionIdBytes(txBlob));
        }

        public static byte[] TransactionIdBytes(byte[] txBlob)
        {
            return Sha512.Half(txBlob, 1415073280u);
        }
    }
}
