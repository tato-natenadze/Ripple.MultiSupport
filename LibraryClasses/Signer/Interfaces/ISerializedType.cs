using Newtonsoft.Json.Linq;

namespace Ripple.LibraryClasses.Signer.Interfaces
{
    public interface ISerializedType
    {
        void ToBytes(IBytesSink sink);

        JToken ToJson();
    }
}
