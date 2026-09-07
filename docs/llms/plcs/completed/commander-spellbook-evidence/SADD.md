# Commander Spellbook Evidence Design

## Document Control

- Lifecycle status: Complete
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Chosen Design

Add one concrete `MtgMcp.Spellbook` adapter. The adapter owns Commander
Spellbook HTTP calls, source JSON, pacing, cache files, and provider errors.
The App owns MCP tools, toolset selection, and mapping from a saved local deck.

The adapter returns unchanged Commander Spellbook JSON inside a small evidence record.
It does not map every source field into a local model. This preserves source
fields that the project does not use yet.

The App reads one saved deck revision before it calls the adapter. It creates a
plain Commander Spellbook deck request from `main` and `commander` entries.
The adapter has no reference to `MtgMcp.Decks` or `MtgMcp.App`.

## Decision Boundary

Commander Spellbook data is source evidence. The server reports what the source
returned. The server does not decide whether a player must add, remove, keep,
or avoid a card or combo.

The source's fields such as `popularity`, `bracketTag`, `legalities`, and
`almostIncluded` retain their source names. mtg-mcp does not turn them into a
local score, deck label, legality conclusion, or recommendation.

## Building Blocks

| Building block | Responsibility | Dependencies | Tests |
| --- | --- | --- | --- |
| `MtgMcp.Spellbook` | The provider boundary. | Core, BCL, SQLite. | Adapter tests. |
| `SpellbookService` | Public adapter entry points, input validation, and one mapping to `OperationResult<T>`. | Core result union, transport, and cache. | Service tests. |
| `SpellbookTransport` | Fixed origin, headers, bounded response reads, and HTTP status mapping. | `HttpClient`. | Fake-HTTP tests. |
| `SpellbookRequestPacer` | Shared request-start interval and 429 cooldown. | `TimeProvider` and `spellbook.db`. | Clock and SQLite tests. |
| `SpellbookCache` | Exact response lookup, storage, expiration, and cleanup. | `spellbook.db`. | SQLite tests. |
| `SpellbookContract` | The observed API version, small route snapshot checksum, limits, and credit URL. | BCL. | Contract tests. |
| `SpellbookRequestDetails` | Caller-visible facts for this source request. | BCL. | Serialization tests. |
| `SpellbookEvidence` | An independent copy of the Commander Spellbook response plus where and when it was obtained. | Core result type and JSON. | Serialization tests. |
| `SpellbookDeckInputResolver` | Maps one local deck revision into the adapter request. | Decks and Spellbook. | App tests. |
| `SpellbookDeckComboEvidence` | Keeps a local record of what the tool sent beside source evidence for one deck lookup. | Decks and Spellbook. | App tests. |
| `SpellbookReadTools` | The three MCP tool methods. | Service and resolver. | Tool and E2E tests. |
| `SpellbookToolsetManifest` | The opt-in toolset descriptor and registration. | App composition. | Surface tests. |

No generic provider interface, shared provider cache, provider router, or
cross-provider result type is added.

The adapter uses the existing `OperationResult<T>` union from Core. The
transport uses one private provider exception for expected HTTP conditions.
`SpellbookService` converts that exception once into the existing result cases.
It lets cancellation pass through. It does not turn programming or SQLite
faults into invented provider results.

New time-dependent code uses `TimeProvider` and its BCL delay overload. Tests
use a controllable provider. No new clock interface is needed.

## Request Design

### Variant search

The tool accepts one required `sourceQuery`. It sends that string as the source
`q` parameter without parsing or rewriting it. It sends only `limit`, `offset`,
and `groupByCombo` beside the query.

The defaults are `limit=20`, `offset=0`, and `groupByCombo=true`. The adapter
always sends all three controls, including when the caller omitted them. It
rejects a null, empty, or whitespace-only query before HTTP. For a valid query,
it sends the caller's exact characters without trimming or rewriting them.
This prevents an accidental broad browse and avoids the source defaults of 100
variants or 1,000 deck-lookup variants.

```text
GET /variants/?q=card%3A%22Thassa%27s%20Oracle%22&limit=20&offset=0&groupByCombo=true
```

