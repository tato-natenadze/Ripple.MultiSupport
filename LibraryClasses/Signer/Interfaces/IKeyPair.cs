namespace Ripple.LibraryClasses.Signer.Interfaces
{
    public interface IKeyPair
    {
        byte[] CanonicalPubBytes();

        bool Verify(byte[] message, byte[] signature);

        byte[] Sign(byte[] message);

        string Id();
    }
}
