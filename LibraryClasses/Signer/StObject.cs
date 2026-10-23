using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Fields;
using Ripple.LibraryClasses.Signer.Hashes;
using Ripple.LibraryClasses.Signer.Interfaces;
using Ripple.LibraryClasses.Signer.Uints;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ripple.LibraryClasses.Signer
{
    public class StObject : ISerializedType
    {
        internal class BuildFrom
        {
            public FromParser Parser;

            public FromJson Json;

            public BuildFrom(FromJson json, FromParser parser)
            {
                Parser = parser;
                Json = json;
            }
        }

        protected internal readonly SortedDictionary<Field, ISerializedType> Fields;

        public AccountId this[AccountIdField f]
        {
            get
            {
                return (AccountId)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Amount this[AmountField f]
        {
            get
            {
                return (Amount)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Blob this[BlobField f]
        {
            get
            {
                return (Blob)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Hash128 this[Hash128Field f]
        {
            get
            {
                return (Hash128)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Hash160 this[Hash160Field f]
        {
            get
            {
                return (Hash160)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Hash256 this[Hash256Field f]
        {
            get
            {
                return (Hash256)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public PathSet this[PathSetField f]
        {
            get
            {
                return (PathSet)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public StArray this[StArrayField f]
        {
            get
            {
                return (StArray)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public StObject this[StObjectField f]
        {
            get
            {
                return (StObject)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Uint16 this[Uint16Field f]
        {
            get
            {
                return (Uint16)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public LedgerEntryType this[LedgerEntryTypeField f]
        {
            get
            {
                return (LedgerEntryType)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public TransactionType this[TransactionTypeField f]
        {
            get
            {
                return (TransactionType)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Uint32 this[Uint32Field f]
        {
            get
            {
                return (Uint32)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Uint64 this[Uint64Field f]
        {
            get
            {
                return (Uint64)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Uint8 this[Uint8Field f]
        {
            get
            {
                return (Uint8)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public EngineResult this[EngineResultField f]
        {
            get
            {
                return (EngineResult)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public Vector256 this[Vector256Field f]
        {
            get
            {
                return (Vector256)Fields[f];
            }
            set
            {
                Fields[f] = value;
            }
        }

        public StObject()
        {
            Fields = new SortedDictionary<Field, ISerializedType>();
        }

        static StObject()
        {
            Dictionary<FieldType, BuildFrom> d2 = new Dictionary<FieldType, BuildFrom>
            {
                [FieldType.StObject] = new BuildFrom(FromJson, FromParser),
                [FieldType.StArray] = new BuildFrom(StArray.FromJson, StArray.FromParser),
                [FieldType.Uint8] = new BuildFrom(Uint8.FromJson, Uint8.FromParser),
                [FieldType.Uint32] = new BuildFrom(Uint32.FromJson, Uint32.FromParser),
                [FieldType.Uint64] = new BuildFrom(Uint64.FromJson, Uint64.FromParser),
                [FieldType.Uint16] = new BuildFrom(Uint16.FromJson, Uint16.FromParser),
                [FieldType.Amount] = new BuildFrom(Amount.FromJson, Amount.FromParser),
                [FieldType.Hash128] = new BuildFrom(Hash128.FromJson, Hash128.FromParser),
                [FieldType.Hash256] = new BuildFrom(Hash256.FromJson, Hash256.FromParser),
                [FieldType.Hash160] = new BuildFrom(Hash160.FromJson, Hash160.FromParser),
                [FieldType.AccountId] = new BuildFrom(AccountId.FromJson, AccountId.FromParser),
                [FieldType.Blob] = new BuildFrom(Blob.FromJson, Blob.FromParser),
                [FieldType.PathSet] = new BuildFrom(PathSet.FromJson, PathSet.FromParser),
                [FieldType.Vector256] = new BuildFrom(Vector256.FromJson, Vector256.FromParser)
            };
            foreach (Field item in Field.Values.Where((field) => d2.ContainsKey(field.Type)))
            {
                BuildFrom buildFrom = d2[item.Type];
                item.FromJson = buildFrom.Json;
                item.FromParser = buildFrom.Parser;
            }

            Field.TransactionType.FromJson = TransactionType.Values.FromJson;
            Field.TransactionType.FromParser = TransactionType.Values.FromParser;
            Field.TransactionResult.FromJson = EngineResult.Values.FromJson;
            Field.TransactionResult.FromParser = EngineResult.Values.FromParser;
            Field.LedgerEntryType.FromJson = LedgerEntryType.Values.FromJson;
            Field.LedgerEntryType.FromParser = LedgerEntryType.Values.FromParser;
        }

        public static StObject FromParser(BinaryParser parser, int? hint = null)
        {
            StObject stObject = new StObject();
            if (hint.HasValue)
            {
                hint = parser.Pos() + hint;
            }

            while (!parser.End(hint))
            {
                Field field = parser.ReadField();
                if (field == Field.ObjectEndMarker)
                {
                    break;
                }

                int? hint2 = field.IsVlEncoded ? new int?(parser.ReadVlLength()) : null;
                ISerializedType serializedType = field.FromParser(parser, hint2);
                if (serializedType == null)
                {
                    throw new InvalidOperationException(string.Concat("Parsed ", field, " as null"));
                }

                stObject.Fields[field] = serializedType;
            }

            return stObject;
        }

        public static StObject FromJson(JToken token)
        {
            return FromJson(token, strict: false);
        }

        public static StObject FromJson(JToken token, bool strict)
        {
            if (token.Type != JTokenType.Object)
            {
                throw new ArgumentException("{token} is not an object");
            }

            StObject stObject = new StObject();
            foreach (KeyValuePair<string, JToken> item in (JObject)token)
            {
                if (!Field.Values.Has(item.Key))
                {
                    if (strict)
                    {
                        throw new ArgumentException("unknown field " + item.Key);
                    }

                    continue;
                }

                Field field = Field.Values[item.Key];
                if (stObject.Fields.ContainsKey(field))
                {
                    throw new ArgumentException("Duplicate serialized field " + item.Key);
                }
                JToken value = item.Value;
                ISerializedType value2;
                try
                {
                    value2 = field.FromJson(value);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"In Stobject line 334 Can't decode `{field}` " + $"from `{value}`", ex);
                }

                stObject.Fields[field] = value2;
            }

            return stObject;
        }

        public void ToBytes(IBytesSink to)
        {
            ToBytes(to, null);
        }

        public JToken ToJson()
        {
            return ToJsonObject();
        }

        public JObject ToJsonObject()
        {
            JObject jObject = new JObject();
            foreach (KeyValuePair<Field, ISerializedType> field in Fields)
            {
                jObject[field.Key] = field.Value.ToJson();
            }
            return jObject;
        }

        public void ToBytes(IBytesSink to, Func<Field, bool> p)
        {
            BinarySerializer binarySerializer = new BinarySerializer(to);
            foreach (KeyValuePair<Field, ISerializedType> item in Fields.Where((pair) => pair.Key.IsSerialised && (p == null || p(pair.Key))))
            {
                binarySerializer.Add(item.Key, item.Value);
            }
        }

        public static implicit operator StObject(JToken v)
        {
            return FromJson(v);
        }

        public static StObject FromHex(string s)
        {
            return FromParser(new BufferParser(s));
        }

        public bool Has(Field field)
        {
            return Fields.ContainsKey(field);
        }

        public byte[] SigningData()
        {
            BytesList bytesList = new BytesList();
            bytesList.Put(HashPrefix.TxSign.Bytes());
            ToBytes(bytesList, (f) => f.IsSigningField);
            return bytesList.Bytes();
        }

        public byte[] ToBytes()
        {
            BytesList bytesList = new BytesList();
            ToBytes(bytesList, (f) => f.IsSerialised);
            return bytesList.Bytes();
        }

        public StObject SetFlag(uint flags)
        {
            if (Has(Field.Flags))
            {
                flags |= (uint)this[Field.Flags];
            }

            this[Field.Flags] = flags;
            return this;
        }
    }
}
