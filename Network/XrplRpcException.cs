using System;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    /// <summary>An XRPL application error. Does not contain the request or signing secret.</summary>
    public sealed class XrplRpcException : Exception
    {
        public string Code { get; }
        public string Method { get; }
        internal JObject Response { get; }
        internal XrplRpcException(string method, string code, string message, JObject response = null)
            : base("XRPL " + method + " failed (" + code + "): " + message)
        {
            Method = method;
            Code = code;
            Response = response;
        }
    }
}