### Variant get

The tool accepts an exact source ID. It URL-escapes the ID as one path segment.
It does not accept a caller-controlled origin, path, or query string.

```text
GET /variants/742-1295/
```

### Saved deck combo lookup

The tool accepts a local `deckId` and `expectedRevision`. It loads the exact
current deck. If the revision changed, it returns a conflict before it sends
HTTP.

The resolver uses these rules:

1. Put `commander` entries in the source `commanders` array.
2. Put `main` entries in the source `main` array.
3. Group repeated card names in each source zone and add their quantities.
4. Sort source rows by card name with ordinal comparison.
5. Report every non-main, non-commander entry as skipped evidence.
6. Reject an empty selection, more than 12 commander rows, more than 600 main
   rows, a sent card name longer than 256 characters, or a quantity sum that
   overflows before HTTP.

The tool can receive an optional `sourceQuery`. It passes the string as the
source `q` parameter. It does not derive a query from the local deck's format,
color identity, tags, or card text.

This page uses the same explicit defaults as variant search: `limit=20`,
`offset=0`, and `groupByCombo=true`.

```json
{
  "commanders": [
    { "card": "Muldrotha, the Gravetide", "quantity": 1 }
  ],
  "main": [
    { "card": "Sol Ring", "quantity": 1 }
  ]
}
```

### Commander Spellbook response layout

The current v6.3.3 deck response is a paginated object. It has `count`,
`next`, `previous`, and one `results` object. `results` is not an array. It
contains `identity` plus these six Commander Spellbook groups:

- `included`
- `includedByChangingCommanders`
- `almostIncluded`
- `almostIncludedByAddingColors`
- `almostIncludedByChangingCommanders`
- `almostIncludedByAddingColorsAndChangingCommanders`

The adapter does not send the optional source `count` parameter. It preserves
the source value, which is normally `null` when the parameter is omitted. It
does not rename, merge, rank, or omit those groups.

## Source Evidence Response

`SpellbookEvidence` keeps an independent `JsonElement` copy of the Commander
Spellbook response.
It also contains these fields:

| Field | Meaning |
| --- | --- |
| `name` | `Commander Spellbook`. |
| `operation` | `variant-search`, `variant-get`, or `find-my-combos`. |
| `request` | The current call's query and page controls, or its exact variant ID. It is not stored in the cache. |
| `endpoint` | The fixed method and source path, without deck data or query text. |
| `sourceApiVersion` | The API version recorded by the checked-in OpenAPI fixture. |
| `contractChecksum` | SHA-256 of that fixture. |
| `retrievedAtUtc` | The time of the original successful source response. |
| `cacheStatus` | `network` for a source result or `cached` for a usable local result. |
| `sourceChecksum` | SHA-256 of the returned JSON. |
| `sourceUrl` | `https://commanderspellbook.com`. |
| `limitations` | Short source and local limits that affect interpretation, including that sent deck entries are not proof that the source recognized them. |
| `data` | The lossless provider JSON. |

For only the deck lookup, the App returns `SpellbookDeckComboEvidence` with a
`deck` selection record and a nested `source` `SpellbookEvidence`. The selection
record contains the deck ID, revision, entries sent to the source, and skipped
entries. It is not stored in the adapter cache. The App does not copy, rename,
or mutate source evidence to add local facts.

## Cache Design

The adapter owns one `spellbook.db` file under the existing application data
root. The file is a response cache, not a local deck store.

| Table | Stored data | Not stored |
| --- | --- | --- |
| `response_cache` | Operation, request hash, response JSON, response checksum, source API version, contract checksum, and retrieval time. | A separate raw request column, raw deck request, user account data, or usage history. |
| `request_pacing` | Next reserved request start and cooldown end. | Request content or source response. |

The adapter builds a stable in-memory request representation in a fixed field
order. It contains the
operation, HTTP method and path, raw source query when present, `limit`,
`offset`, `groupByCombo`, the sorted deck body for a deck lookup, and the
contract checksum. The raw query is not trimmed, parsed, or normalized. The
adapter hashes that value with SHA-256 and stores the hash, never the value.

