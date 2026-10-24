# Ripple.MultiSupport

`Ripple.MultiSupport` is a .NET library for XRPL wallet generation, address validation,
offline transaction signing, and direct XRP payments, history and incoming-payment monitoring.
It targets **.NET Standard 2.0** and **.NET 8**, so it works on old .NET Framework (e.g., 4.7.2 Web Forms) and modern .NET.

## Install
```powershell
dotnet add package Ripple.MultiSupport
```

## Public entry points

The existing API remains available:

```csharp
using Ripple;

var wallet = RippleClient.GenerateAddress();
bool valid = RippleClient.ValidateAddress(wallet.Address);

string txBlob = RippleClient.SignTransaction(new
{
    TransactionType = "Payment",
    Account = wallet.Address,
    Destination = "DESTINATION_CLASSIC_ADDRESS",
    Amount = "1000000",
    Fee = "12",
    Sequence = 1,
    Seed = wallet.Seed
});
```

Replace the destination and obtain the current fee and account sequence from your
XRPL server before signing. `Amount` and `Fee` are integer strings in drops
(1 XRP = 1,000,000 drops). Keep `Seed` secret; do not log it or send it to a server.
The example only signs locally; submission and ledger confirmation are handled
by the consuming application.

`GenerateAddress()` returns `AddressData` with `Address` and `Seed` properties.
`ValidateAddress()` checks a classic address's encoding and checksum; it does not
check account activation or balance. Invalid and null addresses return `false`.
`SignTransaction()` accepts a transaction object containing `Seed`, returns its
signed hexadecimal `tx_blob`, and leaves the caller's object unchanged.

## XRP payment client (1.1.0)

The separate `XrplClient` adds the same five operations on **both** target frameworks.
Existing `RippleClient` calls continue to work without creating a network client.

| Method | Result |
| --- | --- |
| `GetAccountAsync(address)` | Account existence, validated ledger, XRP balance, reserve/spendable estimates and destination requirements |
| `SendXrpAsync(seed, request)` | Prepare, sign locally, submit once and wait for a validated result |
| `GetTransactionAsync(hash)` | Transaction data and status; a receipt overload also checks for proven expiry |
| `GetPaymentsAsync(address, request)` | Validated incoming/outgoing XRP Payment history, with continuation tokens |
| `WatchIncomingPaymentsAsync(address, callback, options, token)` | Successful, validated incoming XRP Payments with replay/checkpoint support |

All network operations accept a `CancellationToken`. The client constructor performs
no network I/O. Create a long-lived client and dispose it at application shutdown.
The one-argument constructor owns its HTTP client. An injected `HttpClient` remains
owned by the caller. Client options and payment requests are copied when used.

### Configuration and .NET 8

Choose matching RPC and WebSocket endpoints for your network. This example uses
**Testnet**, with valueless test XRP. Production servers are a separate configuration;
the library does not silently switch networks or servers.

```csharp
using System;
using Ripple;

var options = new XrplClientOptions
{
    RpcEndpoint = new Uri("https://testnet.xrpl-labs.com/"),
    WebSocketEndpoint = new Uri("wss://testnet.xrpl-labs.com/"),
    MaxFeeXrp = 0.0001m,
    RequestTimeout = TimeSpan.FromSeconds(15),
    ConfirmationTimeout = TimeSpan.FromSeconds(60),
    LastLedgerOffset = 4
};

using (var client = new XrplClient(options))
{
    var account = await client.GetAccountAsync("YOUR_CLASSIC_ADDRESS");
    Console.WriteLine(account.BalanceXrp);
}
```

`EstimatedSpendableXrp` is an estimate before fees and pending transactions, based
on the reported reserve and OwnerCount. It is not a reservation of funds. The
network remains authoritative, including special reserve/sponsorship rules.

### Sending and recovering a payment

`AmountXrp` and `MaxFeeXrp` are decimal XRP, unlike the legacy signer's drops fields.
Amounts must be positive, have at most six decimal places, and are never silently
rounded. A tag of `0` is valid and differs from an omitted tag. Memo is optional
UTF-8 text, limited to 1000 bytes. This high-level method sends direct XRP using
the account's master seed; existing offline signing remains available for other
transaction/key arrangements.

The following helper is usable in either framework. `persistReceipt` is your
application's durable storage callback, associated with your own payment/order ID.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Ripple;

