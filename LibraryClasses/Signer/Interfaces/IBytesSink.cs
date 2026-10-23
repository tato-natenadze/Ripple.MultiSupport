namespace Ripple.LibraryClasses.Signer.Interfaces
{
    public interface IBytesSink
    {
        void Put(byte aByte);

        void Put(byte[] bytes);
    }
}
