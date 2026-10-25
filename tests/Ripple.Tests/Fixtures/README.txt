legacy-transactions.json contains seven signed transactions captured from the
unmodified 1.0.1 source before fixes. These are compatibility references, not
evidence that the transactions have been submitted to a network.

legacy-public-api.txt was captured from the original net8.0 assembly on .NET 8.
legacy-public-api-net472.txt was captured from a preserved copy of the original
netstandard2.0 assembly on .NET Framework 4.7.2. Separate snapshots are necessary
because reflection attribute names and inherited framework interfaces vary by
runtime. Existing entries must remain; new public members may be added.

Do not regenerate these files to accommodate a regression.

The test keys are public XRPL fixtures. Never use them for funded accounts.
Official key/signature reference:
https://github.com/XRPLF/xrpl.js/blob/main/packages/ripple-keypairs/test/fixtures/api.json

Protocol references for regression tests:
https://xrpl.org/docs/references/protocol/transactions/types/nftokenacceptoffer
https://xrpl.org/docs/references/protocol/transactions/types/ticketcreate
https://xrpl.org/docs/references/protocol/binary-format
https://xrpl.org/docs/references/protocol/data-types/currency-formats

account-delete-transactions.json contains independent AccountDelete signatures
from the official xrpl.js SDK 5.3.0 using the same public test keys above. It
covers secp256k1, Ed25519, absent/zero/maximum destination tags and type 21.
Keep these expected blobs fixed; never regenerate them to conceal a regression.
