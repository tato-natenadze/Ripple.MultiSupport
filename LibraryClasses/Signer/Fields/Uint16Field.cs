namespace Ripple.LibraryClasses.Signer.Fields
{
    public class Uint16Field : Field
    {
        public Uint16Field(string name, int nthOfType, bool isSigningField = true, bool isSerialised = true)
            : base(name, nthOfType, FieldType.Uint16, isSigningField, isSerialised)
        {
        }
    }
}
