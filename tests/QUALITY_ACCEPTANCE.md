# Quality fixes: acceptance criteria

Protected reference: commit `1eee9f5` before these fixes. Existing 1.0.1 API and
signed-transaction fixtures remain immutable. The two `v1.1.0-public-api-*`
fixtures were captured from that reference, before production edits, and protect
the new API as well. Additions are allowed; removing/changing existing members is
not. Do not regenerate expected values to accept a regression.

## Required for every fix

- Demonstrate the defect with a regression test before changing production code.
- Pass that test after the fix on .NET 8 and on the .NET Standard 2.0 library
  consumed by net472. Record that the installed Framework runtime is 4.8.
- Pass the entire suite on both paths, including legacy API, exact signatures,
  account/payment/history, parallel sends, cancellation, recovery, reconnect,
  callback failure and checkpoint tests. Any failure leaves the fix incomplete.
- Inspect the diff for only the requested behavior. Keep existing commits intact
  and make one new commit for each numbered fix after its checks pass.
- Retain unknown-payment protection: no blind retries, false success, false
  failure or checkpoint beyond unacknowledged work.

## Numbered acceptance gates

1. **Watch ordering.** Sort each history page by ledger/index before callbacks.
   Accept shuffled pages, process each hash once, preserve order across pages,
   reject a backwards page/missing index, and preserve the checkpoint and original
   callback exception on failure.
2. **Final payment state.** Remove only the matching finalized pending receipt.
   A confirmed payment followed by unavailable old history must not block a new
   send. Unknown/restored receipts still block; a concurrent old lookup cannot
   erase a newer pending payment. Parallel sequences remain correct.
3. **Response validation.** Reject missing/mistyped transaction fields and
   inconsistent payment data. A response must match the saved signed payment
   before finalizing it. Valid v1/v2 responses, non-XRP transaction queries and
   failed transactions continue to work. Malformed data never produces success
   or an incoming-payment callback; after broadcast it preserves the receipt and
   an indeterminate status.
4. **Error diagnostics.** Preserve error kind, RPC method/code and available HTTP
   status in an additive result model, while keeping the existing diagnostic.
   Distinguish RPC, transport, timeout/cancellation and invalid response failures.
   Do not expose requests, seeds/private keys or arbitrary server text in the
   structured diagnostic. Do not turn uncertain submission into final failure.
5. **Watch cancellation.** Observe cancellation/disposal before starting another
   payment/checkpoint callback. A cancellation between payments leaves no advanced
   checkpoint; restart replays the unfinished range. Existing callback exception,
   shutdown and reconnect behavior must continue to pass.
6. **Address validation bounds.** Reject impossible classic-address lengths before
   Base58 decoding. Cover very large ASCII/Unicode inputs, boundaries, valid
   secp256k1/Ed25519/special addresses and checksum failures. Invalid inputs perform
   no network I/O; existing valid addresses remain accepted.
7. **History range.** Pin the first available lower bound for a default history
   query in its continuation token. Preserve explicit ranges and server/account
   binding; expose missing history instead of silently claiming completeness.
   Existing version-1 tokens must still be accepted.
8. **Continuation/checkpoint validation.** Validate the whole payload before I/O.
   Malformed base64/JSON, incorrect field types, missing fields, wrong binding,
   unsupported version and invalid ranges produce ArgumentException consistently.
   Valid current and previously issued tokens/checkpoints continue to work.

## Final integration gate

After all numbered fixes pass, rebuild and run the complete suite, a real async
Web Forms page under isolated IIS Express, package-only .NET 8/net472 consumers,
the existing precompiled legacy consumers and the dependency advisory audit.
Use saved Testnet receipts for read-only account/transaction/history/WebSocket
verification; no new funded transactions are needed. Check that test servers are
stopped and the working tree is clean after the final commits. Report actual
automated/integration evidence separately from the user's deployment/database,
which has not been verified by these checks. Newly discovered in-scope defects
must be recorded and resolved or explicitly remain open; passing this list is
not a claim that every possible runtime condition has been tested.

## Execution record

1. Watch ordering: three new cases failed before the fix; all four ordering cases
   and the full suite passed afterward (119 per runtime, 238 total). Legacy and
   pre-review API checks passed. Integration gate remains pending until all fixes.
