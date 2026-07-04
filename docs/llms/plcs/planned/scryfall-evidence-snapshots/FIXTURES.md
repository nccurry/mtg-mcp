# Scryfall Evidence Snapshots Fixtures And Acceptance Matrix

## Endpoint Fixture Matrix

| ID | Request | Required cases |
| --- | --- | --- |
| SCRY-FIX-SEARCH | Search | Empty, one page, multiple pages, invalid query, too large. |
| SCRY-FIX-NAMED | Named | Exact, fuzzy, set-qualified, ambiguous/not found. |
| SCRY-FIX-ID | Card ID | Scryfall, Oracle, Multiverse, MTGO, Arena, TCGplayer, Cardmarket. |
| SCRY-FIX-COLLECTION | Collection | Mixed identifiers, observed 75-identifier limit with date/source metadata, 76 rejection, per-item not found. |
| SCRY-FIX-PRINTS | Prints | Multiple languages/sets and pagination. |
| SCRY-FIX-RULINGS | Rulings | Empty and ordered dated rulings. |
| SCRY-FIX-SETS | Sets | List and single code. |
| SCRY-FIX-CATALOG | Catalog | Valid and unknown catalog. |
| SCRY-FIX-AUTO | Autocomplete | Empty, Unicode, extras option. |
| SCRY-FIX-BULK | Bulk metadata | List and one known bulk type; no binary download. |

## Persistence Cases

| Case | Expected result |
| --- | --- |
| Unknown card field in source JSON | Field is returned unchanged after snapshot read. |
| Refresh after provider response changes | New ID/checksum; predecessor remains byte-identical. |
| Failure on page 3 | Failed status; no complete objects are readable. |
| Cancellation between pages | Canceled status and no completed snapshot. |
| Corrupt stored page | Unavailable with checksum reason. |
| Empty complete search | Successful complete snapshot with zero objects. |
| Failure/cancellation with staged pages | Staging rows are removed; bounded failed/canceled diagnostics remain; explicit retry gets a new ID. |
| Abandoned pending run on startup | Marked failed and staging rows removed; never resumed. |
| Delete without evidence-loss acknowledgement | Invalid input; complete snapshot remains unchanged. |
| Delete snapshot cited by Tagger fixture | Payload purged; immutable tombstone resolves; Tagger row remains and reports source payload unavailable. |
| Projection with `prices` and `edhrec_rank` | Fields are labeled provider-supplied price/popularity evidence, not quality. |
| Split/transform/modal DFC with root/face omissions and explicit empty fields | Ordered root/faces round-trip; known-empty and unknown remain distinct per level. |

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `scryfall_snapshot_list`, `scryfall_snapshot_get`, `scryfall_snapshot_objects`, `scryfall_snapshot_card` | Visible | Visible | Visible |
| `scryfall_snapshot_create`, `scryfall_snapshot_refresh`, `scryfall_snapshot_delete` | Hidden | Visible | Visible |

## Provider Safety Cases

- Requests start at least 125 ms apart under concurrency.
- User-Agent and Accept are present.
- 403 and 429 cause no additional request; Retry-After is recorded.
- 500/transport failures retry no more than twice.
- Pagination URL with a foreign host is rejected.
- Cached reads issue zero HTTP calls.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| SCRY-001 | Entire endpoint fixture matrix and typed request schema snapshots. |
| SCRY-002 | SCRY-FIX-SEARCH multi-page case and ordered page persistence. |
| SCRY-003 | SCRY-FIX-COLLECTION provider-boundary and mixed-result cases. |
| SCRY-004 | Exact MCP surface and forbidden random-operation scan. |
| SCRY-005, SCRY-006 | Immutable refresh and predecessor persistence cases. |
| SCRY-007, SCRY-008 | Unknown-field, raw-page, validator, and normalized-projection round trips. |
| SCRY-009 | Failure, cancellation, corruption, and empty-complete persistence cases. |
| SCRY-010, SCRY-011, SCRY-012 | Fake-clock, header, stop, retry, and cancellation provider-safety cases. |
| SCRY-013 | Search/collection size and byte-boundary fixtures. |
| SCRY-014 | Cached-read network-spy case. |
| SCRY-015 | MCP surface matrix and mode-guard E2E tests. |
| SCRY-016 | Sanitized provider-error and foreign-pagination URL fixtures. |
| SCRY-017 | Failed/canceled/abandoned staging cleanup and explicit-retry cases. |
| SCRY-018 | Delete acknowledgement, tombstone, and Tagger-reference cases. |
| SCRY-019 | SCRY-FIX-COLLECTION pinned-limit metadata and boundary cases. |
| SCRY-020 | Price/rank projection evidence-label snapshot. |
| SCRY-021 | Multi-face root/face coverage and round-trip fixtures. |
| SCRY-022 | Default/all/none/explicit `scryfall` profile matrix plus a local-deck-to-immutable-card-evidence workflow. |

## North-Star Workflow Fixture

Given a revisioned local Commander deck and a declared card/query question,
the client creates or selects an immutable Scryfall snapshot, resolves bounded
card evidence, observes explicit missing/partial states, and receives no role or
card recommendation. The same snapshot replay is byte-stable. The workflow is
visible in the default profile and absent when `scryfall` is not selected.

## Live Tests

Optional `Category=Live` tests may create a small read-only snapshot for a fixed
query and immediately delete the local snapshot. They never mutate Scryfall and
must not be part of `task test`.
