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
9. **URI dependency advisory (discovered in final online audit).** Override the
   transitive System.Private.Uri 4.3.0 with Microsoft's patched 4.3.2. Acceptance:
   restore/build and the complete suite pass on both targets; a fresh package-only
   consumer audit with NuGetAuditMode=all includes runtime-specific graphs and
   returns no advisories or audit-fetch warnings. Package-only queries, precompiled
   legacy consumers and Web Forms must pass with the final dependency set. The
   original warnings were on the consumer's net8.0/win-x86 dependency graph; they
   do not demonstrate an exploit on the installed .NET 8/.NET Framework runtime.

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
2. Pending state: the original failure and expiry cases failed before the fix.
   Seven new cases now pass, including overlapping stale/final lookups and a
   receipt restored during preparation; full suite 126 per runtime (252 total).
3. Response validation: 24 of the initial 28 new cases reproduced defects; four
   additional proof-field cases failed in the follow-up. All 33 new cases and the
   full suite now pass (159 per runtime, 318 total). The culture regression gate
   caught a numeric-format issue during implementation; it was corrected without
   changing the accepted expectation. Signed receipt comparison covers API v2's
   DeliverMax alias and v1/v2 metadata; malformed post-submit data retains receipt.
4. Error diagnostics: nine initial cases failed without structured diagnostics;
   all twelve new error cases now pass. Full suite 171 per runtime (342 total),
   including HTTP 400/429/503 on Framework, request/confirmation deadlines,
   actual RPC method attribution, seed redaction and existing API compatibility.
5. Watch cancellation: four of five new cases failed before the fix. All now pass,
   including disposal, already-cancelled startup, restart/hash deduplication and
   checkpoint failure. Full suite 176 per runtime (352 total).
6. Address bounds: the oversized Base58 timing case failed on both runtimes before
   the guard. All fifteen new cases pass, including 256 deterministic valid
   payloads, special addresses, large Unicode/surrogates, checksums and no-I/O
   rejection. Full suite 191 per runtime (382 total).
7. History range: default-range pruning and legacy-token upgrade cases failed
   before the fix. All five new cases now pass, with original explicit bounds and
   binding checks intact. Full suite 196 per runtime (392 total).
8. Continuation/checkpoint validation: 25 of 46 initial cases failed before the
   fix. All 48 new cases now pass, including strict field types, duplicate keys,
   UTF-8, opaque marker types/date-like strings and request snapshotting. The
   additional invalid-UTF-8 response case also failed before its correction.
   Full suite 244 per runtime (488 total). Existing checkpoint/restart and old
   version-1 token tests remain unchanged and pass.
9. URI advisory: the fresh online consumer restore initially reported three
   advisories for 4.3.0 in its win-x86 graph. After the 4.3.2 override, both fresh
   package-only consumers build with zero warnings/errors and online audit has no
   entries. Full suite remains 244 per runtime (488 total); patched IIS and
   unchanged precompiled consumers also pass.

Final integration gate passed with the limits in
`Ripple.Integration/quality-verification.json`. Saved Testnet history was pruned
on the original endpoint; both runtime paths passed read-only recovery and actual
WebSocket replay on an explicitly configured official history server. Original
receipts and endpoint binding were preserved. The historical consumer-hash file
predated the existing binaries and was left intact; the same current precompiled
consumer hashes were checked before/after running both published 1.0.1 and the new
DLL. No user deployment/database acceptance or actual Framework 4.7.2 runtime
verification is claimed.