public static class Payments
{
    public static Task<PaymentResult> SendAsync(
        XrplClient client, string seed, string destination,
        decimal amountXrp, uint? destinationTag,
        Func<PaymentReceipt, CancellationToken, Task> persistReceipt,
        CancellationToken cancellationToken)
    {
        if (persistReceipt == null) throw new ArgumentNullException("persistReceipt");
        return client.SendXrpAsync(seed, new XrpPaymentRequest
        {
            Destination = destination,
            AmountXrp = amountXrp,
            DestinationTag = destinationTag,
            OnPreparedAsync = persistReceipt
        }, cancellationToken);
    }
}
```

`OnPreparedAsync` runs **before broadcast**. If it fails, the client does not submit.
`PaymentReceipt` is JSON serializable and contains the signed blob, hash, sequence,
ledger bounds and RPC endpoint; it contains no Seed. The library creates no receipt
files or database records automatically. Without a persistence callback, recovery
data is only available in the current process/result.

Inspect `PaymentResult.Status`:

- `Succeeded`: a validated transaction has `tesSUCCESS` metadata.
- `Failed`: a validated transaction has a failure code. Its fee can still be charged.
- `Expired`: the last allowed ledger has been validated **and** the server confirms
  it searched the entire receipt range without finding the transaction.
- `Pending` / `Unknown`: no final outcome has been established. A transport error,
  cancellation after submission begins, timeout or missing history is not proof of failure.

`SubmissionResultCode` is provisional. Even `tesSUCCESS` from `submit` is not final.
Once submission begins, cancellation/network failures return a result with the
receipt and a diagnostic. Cancellation before submission throws normally.

`PaymentResult.Error` adds structured diagnostics when submission/confirmation is
indeterminate: `Kind` distinguishes RPC, HTTP, transport, timeout, cancellation
and invalid response errors; `Method`, `RpcCode` and nullable integer
`HttpStatusCode` provide the available details. `Message` is a safe library
description, without raw server messages, request bodies or signing secrets.
Inspect the receipt's final status before retrying, regardless of the error kind.

After a timeout or application restart, load the saved receipt and call:

```csharp
TransactionInfo state = await client.GetTransactionAsync(savedReceipt, cancellationToken);
```

Use the same configured RPC endpoint as the receipt. The receipt overload performs
no submission. A hash-only query cannot prove expiry. Reconcile **all unresolved
saved receipts before accepting new outgoing work** after restart.

One shared client serializes sends from the same account and blocks replacements
while a previous receipt remains unresolved. It also refuses to sign over visible
external/pending sequences. This is per-client coordination, not a distributed lock:
multiple clients, IIS workers or servers need a single sender/host-side coordinator.
Associate receipts with a unique business operation in your database so a repeated
HTTP request cannot create a second payment after the first one has completed.

### History and incoming payments

```csharp
var page = await client.GetPaymentsAsync(address, cancellationToken: cancellationToken);
foreach (var payment in page.Payments)
{
    // Includes successful and failed XRP Payments; inspect Status and Destination.
}
if (page.ContinuationToken != null)
{
    page = await client.GetPaymentsAsync(address,
        new PaymentHistoryRequest { ContinuationToken = page.ContinuationToken },
        cancellationToken);
}
```

Continuation tokens pin the ledger range and are bound to the account/server.
Without an explicit lower bound, the first page's earliest available ledger is
pinned for continuation. Older version-1 tokens containing `from: -1` remain
accepted and pin the earliest available ledger when their next page is read.
When requesting an explicit range, inspect `RequestedRangeAvailable`; a server
may not have the full history. Pages are requested oldest first; each history
page preserves server order. Incoming monitoring sorts each page by ledger/index.

```csharp
await client.WatchIncomingPaymentsAsync(address,
    ApplyIncomingPaymentOnceAsync,
    new PaymentWatchOptions
    {
        Checkpoint = savedCheckpoint,
        SaveCheckpointAsync = SaveCheckpointAsync
    }, stoppingToken);
