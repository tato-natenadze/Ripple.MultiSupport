# Ripple.MultiSupport

`Ripple.MultiSupport` is a lightweight .NET library for XRPL-compatible transaction serialization, signing, create wallet and validate address.
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

The .NET 8 dependency graph overrides the three vulnerable transitive package
versions found during the audit, retaining the existing public crypto dependencies.

## Tests

On Windows, run the full suite against .NET 8 and .NET Framework 4.7.2:

```powershell
dotnet test Ripple.sln --configuration Release -p:GeneratePackageOnBuild=false
```

To run only the .NET 8 tests:

```powershell
dotnet test tests/Ripple.Tests/Ripple.Tests.csproj --framework net8.0 --configuration Release -p:GeneratePackageOnBuild=false
```

Tests cover the public entry points, official secp256k1/Ed25519 vectors, public API
compatibility, legacy transaction bytes, amount cultures/bounds and malformed
inputs. They do not submit transactions or use funded accounts.
