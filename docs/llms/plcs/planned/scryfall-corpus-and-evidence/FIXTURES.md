# Scryfall Corpus And Evidence Fixtures And Acceptance Matrix

## Official Contract Fixtures

| Fixture | Required cases |
| --- | --- |
| Bulk metadata | All Cards, Rulings, Oracle Tags, Art Tags; provider update/version/size/encoding/download fields; unknown extensions. |
| Cards JSONL | Single/multi-face, multilingual printing, known-empty/unknown groups, prices/ranks, malformed line, unknown fields. |
| Rulings JSONL | Multiple ordered rulings, no rulings, unknown field, missing Oracle reference. |
| Oracle Tags JSONL | Direct weights/annotations, aliases, hierarchy, multiple Oracle assignments, dangling/cyclic graph. |
| Art Tags JSONL | Illustration assignments, shared illustration, hierarchy, direct versus inherited evidence. |

Fixtures record the official observation date and source without credentials or
unstable download URLs. The implementation rechecks the contract before live
activation.

## Corpus Lifecycle Cases

| Case | Expected result |
| --- | --- |
| Corpus absent | Status is `not-cached`; no directory/database is created by a read. |
| First explicit sync | Four datasets stage, validate, and atomically become active. |
| Metadata unchanged | Sync returns no-op and updates only the safe check metadata. |
| Second complete sync | New generation becomes active and prior active becomes previous. |
| Third complete sync | Oldest generation is pruned after activation; active/previous remain. |
| Download/import cancellation | Active generation is unchanged and staging is removed. |
| One corrupt dataset | Whole generation fails; no mixed-generation evidence is visible. |
| Rollback with expected active ID | Previous and active swap atomically. |
| Stale rollback/delete request | Conflict and no mutation. |
| Delete without acknowledgement | Invalid input and corpus remains. |

## Cache And Provider Cases

- `default` reuses an exact request/card before 24 hours and refreshes after it.
- `cache-only` returns fresh or stale local evidence and never contacts HTTP.
- `refresh` bypasses eligibility and creates linked immutable evidence.
- In `read-only`, stored hits work, while a miss or `refresh` performs zero HTTP
  and SQLite writes and returns local-write-required.
- Provider failure exposes, but does not silently return, a stale candidate.
- New raw query syntax always contacts Scryfall unless its exact fingerprint is
  eligible.
- Direct IDs/names/printing identities resolve from active corpus first.
- Collection lookup preserves caller order and sends only misses.
- 403/429 stops immediately; transient retry count never exceeds two.
- API request starts across two processes remain at least 125 milliseconds apart.
- Two processes requesting the same miss produce one provider acquisition.
- Process death expires its lease; global request starts remain correctly paced.

## Snapshot And Tag Cases

| Case | Expected result |
| --- | --- |
| Multi-page provider response | Every raw page and result ordinal persists. |
| Failure on later page | No completed readable snapshot is published. |
| Refresh changes response | New linked snapshot; old checksum/bytes unchanged. |
| Cursor replay | Stable 25-item default pages; tampered/wrong-snapshot cursor rejected. |
| Raw source requested | At most 25 objects returned; unknown fields preserved. |
| Card with direct Oracle/art tags | Separate community evidence sections join by correct IDs. |
| Ancestor expansion requested | Direct and inherited matches remain distinct with hierarchy paths. |
| Missing tag dataset | Card facts remain available; tag group is explicitly not cached. |

## MCP Surface Matrix

| Group | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| Provider/evidence reads | 9 | 9 | 9 |
| Corpus status/sync/rollback/delete | 1 | 4 | 4 |
| Snapshot list/get/delete | 2 | 3 | 3 |
| Tag search/cards | 2 | 2 | 2 |
| **Total** | **14** | **18** | **18** |

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| SCRY-001 through SCRY-005 | Official contract and corpus lifecycle matrices |
| SCRY-006 through SCRY-010 | Cache/provider cases and collection boundaries |
| SCRY-011 through SCRY-015 | Snapshot, raw projection, face, and tag cases |
| SCRY-016 through SCRY-020 | Multi-process, HTTP capture, status, mutation guard, and recovery cases |
| SCRY-021 through SCRY-026 | Surface matrix, forbidden scans, schema snapshots, architecture, full gates, and redacted real-corpus acceptance |

## North-Star Workflow

Install the fixture corpus explicitly, restart the MCP in a separate process,
resolve a disposable Commander deck to card/printing/ruling/direct-and-inherited
tag evidence without HTTP, then run one new raw query through Scryfall and
replay its immutable snapshot. No result assigns a deck category or recommends
a card.

## Live Acceptance

Optional `Category=Live` tests fetch current bulk metadata and one bounded
read-only provider response. Before this child is accepted, a separately
invoked manual workflow must install the real four-dataset corpus, verify
activation, local reuse from a second MCP process, rollback, and guarded cleanup
only with explicit consent. Normal CI never downloads bulk payloads.
