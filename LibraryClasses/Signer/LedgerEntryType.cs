using Newtonsoft.Json.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class LedgerEntryType : SerializedEnumItem<ushort>
    {
        public class Enumeration : SerializedEnumeration<LedgerEntryType, ushort>
        {
        }

        public static Enumeration Values = new Enumeration();

        public static readonly LedgerEntryType Invalid = Add("Invalid", -1);

        public static readonly LedgerEntryType AccountRoot = Add("AccountRoot", 97);

        public static readonly LedgerEntryType DirectoryNode = Add("DirectoryNode", 100);

        public static readonly LedgerEntryType GeneratorMap = Add("GeneratorMap", 103);

        public static readonly LedgerEntryType RippleState = Add("RippleState", 114);

        public static readonly LedgerEntryType Offer = Add("Offer", 111);

        public static readonly LedgerEntryType Contract = Add("Contract", 99);

        public static readonly LedgerEntryType LedgerHashes = Add("LedgerHashes", 104);

        public static readonly LedgerEntryType EnabledAmendments = Add("EnabledAmendments", 102);

        public static readonly LedgerEntryType FeeSettings = Add("FeeSettings", 115);

        public static readonly LedgerEntryType Ticket = Add("Ticket", 84);

        public static readonly LedgerEntryType SignerList = Add("SignerList", 83);

        private LedgerEntryType(string name, int ordinal)
            : base(name, ordinal)
        {
        }

        private static LedgerEntryType Add(string reference, int ordinal)
        {
            return Values.AddEnum(new LedgerEntryType(reference, ordinal));
        }

        public static LedgerEntryType FromJson(JToken jToken)
        {
            return Values.FromJson(jToken);
        }
    }
}
