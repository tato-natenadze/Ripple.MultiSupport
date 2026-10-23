namespace Ripple.LibraryClasses.Signer.Fields
{
    public class FieldType : EnumItem
    {
        public static Enumeration<FieldType> Values = new Enumeration<FieldType>();

        public static readonly FieldType Unknown = new FieldType("Unknown", -2);

        public static readonly FieldType Done = new FieldType("Done", -1);

        public static readonly FieldType NotPresent = new FieldType("NotPresent", 0);

        public static readonly FieldType Uint16 = new FieldType("Uint16", 1);

        public static readonly FieldType Uint32 = new FieldType("Uint32", 2);

        public static readonly FieldType Uint64 = new FieldType("Uint64", 3);

        public static readonly FieldType Hash128 = new FieldType("Hash128", 4);

        public static readonly FieldType Hash256 = new FieldType("Hash256", 5);

        public static readonly FieldType Amount = new FieldType("Amount", 6);

        public static readonly FieldType Blob = new FieldType("Blob", 7);

        public static readonly FieldType AccountId = new FieldType("AccountId", 8);

        public static readonly FieldType StObject = new FieldType("StObject", 14);

        public static readonly FieldType StArray = new FieldType("StArray", 15);

        public static readonly FieldType Uint8 = new FieldType("Uint8", 16);

        public static readonly FieldType Hash160 = new FieldType("Hash160", 17);

        public static readonly FieldType PathSet = new FieldType("PathSet", 18);

        public static readonly FieldType Vector256 = new FieldType("Vector256", 19);

        public static readonly FieldType Transaction = new FieldType("Transaction", 10001);

        public static readonly FieldType LedgerEntry = new FieldType("LedgerEntry", 10002);

        public static readonly FieldType Validation = new FieldType("Validation", 10003);

        public FieldType(string name, int ordinal)
            : base(name, ordinal)
        {
            Values.AddEnum(this);
        }
    }
}
