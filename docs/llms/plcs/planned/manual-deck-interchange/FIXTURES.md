# Manual Deck Interchange Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Scenario | Expected result |
| --- | --- | --- |
| XCHG-FIX-001 | Fully populated local deck | Native JSON exact round trip. |
| XCHG-FIX-002 | Name-only and split/Unicode card names | No identity invention or text corruption. |
| XCHG-FIX-003 | Main, commander, sideboard, maybeboard, excluded zones | Provider sections and preservation report are exact. |
| XCHG-FIX-004 | Three categories with one primary | Moxfield emits three local tags; Archidekt emits verified primary plus companion assignments. |
| XCHG-FIX-005 | Same card in multiple printings/finishes | Printing hints emitted; unsupported finish detail reported. |
| XCHG-FIX-006 | Malformed lines around valid cards | Bounded one-based diagnostics and partial status. |
| XCHG-FIX-007 | 5 MiB and 10,000-entry boundaries | Limits accept exact bound and reject overflow. |
| XCHG-FIX-008 | Preview content changed before create | Fingerprint conflict and no deck. |
| XCHG-FIX-009 | Partial preview create with omitted/default `allowPartial` | Rejected with diagnostics and no deck. |
| XCHG-FIX-010 | Partial preview create with `allowPartial=true` and matching fingerprint | Valid entries create atomically; diagnostics remain in the result. |
| XCHG-FIX-011 | 200/201 diagnostics and 512/513-character message | Exact limits are retained; overflow is truncated deterministically with omitted count. |
| XCHG-FIX-012 | 16/17 artifacts and 20 MiB boundary | Exact bound succeeds; overflow returns unsupported-size and no partial bundle. |

## Artifact Expectations

| Bundle | Required artifacts |
| --- | --- |
| Native | `deck.mtg-mcp.json`, `preservation.json` |
| Generic | `deck.txt`, `deck.mtg-mcp.json`, `preservation.json` |
| Archidekt | `deck.archidekt.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |
| Moxfield | `deck.moxfield.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `deck_import_formats`, `deck_import_preview`, `deck_export_formats`, `deck_export_bundle` | Visible | Visible | Visible |
| `deck_import_create` | Hidden | Visible | Visible |

## Manual Acceptance

| Provider | Procedure | Pass condition |
| --- | --- | --- |
| Archidekt | Paste artifact into a disposable deck's Import Cards UI. | Quantities, names, supported printings, and documented primary categories match; companion-only rows are not claimed applied. |
| Moxfield | Paste artifact into a disposable deck's Bulk Edit UI. | Boards, printings, and all local `#Tag` assignments match; global tags appear only when requested. |

Manual checks record provider, observed UTC, UI flow/path, artifact checksums,
result, notes, and revalidation reason. They do not use automated APIs or retain
user deck data. The acceptance is repeated during implementation and before
stable cutover.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| XCHG-001 | XCHG-FIX-001 and native JSON golden equality. |
| XCHG-002, XCHG-003 | XCHG-FIX-006, XCHG-FIX-008, preview schema, and atomic create tests. |
| XCHG-004 | Architecture and network-spy tests for every import format. |
| XCHG-005, XCHG-011 | Artifact expectations, checksum snapshots, and preservation-report assertions. |
| XCHG-006, XCHG-013 | XCHG-FIX-002, XCHG-FIX-003, XCHG-FIX-006, and generic golden text. |
| XCHG-007, XCHG-008 | XCHG-FIX-003 through XCHG-FIX-005 plus Archidekt manual acceptance. |
| XCHG-009, XCHG-010 | XCHG-FIX-004 plus Moxfield golden syntax and manual acceptance. |
| XCHG-012 | XCHG-FIX-007 and cancellation tests. |
| XCHG-014 | MCP surface matrix and process E2E tests. |
| XCHG-015 | XCHG-FIX-006, XCHG-FIX-009, and XCHG-FIX-010. |
| XCHG-016 | XCHG-FIX-011 and XCHG-FIX-012. |
| XCHG-017 | Dated manual-acceptance metadata schema and pre-cutover revalidation record. |
