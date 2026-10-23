using Ripple.LibraryClasses.Signer.Interfaces;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public static class StExtensions
    {
        public static string ToHex(this ISerializedType st)
        {
            BytesList bytesList = new BytesList();
            st.ToBytes(bytesList);
            return bytesList.BytesHex();
        }

        public static string ToDebuggedHex(this ISerializedType st)
        {
            BytesList bytesList = new BytesList();
            st.ToBytes(bytesList);
            return bytesList.RawList().Aggregate("", (a, b) => a + "," + B16.Encode(b));
        }
    }
}
