namespace Ripple.LibraryClasses.Signer
{
    public class TransactionType : SerializedEnumItem<ushort>
    {
        public class Enumeration : SerializedEnumeration<TransactionType, ushort>
        {
        }

        public static Enumeration Values = new Enumeration();

        public static readonly TransactionType Invalid = Add("Invalid", -1);

        public static readonly TransactionType Payment = Add("Payment", 0);

        public static readonly TransactionType EscrowCreate = Add("EscrowCreate", 1);

        public static readonly TransactionType EscrowFinish = Add("EscrowFinish", 2);

        public static readonly TransactionType AccountSet = Add("AccountSet", 3);

        public static readonly TransactionType EscrowCancel = Add("EscrowCancel", 4);

        public static readonly TransactionType SetRegularKey = Add("SetRegularKey", 5);

        public static readonly TransactionType OfferCreate = Add("OfferCreate", 7);

        public static readonly TransactionType OfferCancel = Add("OfferCancel", 8);

        public static readonly TransactionType TicketCreate = Add("TicketCreate", 10);

        public static readonly TransactionType TicketCancel = Add("TicketCancel", 11);

        public static readonly TransactionType SignerListSet = Add("SignerListSet", 12);

        public static readonly TransactionType PaymentChannelCreate = Add("PaymentChannelCreate", 13);

        public static readonly TransactionType PaymentChannelFund = Add("PaymentChannelFund", 14);

        public static readonly TransactionType PaymentChannelClaim = Add("PaymentChannelClaim", 15);

        public static readonly TransactionType TrustSet = Add("TrustSet", 20);

        public static readonly TransactionType NFTokenMint = Add("NFTokenMint", 25);

        public static readonly TransactionType NFTokenBurn = Add("NFTokenBurn", 26);

        public static readonly TransactionType NFTokenCreateOffer = Add("NFTokenCreateOffer", 27);

        public static readonly TransactionType NFTokenCancelOffer = Add("NFTokenCancelOffer", 28);

        public static readonly TransactionType NFTokenAcceptOffer = Add("NFTokenAcceptOffer", 29);

        public static readonly TransactionType EnableAmendment = Add("EnableAmendment", 100);

        public static readonly TransactionType SetFee = Add("SetFee", 101);

        private TransactionType(string reference, int ordinal)
            : base(reference, ordinal)
        {
        }

        private static TransactionType Add(string name, int ordinal)
        {
            return Values.AddEnum(new TransactionType(name, ordinal));
        }
    }
}
