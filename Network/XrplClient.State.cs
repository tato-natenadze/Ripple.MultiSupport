using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private static JObject ReadJsonObject(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonReaderException("Unexpected content after JSON object.");
                foreach (var token in result.Descendants())
                {
                    if (token.Type == JTokenType.Undefined || token.Type == JTokenType.Constructor || token.Type == JTokenType.Raw ||
                        (token.Type == JTokenType.Float && (double.IsNaN((double)token) || double.IsInfinity((double)token))))
                        throw new JsonReaderException("Invalid JSON value.");
                }
                return result;
            }
        }

        private static JObject DecodeStateToken(string value, string parameter)
        {
            try
            {
                return ReadJsonObject(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value)));
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException || ex is DecoderFallbackException)
            { throw new ArgumentException("Invalid continuation token or checkpoint.", parameter, ex); }
        }

        private void ValidateStateBinding(JObject state, string address, string parameter)
        {
            StateInteger(state, "version", 1, 1, parameter);
            if (state["account"]?.Type != JTokenType.String || (string)state["account"] != address ||
                state["endpoint"]?.Type != JTokenType.String || (string)state["endpoint"] != _options.RpcEndpoint.AbsoluteUri)
                throw new ArgumentException("State belongs to a different account/server or version.", parameter);
        }

        private static long StateInteger(JObject state, string name, long minimum, long maximum, string parameter)
        {
            long value;
            if (state[name]?.Type != JTokenType.Integer ||
                !long.TryParse((string)state[name], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) ||
                value < minimum || value > maximum)
                throw new ArgumentException("Missing or invalid state field: " + name + ".", parameter);
            return value;
        }
    }
}