`SpellbookRequestDetails` is built from the current call after validation. It
is returned with both network and cached evidence, but it is never written to
the database. A deck selection labels its card rows as `sentEntries`; Commander
Spellbook can ignore an unknown card name, so the source response does not prove
that it recognized every sent entry.

The response cache is lossless provider data, not a private request log. The
provider's `next` or `previous` URL can repeat a source query, so the cached
JSON can contain that source-provided value. The adapter does not add a second
copy of a query or deck request. It documents this limit instead of rewriting
the provider response or claiming the cache hides request content.

A cache hit requires the current contract checksum. The checked-in contract
snapshot records only the three routes and response fields this adapter uses;
it is reviewed against the official schema. If that snapshot changes, the old
row is a miss and is removed during normal cleanup.
The adapter stores only a completed successful JSON object response. It never
caches an error, a non-object response, or a partial read. A cache hit uses the
stored source response and original retrieval time.

The default cache freshness time is 15 minutes. The App exposes it as
`--spellbook-ttl-minutes`, `MTGMCP__SPELLBOOK__TTL_MINUTES`, or
`SPELLBOOK_TTL_MINUTES` in `mtg-mcp.json`. Valid values are whole minutes from
1 through 1,440.

When the adapter reads or writes the cache, it removes expired rows. It does not
use an expired result if the source is unavailable. It does not refresh data in
the background.

## Provider Stewardship

The transport uses only `https://backend.commanderspellbook.com/`. It sends:

```text
User-Agent: mtg-mcp/<package-version> (+https://github.com/nccurry/mtg-mcp)
Accept: application/json
```

The adapter uses these limits:

| Rule | Value |
| --- | --- |
| Request starts | At least one second apart across processes that share the same data root. |
| Request rate | At most 60 starts per minute. |
| Upstream calls per tool call | One. |
| Variant or deck page size | 1 through 25. |
| Offset | 0 through 1,000. |
| Response size | At most the configured local 2 MiB limit. |
| Transport timeout | 15 seconds. |
| Automatic retries | None. |
| `Retry-After` cooldown | Record a value up to 60 seconds. Do not retry the current call. |

The source says 80 requests per minute is a safe ceiling. The one-second limit
stays below that value. A `429` records a bounded cooldown. The current call,
and later calls during that cooldown, return a typed unavailable result without
another upstream request.

The transport uses one fixed 15-second timeout and no timeout setting. It maps
only a timeout that it created to unavailable. If the caller cancels, it lets
the caller's `OperationCanceledException` continue unchanged.

The pacer reserves a request start with one SQLite immediate transaction. It
uses `BEGIN IMMEDIATE` through `SqliteTransaction(deferred: false)`, reads the
next start and cooldown, and returns unavailable without a reservation if the
cooldown is active. Otherwise it reserves
`max(now, nextStart)`, writes that value plus one second as the next start, and
commits before it waits. After the delay, it rechecks the cooldown immediately
before HTTP. A new active cooldown stops the request. A `429` writes the later
of the existing cooldown and the accepted bounded `Retry-After` value in a
separate immediate transaction. It never retries the failed request.

The adapter never follows a provider `next` URL. It never pages through the
full variant list. It never calls the bulk export in this phase.

## Failure Handling

| Condition | Result |
| --- | --- |
| Invalid local tool input or source 400 | `OperationInvalidInput` with a safe source-rejected reason. |
| Missing local deck | `OperationNotFound`. |
| Changed local deck revision | `OperationConflict`. |
| Source 404 | `OperationNotFound` with a source-not-found reason. |
| Source 429 or active cooldown | `OperationUnavailable` with a rate-limited reason and no wait. |
| Source 401, 403, 5xx, or network error | `OperationUnavailable`. |
| Transport timeout | `OperationUnavailable` with a timeout reason. |
| Invalid JSON or a non-object response | `OperationUnsupported`. |
| Response above 2 MiB | `OperationUnavailable`. |
| Cancellation | Propagate `OperationCanceledException`. |

