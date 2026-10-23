using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Interfaces;
using System;
using System.Collections.Generic;

namespace Ripple.LibraryClasses.Signer
{
    public class TxFormat : Dictionary<Field, TxFormat.Requirement>
    {
        public enum Requirement
        {
            Default,
            Optional,
            Required
        }

        public static Dictionary<TransactionType, TxFormat> Formats;

        public TxFormat()
        {
            base[Field.TransactionType] = Requirement.Required;
            base[Field.Account] = Requirement.Required;
            base[Field.Fee] = Requirement.Required;
            base[Field.Sequence] = Requirement.Required;
            base[Field.SigningPubKey] = Requirement.Required;
            base[Field.Flags] = Requirement.Optional;
            base[Field.SourceTag] = Requirement.Optional;
            base[Field.PreviousTxnID] = Requirement.Optional;
            base[Field.LastLedgerSequence] = Requirement.Optional;
            base[Field.AccountTxnID] = Requirement.Optional;
            base[Field.OperationLimit] = Requirement.Optional;
            base[Field.Memos] = Requirement.Optional;
            base[Field.TxnSignature] = Requirement.Optional;
            base[Field.Signers] = Requirement.Optional;
            base[Field.TicketSequence] = Requirement.Optional;
        }

        public static void Validate(StObject obj)
        {
            List<string> list = new List<string>();
            Validate(obj, list.Add);
            if (list.Count > 0)
            {
                throw new FormatException(string.Join("\n", list));
            }
        }

        internal static void Validate(StObject obj, Action<string> onError)
        {
            if (!obj.Has(Field.TransactionType))
            {
                onError("Missing `TransactionType` field");
                return;
            }

            TransactionType transactionType = obj[Field.TransactionType];
            if (transactionType == null)
            {
                onError("`TransactionType` is set to null");
                return;
            }

            TxFormat txFormat;
            if (!Formats.TryGetValue(transactionType, out txFormat))
            {
                onError($"Unsupported transaction type `{transactionType}`");
                return;
            }
            SortedSet<Field> sortedSet = new SortedSet<Field>(obj.Fields.Keys);
            sortedSet.UnionWith(txFormat.Keys);
            foreach (Field item in sortedSet)
            {
                bool num = txFormat.ContainsKey(item);
                ISerializedType value;
                bool flag = obj.Fields.TryGetValue(item, out value);
                if (!num)
                {
                    onError($"`{transactionType}` has no `{item}` field");
                }
                else if (txFormat[item] == Requirement.Required)
                {
                    if (!flag)
                    {
                        onError($"`{transactionType}` has required field `{item}`");
                    }
                    else if (value == null)
                    {
                        onError($"Required field `{item}` is set to null");
                    }
                }
            }

            if (obj.Has(Field.TicketSequence) && obj[Field.TicketSequence] != null &&
                obj.Has(Field.Sequence) && obj[Field.Sequence] != null && (uint)obj[Field.Sequence] != 0)
            {
                onError("`Sequence` must be 0 when `TicketSequence` is provided");
            }

            if (transactionType == TransactionType.TicketCreate && obj.Has(Field.TicketCount) && obj[Field.TicketCount] != null)
            {
                uint count = obj[Field.TicketCount];
                if (count < 1 || count > 250)
                {
                    onError("`TicketCount` must be between 1 and 250");
                }
            }

            if (transactionType == TransactionType.NFTokenAcceptOffer)
            {
                bool buy = obj.Has(Field.NFTokenBuyOffer) && obj[Field.NFTokenBuyOffer] != null;
                bool sell = obj.Has(Field.NFTokenSellOffer) && obj[Field.NFTokenSellOffer] != null;
                if (!buy && !sell)
                {
                    onError("`NFTokenAcceptOffer` requires `NFTokenBuyOffer` or `NFTokenSellOffer`");
                }
                if ((obj.Has(Field.NFTokenBrokerFee) || obj.Has(Field.BrokerFee)) && (!buy || !sell))
                {
                    onError("A broker fee requires both `NFTokenBuyOffer` and `NFTokenSellOffer`");
                }
            }
        }

