using System;

namespace Ripple.LibraryClasses.Signer.Fields
{
    public class Field : EnumItem
    {
        public readonly bool IsSigningField;

        public readonly bool IsSerialised;

        public readonly bool IsVlEncoded;

        public readonly int NthOfType;

        public readonly FieldType Type;

        public readonly byte[] Header;

        public FromJson FromJson;
        public FromParser FromParser;
        public static readonly Enumeration<Field> Values = new Enumeration<Field>();

        public static readonly Field Generic = new Field("Generic", 0, FieldType.Unknown);

        public static readonly Field Invalid = new Field("Invalid", -1, FieldType.Unknown);

        public static readonly LedgerEntryTypeField LedgerEntryType = new LedgerEntryTypeField("LedgerEntryType", 1);

        public static readonly TransactionTypeField TransactionType = new TransactionTypeField("TransactionType", 2);

        public static readonly Uint16Field SignerWeight = new Uint16Field("SignerWeight", 3);

        public static readonly Uint16Field TransferFee = new Uint16Field("TransferFee", 4);

        public static readonly Uint32Field Flags = new Uint32Field("Flags", 2);

        public static readonly Uint32Field SourceTag = new Uint32Field("SourceTag", 3);

        public static readonly Uint32Field Sequence = new Uint32Field("Sequence", 4);

        public static readonly Uint32Field PreviousTxnLgrSeq = new Uint32Field("PreviousTxnLgrSeq", 5);

        public static readonly Uint32Field LedgerSequence = new Uint32Field("LedgerSequence", 6);

        public static readonly Uint32Field CloseTime = new Uint32Field("CloseTime", 7);

        public static readonly Uint32Field ParentCloseTime = new Uint32Field("ParentCloseTime", 8);

        public static readonly Uint32Field SigningTime = new Uint32Field("SigningTime", 9);

        public static readonly Uint32Field Expiration = new Uint32Field("Expiration", 10);

        public static readonly Uint32Field TransferRate = new Uint32Field("TransferRate", 11);

        public static readonly Uint32Field WalletSize = new Uint32Field("WalletSize", 12);

        public static readonly Uint32Field OwnerCount = new Uint32Field("OwnerCount", 13);

        public static readonly Uint32Field DestinationTag = new Uint32Field("DestinationTag", 14);

        public static readonly Uint32Field HighQualityIn = new Uint32Field("HighQualityIn", 16);

        public static readonly Uint32Field HighQualityOut = new Uint32Field("HighQualityOut", 17);

        public static readonly Uint32Field LowQualityIn = new Uint32Field("LowQualityIn", 18);

        public static readonly Uint32Field LowQualityOut = new Uint32Field("LowQualityOut", 19);

        public static readonly Uint32Field QualityIn = new Uint32Field("QualityIn", 20);

        public static readonly Uint32Field QualityOut = new Uint32Field("QualityOut", 21);

        public static readonly Uint32Field StampEscrow = new Uint32Field("StampEscrow", 22);

        public static readonly Uint32Field BondAmount = new Uint32Field("BondAmount", 23);

        public static readonly Uint32Field LoadFee = new Uint32Field("LoadFee", 24);

        public static readonly Uint32Field OfferSequence = new Uint32Field("OfferSequence", 25);

        [Obsolete]
        public static readonly Uint32Field FirstLedgerSequence = new Uint32Field("FirstLedgerSequence", 26);

        public static readonly Uint32Field LastLedgerSequence = new Uint32Field("LastLedgerSequence", 27);

        public static readonly Uint32Field TransactionIndex = new Uint32Field("TransactionIndex", 28);

        public static readonly Uint32Field OperationLimit = new Uint32Field("OperationLimit", 29);

        public static readonly Uint32Field ReferenceFeeUnits = new Uint32Field("ReferenceFeeUnits", 30);

        public static readonly Uint32Field ReserveBase = new Uint32Field("ReserveBase", 31);

        public static readonly Uint32Field ReserveIncrement = new Uint32Field("ReserveIncrement", 32);

        public static readonly Uint32Field SetFlag = new Uint32Field("SetFlag", 33);