The adapter never includes a response body, request body, local data path, or
exception text in an error message.

## MCP Surface

The `spellbook` descriptor is stable and opt-in. It has three read-only tools.
It is visible in `read-only`, `local`, and `remote` modes because provider reads
are allowed in each mode.

| Profile | Read-only | Local | Remote |
| --- | ---: | ---: | ---: |
| `default` | 32 | 54 | 54 |
| `all` | 60 | 83 | 96 |
| `spellbook` | 3 | 3 | 3 |

The capability resource adds a `spellbook` toolset item. It reports that no
credentials are required. The resource schema version increases because its
data schema list adds the Spellbook cache format.

`FoundationHost` creates a deck store when `spellbook` is enabled. The App uses
that store only to resolve `spellbook_deck_combos_find`. The Spellbook adapter
never reads `decks.db`.

## Project Boundaries

```text
MtgMcp.App --> MtgMcp.Decks
           --> MtgMcp.Spellbook --> MtgMcp.Core
```

`MtgMcp.Core` receives no provider transport, cache, DTO, or MCP dependency.
`MtgMcp.Decks` receives no HTTP dependency. `MtgMcp.App` composes the adapter
and exposes the MCP surface. The new adapter owns SQLite and HTTP behavior.

## Test Design

| Test layer | Coverage |
| --- | --- |
| Contract tests | The checked-in API fixture, version, checksum, route list, and limits. |
| Cache tests | Hash-only keys, fresh hit, expiration, cleanup, current contract checksum, and original retrieval time. |
| Pacing tests | Independent database owners, atomic start reservations, cooldown, and cancellation. |
| Transport tests | Headers, fixed origin, response bound, statuses, timeout, caller cancellation, no retry, and redaction. |
| Service tests | Query bounds, explicit defaults, ID validation, JSON preservation, request facts, and typed result mapping. |
| App tests | Deck zone mapping, revision conflict, skipped entries, and tool descriptions. |
| Architecture tests | Project references, surface count, tool assignment, Task wiring, and coverage target. |
| E2E tests | Default, all, none, explicit `spellbook`, and a separate-server saved-deck flow that reads a pre-seeded valid cache row. |
| Live test | One bounded public read, marked `Category=Live`. |

The process test does not put a fake HTTP handler inside the server process.
Its seed callback creates a local deck and stores one valid cache response with
the adapter and a fake handler before the server starts. The real child server
then reads the cache, reports `cached`, and sends no network request. HTTP
failure behavior remains an adapter test.

## Non-goals

Do not add these items in this packet:

- `estimate-bracket` or a local bracket score.
- A claim that a source legality field proves the whole deck is legal.
- A recommendation based on included or almost-included results.
- Bulk source download or source-wide local search.
- Ad-hoc card-list or text-deck input.
- A shared `IProvider`, `ICache`, or request router.
- Background sync, telemetry, or user-profile tracking.

## Alternatives

| Option | Decision | Reason |
| --- | --- | --- |
| Use the source bulk export first. | Rejected | The immediate workflow needs small current responses. |
| Page through variants for local search. | Rejected | The source warns against it. |
| Include bracket estimates. | Rejected | They are qualitative source output, not a first evidence workflow. |
| Use an in-memory cache only. | Rejected | It gives no benefit after an MCP process restarts. |
| Store raw deck requests for cache lookup. | Rejected | A hash is enough to find the cached response. |
| Accept raw deck text on day one. | Deferred | A saved deck gives a revisioned and inspectable input path. |

## Risks and Deferred Work

| Item | Type | Response |
| --- | --- | --- |
| The source schema changes. | Risk | Recheck the schema before code and refresh the fixture with a review. |
| A source query returns a large page. | Risk | Keep the page limit and 2 MiB response bound. |
| A user needs an unsaved deck. | Deferred | Design a separate input packet after the saved-deck path proves useful. |
| A user needs full source data. | Deferred | Review a separate explicit bulk-cache design. |
| A source field seems useful for advice. | Risk | Return it as source data. Do not add advice. |