        static TxFormat()
        {
            Formats = new Dictionary<TransactionType, TxFormat>
            {
                [TransactionType.AccountSet] = new TxFormat
                {
                    [Field.EmailHash] = Requirement.Optional,
                    [Field.WalletLocator] = Requirement.Optional,
                    [Field.WalletSize] = Requirement.Optional,
                    [Field.MessageKey] = Requirement.Optional,
                    [Field.Domain] = Requirement.Optional,
                    [Field.TransferRate] = Requirement.Optional,
                    [Field.SetFlag] = Requirement.Optional,
                    [Field.ClearFlag] = Requirement.Optional,
                    [Field.TickSize] = Requirement.Optional
                },
                [TransactionType.TrustSet] = new TxFormat
                {
                    [Field.LimitAmount] = Requirement.Optional,
                    [Field.QualityIn] = Requirement.Optional,
                    [Field.QualityOut] = Requirement.Optional
                },
                [TransactionType.OfferCreate] = new TxFormat
                {
                    [Field.TakerPays] = Requirement.Required,
                    [Field.TakerGets] = Requirement.Required,
                    [Field.Expiration] = Requirement.Optional,
                    [Field.OfferSequence] = Requirement.Optional
                },
                [TransactionType.OfferCancel] = new TxFormat { [Field.OfferSequence] = Requirement.Required },
                [TransactionType.NFTokenMint] = new TxFormat
                {
                    [Field.NFTokenTaxon] = Requirement.Required,
                    [Field.Issuer] = Requirement.Optional,
                    [Field.TransferFee] = Requirement.Optional,
                    [Field.URI] = Requirement.Optional
                },
                [TransactionType.NFTokenBurn] = new TxFormat { [Field.NFTokenID] = Requirement.Required },
                [TransactionType.NFTokenCreateOffer] = new TxFormat
                {
                    [Field.NFTokenID] = Requirement.Required,
                    [Field.Amount] = Requirement.Required,
                    [Field.Owner] = Requirement.Optional,
                    [Field.Destination] = Requirement.Optional,
                    [Field.Expiration] = Requirement.Optional
                },
                [TransactionType.NFTokenCancelOffer] = new TxFormat { [Field.NFTokenOffers] = Requirement.Required },
                [TransactionType.NFTokenAcceptOffer] = new TxFormat
                {
                    [Field.NFTokenBuyOffer] = Requirement.Optional,
                    [Field.NFTokenSellOffer] = Requirement.Optional,
                    [Field.NFTokenBrokerFee] = Requirement.Optional,
                    [Field.BrokerFee] = Requirement.Optional
                },
                [TransactionType.SetRegularKey] = new TxFormat { [Field.RegularKey] = Requirement.Optional },
                [TransactionType.Payment] = new TxFormat
                {
                    [Field.Destination] = Requirement.Required,
                    [Field.Amount] = Requirement.Required,
                    [Field.SendMax] = Requirement.Optional,
                    [Field.Paths] = Requirement.Default,
                    [Field.InvoiceID] = Requirement.Optional,
                    [Field.DestinationTag] = Requirement.Optional,
                    [Field.DeliverMin] = Requirement.Optional
                },
                [TransactionType.EscrowCreate] = new TxFormat
                {
                    [Field.Amount] = Requirement.Required,
                    [Field.Destination] = Requirement.Required,
                    [Field.Condition] = Requirement.Optional,
                    [Field.CancelAfter] = Requirement.Optional,
                    [Field.FinishAfter] = Requirement.Optional,
                    [Field.DestinationTag] = Requirement.Optional
                },
                [TransactionType.EscrowCancel] = new TxFormat
                {
                    [Field.Owner] = Requirement.Required,
                    [Field.OfferSequence] = Requirement.Required
                },
                [TransactionType.EscrowFinish] = new TxFormat
                {
                    [Field.Owner] = Requirement.Required,
                    [Field.OfferSequence] = Requirement.Required,
                    [Field.Condition] = Requirement.Optional,
                    [Field.Fulfillment] = Requirement.Optional
                },
                [TransactionType.EnableAmendment] = new TxFormat
                {
                    [Field.LedgerSequence] = Requirement.Optional,
                    [Field.Amendment] = Requirement.Required
                },
                [TransactionType.SetFee] = new TxFormat
                {
                    [Field.LedgerSequence] = Requirement.Optional,
                    [Field.BaseFee] = Requirement.Required,
                    [Field.ReferenceFeeUnits] = Requirement.Required,
                    [Field.ReserveBase] = Requirement.Required,
                    [Field.ReserveIncrement] = Requirement.Required
                },
                [TransactionType.TicketCreate] = new TxFormat
                {
                    [Field.TicketCount] = Requirement.Required
                },
                [TransactionType.TicketCancel] = new TxFormat { [Field.TicketID] = Requirement.Required },
                [TransactionType.SignerListSet] = new TxFormat
                {
                    [Field.SignerQuorum] = Requirement.Required,
                    [Field.SignerEntries] = Requirement.Optional
                },
                [TransactionType.PaymentChannelCreate] = new TxFormat
                {
                    [Field.Destination] = Requirement.Required,
                    [Field.Amount] = Requirement.Required,
                    [Field.SettleDelay] = Requirement.Required,
                    [Field.PublicKey] = Requirement.Required,
                    [Field.CancelAfter] = Requirement.Optional,
                    [Field.DestinationTag] = Requirement.Optional
                },
                [TransactionType.PaymentChannelFund] = new TxFormat
                {
                    [Field.Channel] = Requirement.Required,
                    [Field.Amount] = Requirement.Required,
                    [Field.Expiration] = Requirement.Optional
                },
                [TransactionType.PaymentChannelClaim] = new TxFormat
                {
                    [Field.Channel] = Requirement.Required,
                    [Field.Amount] = Requirement.Optional,
                    [Field.Balance] = Requirement.Optional,
                    [Field.Signature] = Requirement.Optional,
                    [Field.PublicKey] = Requirement.Optional
                }
            };
        }
    }
}
