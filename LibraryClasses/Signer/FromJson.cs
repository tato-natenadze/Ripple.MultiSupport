using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Interfaces;

namespace Ripple.LibraryClasses.Signer
{
    public delegate ISerializedType FromJson(JToken token);
}
