using Ripple.LibraryClasses.Signer.Interfaces;

namespace Ripple.LibraryClasses.Signer
{
    public delegate ISerializedType FromParser(BinaryParser parser, int? hint = null);
}