        public static readonly Uint32Field ClearFlag = new Uint32Field("ClearFlag", 34);

        public static readonly Uint32Field SignerQuorum = new Uint32Field("SignerQuorum", 35);

        public static readonly Uint32Field CancelAfter = new Uint32Field("CancelAfter", 36);

        public static readonly Uint32Field FinishAfter = new Uint32Field("FinishAfter", 37);

        public static readonly Uint32Field SignerListID = new Uint32Field("SignerListID", 38);

        public static readonly Uint32Field SettleDelay = new Uint32Field("SettleDelay", 39);

        public static readonly Uint32Field TicketCount = new Uint32Field("TicketCount", 40);

        public static readonly Uint32Field TicketSequence = new Uint32Field("TicketSequence", 41);

        public static readonly Uint32Field NFTokenTaxon = new Uint32Field("NFTokenTaxon", 42);

        public static readonly Uint32Field MintedTokens = new Uint32Field("MintedTokens", 43);

        public static readonly Uint32Field BurnedTokens = new Uint32Field("BurnedTokens", 44);

        public static readonly Uint64Field IndexNext = new Uint64Field("IndexNext", 1);

        public static readonly Uint64Field IndexPrevious = new Uint64Field("IndexPrevious", 2);

        public static readonly Uint64Field BookNode = new Uint64Field("BookNode", 3);

        public static readonly Uint64Field OwnerNode = new Uint64Field("OwnerNode", 4);

        public static readonly Uint64Field BaseFee = new Uint64Field("BaseFee", 5);

        public static readonly Uint64Field ExchangeRate = new Uint64Field("ExchangeRate", 6);

        public static readonly Uint64Field LowNode = new Uint64Field("LowNode", 7);

        public static readonly Uint64Field HighNode = new Uint64Field("HighNode", 8);

        public static readonly Hash128Field EmailHash = new Hash128Field("EmailHash", 1);

        public static readonly Hash256Field LedgerHash = new Hash256Field("LedgerHash", 1);

        public static readonly Hash256Field ParentHash = new Hash256Field("ParentHash", 2);

        public static readonly Hash256Field TransactionHash = new Hash256Field("TransactionHash", 3);

        public static readonly Hash256Field AccountHash = new Hash256Field("AccountHash", 4);

        public static readonly Hash256Field PreviousTxnID = new Hash256Field("PreviousTxnID", 5);

        public static readonly Hash256Field LedgerIndex = new Hash256Field("LedgerIndex", 6);

        public static readonly Hash256Field WalletLocator = new Hash256Field("WalletLocator", 7);

        public static readonly Hash256Field RootIndex = new Hash256Field("RootIndex", 8);

        public static readonly Hash256Field AccountTxnID = new Hash256Field("AccountTxnID", 9);

        public static readonly Hash256Field BookDirectory = new Hash256Field("BookDirectory", 16);

        public static readonly Hash256Field InvoiceID = new Hash256Field("InvoiceID", 17);

        public static readonly Hash256Field Nickname = new Hash256Field("Nickname", 18);

        public static readonly Hash256Field Amendment = new Hash256Field("Amendment", 19);

        public static readonly Hash256Field TicketID = new Hash256Field("TicketID", 20);

        public static readonly Hash256Field Digest = new Hash256Field("Digest", 21);

        public static readonly Hash256Field Channel = new Hash256Field("Channel", 22);

        public static readonly Hash256Field ConsensusHash = new Hash256Field("ConsensusHash", 23);

        public static readonly Hash256Field hash = new Hash256Field("hash", 257);

        public static readonly Hash256Field index = new Hash256Field("index", 258);

        public static readonly Hash256Field NFTokenID = new Hash256Field("NFTokenID", 10);

        public static readonly Hash256Field NFTokenBuyOffer = new Hash256Field("NFTokenBuyOffer", 28);

        public static readonly Hash256Field NFTokenSellOffer = new Hash256Field("NFTokenSellOffer", 29);

        public static readonly AmountField Amount = new AmountField("Amount", 1);

        public static readonly AmountField Balance = new AmountField("Balance", 2);

        public static readonly AmountField LimitAmount = new AmountField("LimitAmount", 3);

