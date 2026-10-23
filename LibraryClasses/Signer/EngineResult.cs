namespace Ripple.LibraryClasses.Signer
{
    public class EngineResult : SerializedEnumItem<byte>
    {
        public class EngineResultValues : SerializedEnumeration<EngineResult, byte>
        {
        }

        public static EngineResultValues Values = new EngineResultValues();

        private readonly string _description;

        public static readonly EngineResult telLOCAL_ERROR = Add("telLOCAL_ERROR", -399, "Local failure.");

        public static readonly EngineResult telBAD_DOMAIN = Add("telBAD_DOMAIN", -398, "Domain too long.");

        public static readonly EngineResult telBAD_PATH_COUNT = Add("telBAD_PATH_COUNT", -397, "Malformed: Too many paths.");

        public static readonly EngineResult telBAD_PUBLIC_KEY = Add("telBAD_PUBLIC_KEY", -396, "Public key too long.");

        public static readonly EngineResult telFAILED_PROCESSING = Add("telFAILED_PROCESSING", -395, "Failed to correctly process transaction.");

        public static readonly EngineResult telINSUF_FEE_P = Add("telINSUF_FEE_P", -394, "Fee insufficient.");

        public static readonly EngineResult telNO_DST_PARTIAL = Add("telNO_DST_PARTIAL", -393, "Partial payment to create account not allowed.");

        public static readonly EngineResult temMALFORMED = Add("temMALFORMED", -299, "Malformed transaction.");

        public static readonly EngineResult temBAD_AMOUNT = Add("temBAD_AMOUNT", -298, "Can only send positive amounts.");

        public static readonly EngineResult temBAD_AUTH_MASTER = Add("temBAD_AUTH_MASTER", -297, "Auth for unclaimed account needs correct master key.");

        public static readonly EngineResult temBAD_CURRENCY = Add("temBAD_CURRENCY", -296, "Malformed: Bad currency.");

        public static readonly EngineResult temBAD_EXPIRATION = Add("temBAD_EXPIRATION", -295, "Malformed: Bad expiration.");

        public static readonly EngineResult temBAD_FEE = Add("temBAD_FEE", -294, "Invalid fee, negative or not XRP.");

        public static readonly EngineResult temBAD_ISSUER = Add("temBAD_ISSUER", -293, "Malformed: Bad issuer.");

        public static readonly EngineResult temBAD_LIMIT = Add("temBAD_LIMIT", -292, "Limits must be non-negative.");

        public static readonly EngineResult temBAD_OFFER = Add("temBAD_OFFER", -291, "Malformed: Bad offer.");

        public static readonly EngineResult temBAD_PATH = Add("temBAD_PATH", -290, "Malformed: Bad path.");

        public static readonly EngineResult temBAD_PATH_LOOP = Add("temBAD_PATH_LOOP", -289, "Malformed: Loop in path.");

        public static readonly EngineResult temBAD_SEND_XRP_LIMIT = Add("temBAD_SEND_XRP_LIMIT", -288, "Malformed: Limit quality is not allowed for XRP to XRP.");

        public static readonly EngineResult temBAD_SEND_XRP_MAX = Add("temBAD_SEND_XRP_MAX", -287, "Malformed: Send max is not allowed for XRP to XRP.");

        public static readonly EngineResult temBAD_SEND_XRP_NO_DIRECT = Add("temBAD_SEND_XRP_NO_DIRECT", -286, "Malformed: No Ripple direct is not allowed for XRP to XRP.");

        public static readonly EngineResult temBAD_SEND_XRP_PARTIAL = Add("temBAD_SEND_XRP_PARTIAL", -285, "Malformed: Partial payment is not allowed for XRP to XRP.");

        public static readonly EngineResult temBAD_SEND_XRP_PATHS = Add("temBAD_SEND_XRP_PATHS", -284, "Malformed: Paths are not allowed for XRP to XRP.");

        public static readonly EngineResult temBAD_SEQUENCE = Add("temBAD_SEQUENCE", -283, "Malformed: Sequence is not in the past.");

        public static readonly EngineResult temBAD_SIGNATURE = Add("temBAD_SIGNATURE", -282, "Malformed: Bad signature.");

        public static readonly EngineResult temBAD_SRC_ACCOUNT = Add("temBAD_SRC_ACCOUNT", -281, "Malformed: Bad source account.");

        public static readonly EngineResult temBAD_TRANSFER_RATE = Add("temBAD_TRANSFER_RATE", -280, "Malformed: Bad transfer rate");

        public static readonly EngineResult temDST_IS_SRC = Add("temDST_IS_SRC", -279, "Destination may not be source.");

        public static readonly EngineResult temDST_NEEDED = Add("temDST_NEEDED", -278, "Destination not specified.");

        public static readonly EngineResult temINVALID = Add("temINVALID", -277, "The transaction is ill-formed.");

        public static readonly EngineResult temINVALID_FLAG = Add("temINVALID_FLAG", -276, "The transaction has an invalid flag.");

        public static readonly EngineResult temREDUNDANT = Add("temREDUNDANT", -275, "Sends same currency to self.");

        public static readonly EngineResult temREDUNDANT_SEND_MAX = Add("temREDUNDANT_SEND_MAX", -274, "Send max is redundant.");

        public static readonly EngineResult temRIPPLE_EMPTY = Add("temRIPPLE_EMPTY", -273, "PathSet with no paths.");

        public static readonly EngineResult temUNCERTAIN = Add("temUNCERTAIN", -272, "In process of determining result. Never returned.");

        public static readonly EngineResult temUNKNOWN = Add("temUNKNOWN", -271, "The transactions requires logic not implemented yet.");

        public static readonly EngineResult tefFAILURE = Add("tefFAILURE", -199, "Failed to apply.");

        public static readonly EngineResult tefALREADY = Add("tefALREADY", -198, "The exact transaction was already in this ledger.");

        public static readonly EngineResult tefBAD_ADD_AUTH = Add("tefBAD_ADD_AUTH", -197, "Not authorized to add account.");

        public static readonly EngineResult tefBAD_AUTH = Add("tefBAD_AUTH", -196, "Transaction's public key is not authorized.");

        public static readonly EngineResult tefBAD_LEDGER = Add("tefBAD_LEDGER", -195, "Ledger in unexpected state.");

        public static readonly EngineResult tefCREATED = Add("tefCREATED", -194, "Can't add an already created account.");

        public static readonly EngineResult tefDST_TAG_NEEDED = Add("tefDST_TAG_NEEDED", -193, "Destination tag required.");

        public static readonly EngineResult tefEXCEPTION = Add("tefEXCEPTION", -192, "Unexpected program state.");

        public static readonly EngineResult tefINTERNAL = Add("tefINTERNAL", -191, "Internal error.");

        public static readonly EngineResult tefNO_AUTH_REQUIRED = Add("tefNO_AUTH_REQUIRED", -190, "Auth is not required.");

        public static readonly EngineResult tefPAST_SEQ = Add("tefPAST_SEQ", -189, "This sequence number has already past.");

        public static readonly EngineResult tefWRONG_PRIOR = Add("tefWRONG_PRIOR", -188, "tefWRONG_PRIOR");

        public static readonly EngineResult tefMASTER_DISABLED = Add("tefMASTER_DISABLED", -187, "tefMASTER_DISABLED");

        public static readonly EngineResult tefMAX_LEDGER = Add("tefMAX_LEDGER", -186, "Ledger sequence too high.");

        public static readonly EngineResult terRETRY = Add("terRETRY", -99, "Retry transaction.");

        public static readonly EngineResult terFUNDS_SPENT = Add("terFUNDS_SPENT", -98, "Can't set password, password set funds already spent.");

        public static readonly EngineResult terINSUF_FEE_B = Add("terINSUF_FEE_B", -97, "AccountID balance can't pay fee.");

        public static readonly EngineResult terNO_ACCOUNT = Add("terNO_ACCOUNT", -96, "The source account does not exist.");

        public static readonly EngineResult terNO_AUTH = Add("terNO_AUTH", -95, "Not authorized to hold IOUs.");

        public static readonly EngineResult terNO_LINE = Add("terNO_LINE", -94, "No such line.");

        public static readonly EngineResult terOWNERS = Add("terOWNERS", -93, "Non-zero owner count.");

        public static readonly EngineResult terPRE_SEQ = Add("terPRE_SEQ", -92, "Missing/inapplicable prior transaction.");

        public static readonly EngineResult terLAST = Add("terLAST", -91, "Process last.");

        public static readonly EngineResult terNO_RIPPLE = Add("terNO_RIPPLE", -90, "Process last.");

        public static readonly EngineResult tesSUCCESS = Add("tesSUCCESS", 0, "The transaction was applied.");

        public static readonly EngineResult tecCLAIM = Add("tecCLAIM", 100, "Fee claimed. Sequence used. No action.");

        public static readonly EngineResult tecPATH_PARTIAL = Add("tecPATH_PARTIAL", 101, "Path could not send full amount.");

        public static readonly EngineResult tecUNFUNDED_ADD = Add("tecUNFUNDED_ADD", 102, "Insufficient XRP balance for WalletAdd.");

        public static readonly EngineResult tecUNFUNDED_OFFER = Add("tecUNFUNDED_OFFER", 103, "Insufficient balance to fund created offer.");

        public static readonly EngineResult tecUNFUNDED_PAYMENT = Add("tecUNFUNDED_PAYMENT", 104, "Insufficient XRP balance to send.");

        public static readonly EngineResult tecFAILED_PROCESSING = Add("tecFAILED_PROCESSING", 105, "Failed to correctly process transaction.");

        public static readonly EngineResult tecDIR_FULL = Add("tecDIR_FULL", 121, "Can not add entry to full directory.");

        public static readonly EngineResult tecINSUF_RESERVE_LINE = Add("tecINSUF_RESERVE_LINE", 122, "Insufficient reserve to add trust line.");

        public static readonly EngineResult tecINSUF_RESERVE_OFFER = Add("tecINSUF_RESERVE_OFFER", 123, "Insufficient reserve to create offer.");

        public static readonly EngineResult tecNO_DST = Add("tecNO_DST", 124, "Destination does not exist. Send XRP to create it.");

        public static readonly EngineResult tecNO_DST_INSUF_XRP = Add("tecNO_DST_INSUF_XRP", 125, "Destination does not exist. Too little XRP sent to create it.");

        public static readonly EngineResult tecNO_LINE_INSUF_RESERVE = Add("tecNO_LINE_INSUF_RESERVE", 126, "No such line. Too little reserve to create it.");

        public static readonly EngineResult tecNO_LINE_REDUNDANT = Add("tecNO_LINE_REDUNDANT", 127, "Can't set non-existant line to default.");

        public static readonly EngineResult tecPATH_DRY = Add("tecPATH_DRY", 128, "Path could not send partial amount.");

        public static readonly EngineResult tecUNFUNDED = Add("tecUNFUNDED", 129, "One of _ADD, _OFFER, or _SEND. Deprecated.");

        public static readonly EngineResult tecNO_ALTERNATIVE_KEY = Add("tecNO_ALTERNATIVE_KEY", 130, "tecNO_ALTERNATIVE_KEY");

        public static readonly EngineResult tecNO_REGULAR_KEY = Add("tecNO_REGULAR_KEY", 131, "tecNO_REGULAR_KEY");

        public static readonly EngineResult tecOWNERS = Add("tecOWNERS", 132, "tecOWNERS");

        public static readonly EngineResult tecNO_ISSUER = Add("tecNO_ISSUER", 133, "Issuer account does not exist.");

        public static readonly EngineResult tecNO_AUTH = Add("tecNO_AUTH", 134, "Not authorized to hold asset.");

        public static readonly EngineResult tecNO_LINE = Add("tecNO_LINE", 135, "No such line.");

        public static readonly EngineResult tecINSUFF_FEE = Add("tecINSUFF_FEE", 136, "Insufficient balance to pay fee.");

        public static readonly EngineResult tecFROZEN = Add("tecFROZEN", 137, "Asset is frozen.");

        public static readonly EngineResult tecNO_TARGET = Add("tecNO_TARGET", 138, "Target account does not exist.");

        public static readonly EngineResult tecNO_PERMISSION = Add("tecNO_PERMISSION", 139, "No permission to perform requested operation.");

        public static readonly EngineResult tecNO_ENTRY = Add("tecNO_ENTRY", 140, "No matching entry found.");

        public static readonly EngineResult tecINSUFFICIENT_RESERVE = Add("tecINSUFFICIENT_RESERVE", 141, "Insufficient reserve to complete requested operation.");

        public static readonly EngineResult tecNEED_MASTER_KEY = Add("tecNEED_MASTER_KEY", 142, "The operation requires the use of the Master Key.");

        public static readonly EngineResult tecDST_TAG_NEEDED = Add("tecDST_TAG_NEEDED", 143, "A destination tag is required.");

        public static readonly EngineResult tecINTERNAL = Add("tecINTERNAL", 144, "An internal error has occurred during processing.");

        public static readonly EngineResult tecOVERSIZE = Add("tecOVERSIZE", 145, "Object exceeded serialization limits.");

        public EngineResult(string name, int ordinal, string description)
            : base(name, ordinal)
        {
            _description = description;
        }

        private static EngineResult Add(string name, int ordinal, string description)
        {
            return Values.AddEnum(new EngineResult(name, ordinal, description));
        }

        public bool ShouldClaimFee()
        {
            return Ordinal >= 0;
        }
    }
}
