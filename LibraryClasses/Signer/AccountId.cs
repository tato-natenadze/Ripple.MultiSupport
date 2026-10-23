using Newtonsoft.Json.Linq;
using Ripple.LibraryClasses.Signer.Hashes;
using Ripple.LibraryClasses.ValidateAddress;
using System;

namespace Ripple.LibraryClasses.Signer
{
    public class AccountId : Hash160
    {
        private string _encoded;

        public static readonly AccountId Zero = 0u;

        public static readonly AccountId Neutral = 1u;

        private string Encoded
        {
            get
            {
                return _encoded ?? (_encoded = AddressCodec.EncodeAddress(Buffer));
            }
            set
            {
                _encoded = value;
            }
        }

        public AccountId(byte[] hash, string encoded)
            : base(hash)
        {
            Encoded = encoded;
        }

        public AccountId(string v)
            : this(AddressCodec.DecodeAddress(v), v)
        {
        }

        public AccountId(byte[] hash)
            : this(hash, AddressCodec.EncodeAddress(hash))
        {
        }

        public static implicit operator AccountId(string value)
        {
            return new AccountId(value);
        }

        public static implicit operator AccountId(uint value)
        {
            byte[] array = new byte[20];
            Array.Copy(Bits.GetBytes(value), 0, array, 16, 4);
            return new AccountId(array);
        }

        public static implicit operator AccountId(JToken json)
        {
            if (json != null)
            {
                return FromJson(json);
            }

            return null;
        }

        public static implicit operator JToken(AccountId v)
        {
            return v.ToString();
        }

        public override string ToString()
        {
            return Encoded;
        }

        public new static AccountId FromJson(JToken json)
        {
            return json?.ToString();
        }

        public new static AccountId FromParser(BinaryParser parser, int? hint = null)
        {
            return new AccountId(parser.Read(20));
        }
    }
}
