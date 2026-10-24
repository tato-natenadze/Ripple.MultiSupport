using System;
using System.IO;
using System.Net.Http;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private const string RpcMethodDataKey = "Ripple.RpcMethod";
        private const string HttpStatusDataKey = "Ripple.HttpStatus";

        private static PaymentError PaymentFailure(Exception exception, string method, string seed, bool operationCancelled)
        {
            var rpc = exception as XrplRpcException;
            var http = exception as HttpRequestException;
            int? status = exception.Data[HttpStatusDataKey] as int?;
#if NET8_0_OR_GREATER
            if (!status.HasValue && http?.StatusCode != null) status = (int)http.StatusCode.Value;
#endif
            PaymentErrorKind kind;
            if (rpc != null) kind = PaymentErrorKind.Rpc;
            else if (exception is ObjectDisposedException && operationCancelled) kind = PaymentErrorKind.Cancelled;
            else if (exception is OperationCanceledException) kind = operationCancelled ? PaymentErrorKind.Cancelled : PaymentErrorKind.Timeout;
            else if (status.HasValue) kind = PaymentErrorKind.Http;
            else if (exception is InvalidDataException || exception is ArgumentException || exception is FormatException || exception is OverflowException)
                kind = PaymentErrorKind.InvalidResponse;
            else kind = PaymentErrorKind.Transport;
            return new PaymentError
            {
                Kind = kind, Method = rpc?.Method ?? exception.Data[RpcMethodDataKey] as string ?? method,
                RpcCode = SafeRpcCode(rpc?.Code, seed), HttpStatusCode = status,
                Message = kind == PaymentErrorKind.Rpc ? "The XRPL server rejected the request." :
                    kind == PaymentErrorKind.Http ? "The HTTP request was unsuccessful." :
                    kind == PaymentErrorKind.Timeout ? "The request or confirmation deadline elapsed." :
                    kind == PaymentErrorKind.Cancelled ? "The payment operation was cancelled." :
                    kind == PaymentErrorKind.InvalidResponse ? "The server returned invalid or inconsistent data." :
                    "The server connection failed."
            };
        }

        private static string SafeRpcCode(string code, string seed)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 64 || (!string.IsNullOrEmpty(seed) && code.IndexOf(seed, StringComparison.Ordinal) >= 0)) return null;
            foreach (char c in code)
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return null;
            return code;
        }
    }
}
