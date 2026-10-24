namespace Ripple
{
    public enum PaymentErrorKind { Rpc, Http, Transport, Timeout, Cancelled, InvalidResponse }

    /// <summary>Safe diagnostics for an indeterminate payment. Does not contain a request, secret or raw server message.</summary>
    public sealed class PaymentError
    {
        public PaymentErrorKind Kind { get; internal set; }
        public string Method { get; internal set; }
        public string RpcCode { get; internal set; }
        public int? HttpStatusCode { get; internal set; }
        public string Message { get; internal set; }
    }
}