        public static readonly AmountField TakerPays = new AmountField("TakerPays", 4);

        public static readonly AmountField TakerGets = new AmountField("TakerGets", 5);

        public static readonly AmountField LowLimit = new AmountField("LowLimit", 6);

        public static readonly AmountField HighLimit = new AmountField("HighLimit", 7);

        public static readonly AmountField Fee = new AmountField("Fee", 8);

        public static readonly AmountField SendMax = new AmountField("SendMax", 9);

        public static readonly AmountField DeliverMin = new AmountField("DeliverMin", 10);

        public static readonly AmountField MinimumOffer = new AmountField("MinimumOffer", 16);

        public static readonly AmountField RippleEscrow = new AmountField("RippleEscrow", 17);

        public static readonly AmountField DeliveredAmount = new AmountField("DeliveredAmount", 18);

        public static readonly AmountField BrokerFee = new AmountField("BrokerFee", 19);

        // Keep the existing public field and its JSON name for older callers.
        // Both names use the same wire field; new transactions use the XRPL name.
        public static readonly AmountField NFTokenBrokerFee = new AmountField("NFTokenBrokerFee", 19);

        public static readonly AmountField taker_gets_funded = new AmountField("taker_gets_funded", 258);

        public static readonly AmountField taker_pays_funded = new AmountField("taker_pays_funded", 259);

        public static readonly BlobField PublicKey = new BlobField("PublicKey", 1);

        public static readonly BlobField MessageKey = new BlobField("MessageKey", 2);

        public static readonly BlobField SigningPubKey = new BlobField("SigningPubKey", 3);

        public static readonly BlobField TxnSignature = new BlobField("TxnSignature", 4, isSigningField: false);

        public static readonly BlobField URI = new BlobField("URI", 5);

        public static readonly BlobField Signature = new BlobField("Signature", 6);

        public static readonly BlobField Domain = new BlobField("Domain", 7);

        public static readonly BlobField FundCode = new BlobField("FundCode", 8);

        public static readonly BlobField RemoveCode = new BlobField("RemoveCode", 9);

        public static readonly BlobField ExpireCode = new BlobField("ExpireCode", 10);

        public static readonly BlobField CreateCode = new BlobField("CreateCode", 11);

        public static readonly BlobField MemoType = new BlobField("MemoType", 12);

        public static readonly BlobField MemoData = new BlobField("MemoData", 13);

        public static readonly BlobField MemoFormat = new BlobField("MemoFormat", 14);

        public static readonly BlobField Fulfillment = new BlobField("Fulfillment", 16);

        public static readonly BlobField Condition = new BlobField("Condition", 17);

        public static readonly BlobField MasterSignature = new BlobField("MasterSignature", 18);

        public static readonly AccountIdField Account = new AccountIdField("Account", 1);

        public static readonly AccountIdField Owner = new AccountIdField("Owner", 2);

        public static readonly AccountIdField Destination = new AccountIdField("Destination", 3);

        public static readonly AccountIdField Issuer = new AccountIdField("Issuer", 4);

        public static readonly AccountIdField Target = new AccountIdField("Target", 7);

        public static readonly AccountIdField RegularKey = new AccountIdField("RegularKey", 8);

        public static readonly StObjectField ObjectEndMarker = new StObjectField("ObjectEndMarker", 1);

        public static readonly StObjectField TransactionMetaData = new StObjectField("TransactionMetaData", 2);

        public static readonly StObjectField CreatedNode = new StObjectField("CreatedNode", 3);

        public static readonly StObjectField DeletedNode = new StObjectField("DeletedNode", 4);

        public static readonly StObjectField ModifiedNode = new StObjectField("ModifiedNode", 5);

        public static readonly StObjectField PreviousFields = new StObjectField("PreviousFields", 6);

        public static readonly StObjectField FinalFields = new StObjectField("FinalFields", 7);

        public static readonly StObjectField NewFields = new StObjectField("NewFields", 8);

        public static readonly StObjectField TemplateEntry = new StObjectField("TemplateEntry", 9);

        public static readonly StObjectField Memo = new StObjectField("Memo", 10);

