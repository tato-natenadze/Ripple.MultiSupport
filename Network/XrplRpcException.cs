using System;

namespace Ripple
{
    /// <summary>An XRPL application error. Does not contain the request or signing secret.</summary>
    public sealed class XrplRpcException : Exception
    {
        public string Code { get; }
        public string Method { get; }
        internal XrplRpcException(string method, string code, string message)
            : base("XRPL " + method + " failed (" + code + "): " + message)
        {
            Method = method;
            Code = code;
        }
    }
}
