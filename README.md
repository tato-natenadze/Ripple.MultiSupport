# Ripple.MultiSupport

A C# library for XRPL wallet generation, address validation, offline signing,
XRP payments, history and incoming-payment monitoring.
Targets **.NET Standard 2.0** (including ASP.NET Web Forms on .NET Framework 4.7.2+)
and **.NET 8**.

[NuGet](https://www.nuget.org/packages/Ripple.MultiSupport) ·
[XRPL documentation](https://xrpl.org/docs)

## Installation

```powershell
dotnet add package Ripple.MultiSupport
```

**Release status:** NuGet currently provides **1.0.1** with the `RippleClient` API.
This repository contains **1.1.0** source, including `XrplClient`; build from source
to use the new APIs until that version is published.

## API

| Client | Method | Purpose |
| --- | --- | --- |
| `RippleClient` | `GenerateAddress()` | Create a wallet with its address and seed |
| | `ValidateAddress(address)` | Check classic-address encoding and checksum |
| | `SignTransaction(transaction)` | Sign locally; return a hexadecimal `tx_blob` |
| `XrplClient` | `GetAccountAsync(address)` | Read validated balance, sequence and reserve estimates |
| | `SendXrpAsync(seed, request)` | Sign and submit a direct XRP payment; wait for its result |
| | `GetTransactionAsync(hash)` / `GetTransactionAsync(receipt)` | Query status; a saved receipt also allows proven expiry checks |
| | `GetPaymentsAsync(address, request)` | Read validated incoming/outgoing XRP payment history |
| | `WatchIncomingPaymentsAsync(address, callback, options, token)` | Monitor successful incoming XRP payments with checkpoint recovery |

Existing `RippleClient` calls remain available. All async operations accept a
`CancellationToken`. The payment client uses the account's master seed.

## Send XRP

In an async method, supply your funded Testnet account's `seed`, `destination`,
`cancellationToken`, and a durable `persistReceipt` callback of type
`Func<PaymentReceipt, CancellationToken, Task>` from your application:

```csharp
using System;
using Ripple;

using (var client = new XrplClient(new XrplClientOptions
{
    RpcEndpoint = new Uri("https://testnet.xrpl-labs.com/"),
    WebSocketEndpoint = new Uri("wss://testnet.xrpl-labs.com/")
}))
{
    var result = await client.SendXrpAsync(seed, new XrpPaymentRequest
    {
        Destination = destination,
        AmountXrp = 1m,
        OnPreparedAsync = persistReceipt
    }, cancellationToken);
}
```

The example uses **Testnet**. Configure matching HTTP/WebSocket endpoints for
[your network](https://xrpl.org/docs/tutorials/public-servers).
Keep one shared client for the application's lifetime and dispose it at shutdown;
multiple processes/workers need host-side coordination for the same sender.

## Payment handling

- Keep seeds secret. `SignTransaction` takes an object containing `Seed` and signs
  locally. Its `Amount`/`Fee` fields use integer strings in drops
  (**1 XRP = 1,000,000 drops**). `AmountXrp`/`MaxFeeXrp` use decimal XRP, with at most
  six decimal places and no silent rounding. `DestinationTag = 0` is valid;
  optional `Memo` text is limited to 1000 UTF-8 bytes.
- Persist the JSON-serializable `PaymentReceipt` in `OnPreparedAsync`, **before
  broadcast**, linked to your unique business operation. It contains no seed.
  If persistence fails, nothing is submitted. After restart, reconcile all saved
  unresolved receipts before sending more payments.
- Only `Succeeded` is a validated success. `Failed` can still charge a fee.
  `Expired` requires complete ledger-history proof. `Pending`/`Unknown` require
  recovery with `GetTransactionAsync(savedReceipt, token)` using the same RPC
  endpoint before creating a replacement payment. A hash-only query cannot prove
  expiry; the initial submission code is provisional.
- Validation/preflight errors throw. Once submission begins, transport failures,
  cancellation or timeout retain the receipt with an indeterminate result.
  Inspect `Status`, `Diagnostic` and `Error` (`Kind`, `Method`, `RpcCode`,
  `HttpStatusCode`) before deciding what to do next.
- For history, follow `ContinuationToken` and check `RequestedRangeAvailable`.
  Monitoring delivers successful, validated incoming XRP payments **at least
  once**. Credit `DeliveredAmountXrp`; atomically deduplicate by network/hash with
  the balance update. Persist `SaveCheckpointAsync` checkpoints and resume with
  `Checkpoint` after restart. Without `Checkpoint`/`FromLedger`, monitoring starts
  with new ledgers. Callback failures stop monitoring and may cause replay.

## ASP.NET Web Forms

Use .NET Framework **4.7.2+**, an async page (`Async="true"`) and
`RegisterAsyncTask(new PageAsyncTask(...))`. Use `await` rather than
`.Result`/`.Wait()`. Run monitoring under a host-managed lifetime; IIS recycling or
shutdown stops it, so resume from a persisted checkpoint. Web Forms uses the
`netstandard2.0` assembly; .NET 8 apps use the `net8.0` assembly.

## Build and test

With the .NET 8 SDK or a newer compatible SDK, run from the repository root:

```powershell
dotnet build Ripple.csproj --configuration Release -p:GeneratePackageOnBuild=false
dotnet test Ripple.sln --configuration Release -p:GeneratePackageOnBuild=false
```

The full test suite requires Windows/.NET Framework for its `net472` target.
To run only .NET 8, add `--framework net8.0` to the test command.
Tests cover API compatibility, signed bytes, amounts, payment recovery and monitoring.

## License

[Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for third-party attribution;
both files are included in the NuGet package.
