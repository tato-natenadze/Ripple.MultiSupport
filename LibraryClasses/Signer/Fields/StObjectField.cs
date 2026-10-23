namespace Ripple.LibraryClasses.Signer.Fields
{
    public class StObjectField : Field
    {
        public StObjectField(string name, int nthOfType, bool isSigningField = true, bool isSerialised = true)
            : base(name, nthOfType, FieldType.StObject, isSigningField, isSerialised)
        {
        }
    }
}