        public static readonly StObjectField SignerEntry = new StObjectField("SignerEntry", 11);

        public static readonly StObjectField Signer = new StObjectField("Signer", 16);

        public static readonly StObjectField Majority = new StObjectField("Majority", 18);

        public static readonly StArrayField ArrayEndMarker = new StArrayField("ArrayEndMarker", 1);

        public static readonly StArrayField Signers = new StArrayField("Signers", 3, isSigningField: false);

        public static readonly StArrayField SignerEntries = new StArrayField("SignerEntries", 4);

        public static readonly StArrayField Template = new StArrayField("Template", 5);

        public static readonly StArrayField Necessary = new StArrayField("Necessary", 6);

        public static readonly StArrayField Sufficient = new StArrayField("Sufficient", 7);

        public static readonly StArrayField AffectedNodes = new StArrayField("AffectedNodes", 8);

        public static readonly StArrayField Memos = new StArrayField("Memos", 9);

        public static readonly Uint8Field CloseResolution = new Uint8Field("CloseResolution", 1);

        public static readonly Uint8Field Method = new Uint8Field("Method", 2);

        public static readonly EngineResultField TransactionResult = new EngineResultField("TransactionResult", 3);

        public static readonly Uint8Field TickSize = new Uint8Field("TickSize", 16);

        public static readonly Hash160Field TakerPaysCurrency = new Hash160Field("TakerPaysCurrency", 1);

        public static readonly Hash160Field TakerPaysIssuer = new Hash160Field("TakerPaysIssuer", 2);

        public static readonly Hash160Field TakerGetsCurrency = new Hash160Field("TakerGetsCurrency", 3);

        public static readonly Hash160Field TakerGetsIssuer = new Hash160Field("TakerGetsIssuer", 4);

        public static readonly PathSetField Paths = new PathSetField("Paths", 1);

        public static readonly Vector256Field Indexes = new Vector256Field("Indexes", 1);

        public static readonly Vector256Field Hashes = new Vector256Field("Hashes", 2);

        public static readonly Vector256Field Features = new Vector256Field("Features", 3);

        public static readonly Vector256Field NFTokenOffers = new Vector256Field("NFTokenOffers", 4);

        public static readonly Field Transaction = new Field("Transaction", 1, FieldType.Transaction);

        public static readonly Field LedgerEntry = new Field("LedgerEntry", 1, FieldType.LedgerEntry);

        public static readonly Field Validation = new Field("Validation", 1, FieldType.Validation);

        static Field()
        {
            // Preserve the legacy decoder's JSON name for wire field 6/19.
            Values[BrokerFee.Ordinal] = BrokerFee;
        }

        public Field(string name, int nthOfType, FieldType type, bool isSigningField = true, bool isSerialised = true)
            : base(name, type.Ordinal << 16 | nthOfType)
        {
            bool flag = nthOfType > 0 && nthOfType < 256 && type.Ordinal > 0 && type.Ordinal < 256;
            Type = type;
            IsSigningField = flag && isSigningField;
            IsSerialised = flag && isSerialised;
            NthOfType = nthOfType;
            IsVlEncoded = IsVlEncodedType();
            Header = CalculateHeader();
            Values.AddEnum(this);
        }

        public static implicit operator Field(string s)
        {
            return Values[s];
        }

        private byte[] CalculateHeader()
        {
            int nthOfType = NthOfType;
            int ordinal = Type.Ordinal;
            if (ordinal < 16)
            {
                if (nthOfType >= 16)
                {
                    return new byte[2]
                    {
                    (byte)(ordinal << 4),
                    (byte)nthOfType
                    };
                }

                return new byte[1] { (byte)(ordinal << 4 | nthOfType) };
            }

            if (nthOfType >= 16)
            {
                return new byte[3]
                {
                0,
                (byte)ordinal,
                (byte)nthOfType
                };
            }

            return new byte[2]
            {
            (byte)nthOfType,
            (byte)ordinal
            };
        }

        private bool IsVlEncodedType()
        {
            if (Type != FieldType.Vector256 && Type != FieldType.Blob)
            {
                return Type == FieldType.AccountId;
            }

            return true;
        }
    }
}
