using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ripple
{
    public sealed partial class XrplClient
    {
        private readonly ConcurrentDictionary<string, byte> _watches = new ConcurrentDictionary<string, byte>();
        internal Func<IXrplSocket> SocketFactory { get; set; } = () => new XrplSocket();
        private sealed class WatchCallbackException : Exception
        {
            internal WatchCallbackException(Exception inner) : base("Payment/checkpoint callback failed.", inner) { }
        }

        /// <summary>Monitor successful incoming XRP Payments. Runs until cancelled/disposed or a non-transient error.
        /// Persist checkpoints and atomically deduplicate database credits by hash for restart-safe processing.</summary>
        public async Task WatchIncomingPaymentsAsync(string address, Func<TransactionInfo, CancellationToken, Task> onPayment,
            PaymentWatchOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            ValidateClassicAddress(address, nameof(address));
            if (onPayment == null) throw new ArgumentNullException(nameof(onPayment));
            if (_options.WebSocketEndpoint == null) throw new InvalidOperationException("A WebSocket endpoint is required for monitoring.");
            options = options ?? new PaymentWatchOptions();
            uint? from = options.FromLedger;
            string saved = options.Checkpoint;
            var saveCheckpoint = options.SaveCheckpointAsync;
            if (from == 0) throw new ArgumentOutOfRangeException(nameof(options.FromLedger));
            if (saved != null && from.HasValue) throw new ArgumentException("Specify either a checkpoint or FromLedger.", nameof(options));
            if (!_watches.TryAdd(address, 0)) throw new InvalidOperationException("This client is already monitoring the account.");
            try
            {
                using (var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
                {
                    uint nextLedger = saved != null ? DecodeCheckpoint(address, saved) : from ?? checked((await GetLedgerAsync(operation.Token).ConfigureAwait(false)).Index + 1);
                    if (saved == null && saveCheckpoint != null)
                        await InvokeWatchCallback(() => saveCheckpoint(EncodeCheckpoint(address, nextLedger), operation.Token)).ConfigureAwait(false);
                    while (true)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        try
                        {
                            using (var socket = SocketFactory())
                            {
                                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                                {
                                    connect.CancelAfter(_options.RequestTimeout);
                                    await socket.ConnectAsync(_options.WebSocketEndpoint, connect.Token).ConfigureAwait(false);
                                    await socket.SendAsync(new JObject { ["id"] = "payments", ["command"] = "subscribe", ["api_version"] = 2,
                                        ["accounts"] = new JArray(address), ["streams"] = new JArray("ledger") }.ToString(Formatting.None), connect.Token).ConfigureAwait(false);
                                    JObject ack;
                                    do { ack = JObject.Parse(await socket.ReceiveAsync(connect.Token).ConfigureAwait(false)); }
                                    while ((string)ack["id"] != "payments");
                                    if ((string)ack["status"] == "error" || ack["error"] != null)
                                        throw new XrplRpcException("subscribe", (string)ack["error"] ?? "unknown", "Subscription was rejected.");
                                    if (!(ack["result"] is JObject)) throw new InvalidDataException("Invalid subscription response.");
                                }
                                // The HTTP history is authoritative. WebSocket messages wake this loop; they never credit a payment directly.
                                while (true)
                                {
                                    var ledger = await GetLedgerAsync(operation.Token).ConfigureAwait(false);
                                    while (nextLedger <= ledger.Index)
                                    {
                                        uint end = (uint)Math.Min((long)ledger.Index, (long)nextLedger + 199);
                                        await ReplayIncomingAsync(address, nextLedger, end, onPayment, operation.Token).ConfigureAwait(false);
                                        uint completed = checked(end + 1);
                                        if (saveCheckpoint != null)
                                            await InvokeWatchCallback(() => saveCheckpoint(EncodeCheckpoint(address, completed), operation.Token)).ConfigureAwait(false);
                                        nextLedger = completed;
                                    }
                                    operation.Token.ThrowIfCancellationRequested();
                                    using (var receive = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                                    {
                                        receive.CancelAfter(_options.RequestTimeout);
                                        // A closed/idle connection is reconnected, followed by catch-up from nextLedger.
                                        JObject.Parse(await socket.ReceiveAsync(receive.Token).ConfigureAwait(false));
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (!operation.IsCancellationRequested && IsWatchConnectionError(ex))
                        {
                            await Task.Delay(_options.PollInterval, operation.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (WatchCallbackException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            finally { byte ignored; _watches.TryRemove(address, out ignored); }
        }

        private async Task ReplayIncomingAsync(string address, uint from, uint to,
            Func<TransactionInfo, CancellationToken, Task> onPayment, CancellationToken token)
        {
            string continuation = null;
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var markers = new HashSet<string>(StringComparer.Ordinal);
            uint lastLedger = from, lastIndex = 0;
            do
            {
                var page = await GetPaymentsAsync(address, new PaymentHistoryRequest {
                    FromLedger = from, ToLedger = to, ContinuationToken = continuation }, token).ConfigureAwait(false);
                if (!page.RequestedRangeAvailable) throw new InvalidDataException("Server cannot supply the complete payment history required for catch-up. Use a server with the missing ledger range.");
                // account_tx orders pages, but does not guarantee the order within a page.
                var payments = new List<TransactionInfo>(page.Payments);
                foreach (var payment in payments)
                {
                    if (!payment.TransactionIndex.HasValue) throw new InvalidDataException("Payment history has no transaction index.");
                }
                payments.Sort((left, right) => {
                    int ledgerOrder = left.LedgerIndex.Value.CompareTo(right.LedgerIndex.Value);
                    return ledgerOrder != 0 ? ledgerOrder : left.TransactionIndex.Value.CompareTo(right.TransactionIndex.Value);
                });
                foreach (var payment in payments)
                {
                    uint ledger = payment.LedgerIndex.Value, index = payment.TransactionIndex.Value;
                    if (!hashes.Add(payment.Hash)) continue;
                    if (ledger < lastLedger || (ledger == lastLedger && index < lastIndex)) throw new InvalidDataException("Payment history is not in ledger order.");
                    lastLedger = ledger; lastIndex = index;
                    if (payment.Status != TransactionStatus.Succeeded || payment.Destination != address || payment.Sender == address) continue;
                    if (!payment.DeliveredAmountXrp.HasValue || payment.DeliveredAmountXrp <= 0)
                        throw new InvalidDataException("Incoming XRP payment has no usable delivered_amount; manual reconciliation is required.");
                    await InvokeWatchCallback(() => onPayment(payment, token)).ConfigureAwait(false);
                }
                continuation = page.ContinuationToken;
                if (continuation != null && !markers.Add(continuation)) throw new InvalidDataException("Server repeated a history page marker.");
            } while (continuation != null);
        }

        private static async Task InvokeWatchCallback(Func<Task> callback)
        {
            try { await callback().ConfigureAwait(false); }
            catch (Exception ex) { throw new WatchCallbackException(ex); }
        }

        private static bool IsWatchConnectionError(Exception ex) => ex is WebSocketException || ex is HttpRequestException ||
            ex is OperationCanceledException || (ex is IOException && !(ex is InvalidDataException));

        private string EncodeCheckpoint(string address, uint nextLedger) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            new JObject { ["version"] = 1, ["account"] = address, ["endpoint"] = _options.RpcEndpoint.AbsoluteUri,
                ["next_ledger"] = nextLedger }.ToString(Formatting.None)));

        private uint DecodeCheckpoint(string address, string value)
        {
            JObject checkpoint;
            try { checkpoint = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value))); }
            catch (Exception ex) when (ex is FormatException || ex is JsonException)
            { throw new ArgumentException("Invalid payment checkpoint.", nameof(value), ex); }
            if ((int?)checkpoint["version"] != 1 || (string)checkpoint["account"] != address || (string)checkpoint["endpoint"] != _options.RpcEndpoint.AbsoluteUri)
                throw new ArgumentException("Checkpoint belongs to a different account/server.", nameof(value));
            uint ledger = RequiredUInt(checkpoint, "next_ledger");
            if (ledger == 0) throw new ArgumentException("Invalid checkpoint ledger.", nameof(value));
            return ledger;
        }
    }
}
