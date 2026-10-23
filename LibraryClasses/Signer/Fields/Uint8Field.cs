namespace Ripple.LibraryClasses.Signer.Fields
{
    public class Uint8Field : Field
    {
        public Uint8Field(string name, int nthOfType, bool isSigningField = true, bool isSerialised = true)
            : base(name, nthOfType, FieldType.Uint8, isSigningField, isSerialised)
        {
        }
    }
}
