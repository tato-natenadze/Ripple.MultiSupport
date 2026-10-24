using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ripple;

namespace Ripple.Integration
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length != 2 || (args[0] != "--testnet" && args[0] != "--verify-testnet"))
            {
                Console.Error.WriteLine("--testnet <receipt-directory>: funds a NEW Testnet wallet and makes two Testnet payments. --verify-testnet <receipt-directory>: rechecks an existing run without submitting. Never uses Mainnet.");
                return 2;
            }
            try
            {
                Directory.CreateDirectory(args[1]);
                using (var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4)))
                {
                    Console.WriteLine(args[0] == "--testnet" ? await Testnet.RunAsync(args[1], deadline.Token) :
                        await Testnet.VerifyAsync(args[1], deadline.Token));
                    return 0;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
        }
    }

    public static class Testnet
    {
        // Fixed official-listed Testnet servers. The runner accepts no private seed or production endpoint.
        private static readonly Uri Rpc = new Uri("https://testnet.xrpl-labs.com/");
        private static readonly Uri Ws = new Uri("wss://testnet.xrpl-labs.com/");

        public static async Task<string> RunAsync(string receiptDirectory, CancellationToken token)
        {
            var sender = RippleClient.GenerateAddress();
            var receiver = RippleClient.GenerateAddress();
            if (!RippleClient.ValidateAddress(sender.Address) || !RippleClient.ValidateAddress(receiver.Address)) throw new Exception("Wallet validation failed.");
            using (var faucet = new HttpClient())
            using (var response = await faucet.PostAsync("https://faucet.altnet.rippletest.net/accounts",
                new StringContent(new JObject { ["destination"] = sender.Address }.ToString(), Encoding.UTF8, "application/json"), token).ConfigureAwait(false))
                response.EnsureSuccessStatusCode();
            using (var client = new XrplClient(new XrplClientOptions { RpcEndpoint = Rpc, WebSocketEndpoint = Ws,
                RequestTimeout = TimeSpan.FromSeconds(30), ConfirmationTimeout = TimeSpan.FromSeconds(90), LastLedgerOffset = 12 }))
            {
                AccountInfo account = null;
                for (int i = 0; i < 30; i++)
                {
                    account = await client.GetAccountAsync(sender.Address, token).ConfigureAwait(false);
                    if (account.Exists) break;
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                if (!account.Exists) throw new Exception("Faucet funding was not validated.");
                var activated = await client.SendXrpAsync(sender.Seed, new XrpPaymentRequest { Destination = receiver.Address, AmountXrp = 2m,
                    OnPreparedAsync = Persist(receiptDirectory, "activation.json") }, token).ConfigureAwait(false);
                RequireSuccess(activated);
                using (var watchCancel = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var received = new TaskCompletionSource<TransactionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task watch = client.WatchIncomingPaymentsAsync(receiver.Address, (payment, ct) => {
                        if (payment.DestinationTag == 0 && payment.DeliveredAmountXrp == 0.25m) received.TrySetResult(payment);
                        return Task.CompletedTask;
                    }, new PaymentWatchOptions { FromLedger = checked(activated.Transaction.LedgerIndex.Value + 1),
                        SaveCheckpointAsync = (checkpoint, ct) => {
                            File.WriteAllText(Path.Combine(receiptDirectory, "checkpoint.txt"), checkpoint);
                            return Task.CompletedTask;
                        } }, watchCancel.Token);
                    try
                    {
                        var sent = await client.SendXrpAsync(sender.Seed, new XrpPaymentRequest { Destination = receiver.Address,
                            AmountXrp = 0.25m, DestinationTag = 0, Memo = "Ripple.MultiSupport integration",
                            OnPreparedAsync = Persist(receiptDirectory, "payment.json") }, token).ConfigureAwait(false);
                        RequireSuccess(sent);
                        Task winner = await Task.WhenAny(received.Task, watch, Task.Delay(TimeSpan.FromSeconds(60), token)).ConfigureAwait(false);
                        if (winner == watch) { await watch.ConfigureAwait(false); throw new Exception("Watch ended unexpectedly."); }
                        if (winner != received.Task) throw new Exception("Incoming payment was not observed before deadline.");
                        var incoming = await received.Task.ConfigureAwait(false);
                        if (incoming.Hash != sent.Receipt.Hash) throw new Exception("Incoming hash mismatch.");
                        var saved = JsonConvert.DeserializeObject<PaymentReceipt>(File.ReadAllText(Path.Combine(receiptDirectory, "payment.json")));
                        var recovered = await client.GetTransactionAsync(saved, token).ConfigureAwait(false);
                        var byHash = await client.GetTransactionAsync(saved.Hash, token).ConfigureAwait(false);
                        var history = await client.GetPaymentsAsync(receiver.Address, new PaymentHistoryRequest { FromLedger = activated.Transaction.LedgerIndex }, token).ConfigureAwait(false);
                        if (recovered.Status != TransactionStatus.Succeeded || byHash.Status != TransactionStatus.Succeeded || !history.Payments.Any(p => p.Hash == incoming.Hash))
                            throw new Exception("Query/history/recovery verification failed.");
                        return new JObject { ["runtime"] = Environment.Version.ToString(), ["sender"] = sender.Address, ["receiver"] = receiver.Address,
                            ["activation_hash"] = activated.Receipt.Hash, ["payment_hash"] = incoming.Hash, ["ledger"] = incoming.LedgerIndex,
                            ["delivered_xrp"] = incoming.DeliveredAmountXrp, ["status"] = "all five APIs passed" }.ToString(Formatting.None);
                    }
                    finally
                    {
                        watchCancel.Cancel();
                        try { await watch.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    }
                }
            }
        }

        private static Func<PaymentReceipt, CancellationToken, Task> Persist(string directory, string name) => (receipt, ct) => {
            File.WriteAllText(Path.Combine(directory, name), JsonConvert.SerializeObject(receipt)); return Task.CompletedTask;
        };

        public static async Task<string> VerifyAsync(string directory, CancellationToken token)
        {
            var receipt = JsonConvert.DeserializeObject<PaymentReceipt>(File.ReadAllText(Path.Combine(directory, "payment.json")));
            if (receipt.RpcEndpoint != Rpc) throw new Exception("Only this runner's fixed Testnet endpoint is accepted.");
            using (var client = new XrplClient(new XrplClientOptions { RpcEndpoint = Rpc, WebSocketEndpoint = Ws,
                RequestTimeout = TimeSpan.FromSeconds(30) }))
            {
                var tx = await client.GetTransactionAsync(receipt, token).ConfigureAwait(false);
                var byHash = await client.GetTransactionAsync(receipt.Hash, token).ConfigureAwait(false);
                var account = await client.GetAccountAsync(tx.Destination, token).ConfigureAwait(false);
                var page = await client.GetPaymentsAsync(tx.Destination, new PaymentHistoryRequest {
                    FromLedger = tx.LedgerIndex, ToLedger = tx.LedgerIndex }, token).ConfigureAwait(false);
                bool observed = false;
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    try
                    {
                        await client.WatchIncomingPaymentsAsync(tx.Destination, (incoming, ct) => {
                            if (incoming.Hash == receipt.Hash) { observed = true; stop.Cancel(); }
                            return Task.CompletedTask;
                        }, new PaymentWatchOptions { FromLedger = tx.LedgerIndex }, stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (observed && !token.IsCancellationRequested) { }
                }
                if (tx.Status != TransactionStatus.Succeeded || byHash.Status != TransactionStatus.Succeeded || !account.Exists ||
                    !page.RequestedRangeAvailable || !page.Payments.Any(p => p.Hash == receipt.Hash) || !observed)
                    throw new Exception("Existing Testnet payment verification failed.");
                return new JObject { ["runtime"] = Environment.Version.ToString(), ["payment_hash"] = receipt.Hash,
                    ["status"] = "installed package queries, recovery and WebSocket replay passed; no submission" }.ToString(Formatting.None);
            }
        }
        private static void RequireSuccess(PaymentResult result)
        {
            if (result.Status != TransactionStatus.Succeeded)
                throw new Exception("Payment not confirmed: " + result.Status + ", hash=" + result.Receipt.Hash + ", " + result.Diagnostic);
        }
    }
}