```

Here `ApplyIncomingPaymentOnceAsync(TransactionInfo, CancellationToken)` and
`SaveCheckpointAsync(string, CancellationToken)` are your host's persistence
callbacks. With no checkpoint/FromLedger, monitoring starts after the validated
ledger observed at invocation. Use `FromLedger` to explicitly replay older data.

Monitoring uses WebSocket notifications to wake authoritative HTTP history queries.
It retries transient transport errors, including the initial ledger request, and
temporary RPC errors (`tooBusy`, `noNetwork`, `noCurrent`, `noClosed`,
`failedToForward`), then replays the missed range using the configured poll delay.
Only successful, validated XRP Payments to the watched destination are delivered;
the amount comes from metadata `DeliveredAmountXrp`, never merely the requested
amount. Missing delivered amounts/history stop processing for reconciliation.

Callbacks run sequentially in ledger order. A checkpoint is advanced only after
all callbacks in its ledger batch succeed. Delivery is **at least once**: after
a crash or callback/checkpoint failure, some events can be replayed. Atomically
record a unique `(network, transaction hash)` and credit your user's balance in
the same database transaction. Persist checkpoints outside process memory.
Callback failures stop the watch and are propagated. Cancelling/disposal stops it.
Cancellation is checked before starting each payment/checkpoint callback. An
already-running callback is awaited and must handle cancellation cooperatively.

### ASP.NET Web Forms first

Web Forms consumes the package's `netstandard2.0` assembly. Targeting .NET Framework
4.7.2 or later is recommended. It does not require installing the .NET 8 runtime.
The same payment API is supplied by the separate `net8.0` assembly for .NET 8 apps.

Create one client for the application's lifetime, for example in `Global.asax`
`Application_Start`, using the configuration above, and store it in `Application`:

```csharp
Application["XrplClient"] = new XrplClient(options);
```

Dispose that instance in `Application_End`. An async Web Forms page can read an
account using the normal ASP.NET task mechanism:

```aspx
<%@ Page Language="C#" Async="true" %>
<%@ Import Namespace="Ripple" %>
<%@ Import Namespace="System.Threading.Tasks" %>
<asp:Label ID="BalanceText" runat="server" />
<script runat="server">
protected void Page_Load(object sender, EventArgs e)
{
    RegisterAsyncTask(new PageAsyncTask(LoadAccountAsync));
}
private async Task LoadAccountAsync()
{
    var client = (XrplClient)Application["XrplClient"];
    var account = await client.GetAccountAsync("YOUR_CLASSIC_ADDRESS");
    BalanceText.Text = account.BalanceXrp.ToString(
        System.Globalization.CultureInfo.InvariantCulture);
}
</script>
```

Use `await` for sending as well; do not block request threads with `.Result`/`.Wait()`.
Direct `HttpClient` injection in a .NET Framework project requires its framework
`System.Net.Http` reference. The simple client constructor avoids that requirement
in your application code. NuGet may supply the usual assembly binding redirects.

`WatchIncomingPaymentsAsync` is a long-running task. Run it under a host-managed
lifetime rather than starting one watcher per page request. IIS recycle/idle/shutdown
can stop the process; the library cannot monitor while the host is stopped. On
restart, resume from the saved checkpoint to catch up. An always-running monitoring
requirement needs a suitable host/deployment configuration. Callbacks should use
their own database resources and must not depend on a page's `HttpContext`.

## Compatibility and fixes

The fixes keep the existing public types, method signatures and target
frameworks. The existing public `BrokerFee` field/name remains available alongside
the protocol's `NFTokenBrokerFee` name. Assembly identity is unchanged.

Fixes cover culture-independent amount arithmetic, XRP amount bounds, malformed
IOU values, fixed-width hash lengths, binary enum decoding, null address handling,
NFT offer acceptance, ticket creation/use and previously unimplemented `Bits`
conversions. Invalid inputs that previously produced malformed or changed data
are now rejected. Supported valid legacy transactions are checked against the
exact signed bytes captured before these fixes.

Both target paths pin patched HTTP/regex/URI transitive package versions. The .NET 8
path also pins the patched ASN.1 dependency, retaining the existing public crypto dependencies.

## Tests

On Windows, run the full suite against .NET 8 and .NET Framework 4.7.2:

```powershell
dotnet test Ripple.sln --configuration Release -p:GeneratePackageOnBuild=false
```

To run only the .NET 8 tests:

```powershell
dotnet test tests/Ripple.Tests/Ripple.Tests.csproj --framework net8.0 --configuration Release -p:GeneratePackageOnBuild=false
```

The normal suite covers the public entry points, official secp256k1/Ed25519 vectors, public API
compatibility, legacy transaction bytes, amount cultures/bounds and malformed
inputs, network parsing, fees/reserves, sequence conflicts, uncertain submissions,
receipt recovery, callback failures and monitoring catch-up. It uses deterministic
network fixtures and does not submit transactions or use funded accounts.

Explicit live Testnet checks are separate from the solution's normal test run:

```powershell
dotnet run --project tests/Ripple.Integration/Ripple.Integration.csproj --framework net8.0 --configuration Release -p:GeneratePackageOnBuild=false -- --testnet "$env:TEMP\Ripple.Testnet8"

dotnet build tests/Ripple.Integration/Ripple.Integration.csproj --configuration Release -p:GeneratePackageOnBuild=false
& .\tests\Ripple.Integration\bin\Release\net472\Ripple.Integration.exe --testnet "$env:TEMP\Ripple.TestnetFramework"
```

Each explicit run creates new Testnet-only wallets, obtains valueless Testnet XRP,
makes two Testnet payments and verifies all five methods. It saves secret-free
receipts/checkpoints in the requested directory; it accepts no production seed.
Use `--verify-testnet` instead of `--testnet` to recheck a previous run's receipts,
history and live WebSocket replay without funding or submitting anything.

To exercise an actual Web Forms async page under IIS Express with deterministic
transport fixtures, after building the normal suite:

```powershell
.\tests\Ripple.Integration\Run-WebFormsSmoke.ps1 -TestOutput .\tests\Ripple.Tests\bin\Release\net472 -OutputDirectory "$env:TEMP\Ripple.WebFormsSmoke"
```

The script isolates the IIS Express configuration/site, uses a temporary localhost
port, and stops its own server in `finally`. It does not edit your IIS configuration.
Actual verification evidence and limitations are recorded in
`tests/Ripple.Integration/verification.json`. The net472 runs used the installed
.NET Framework 4.8 runtime; the user's own deployment/database still needs integration testing.

Protocol references: [public API](https://xrpl.org/docs/references/http-websocket-apis/public-api-methods),
[reliable submission](https://xrpl.org/docs/concepts/transactions/reliable-transaction-submission),
[Payment metadata](https://xrpl.org/docs/references/protocol/transactions/types/payment),
[public servers](https://xrpl.org/docs/tutorials/public-servers).
