# Scryfall Evidence Snapshots Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Scryfall` owns official HTTP contracts, pacing, mapping, and
`scryfall.db`. Core owns provider-neutral card identity/projection contracts;
App owns MCP wrappers and modes. No other adapter references Scryfall transport
types.

### Acquisition request union

- `Search(query, unique, order, direction, includeExtras,
  includeMultilingual, includeVariations)`
- `Named(exactOrFuzzy, value, optionalSet)`
- `CardId(idKind, value)`
- `Collection(identifiers)`
- `Prints(cardOrOracleId)`
- `Rulings(cardIdKind, value)`
- `Sets(optionalCode)`
- `Catalog(catalogName)`
- `Autocomplete(query, includeExtras)`
- `BulkDataMetadata(optionalType)`

The canonical serialized union and options form the request fingerprint.

### SQLite schema

| Table | Responsibility |
| --- | --- |
| `snapshots` | ID/name, request kind/JSON/fingerprint, status, lineage, counts, bytes, timestamps, completeness. |
| `snapshot_pages` | Ordered raw JSON pages, source URL, HTTP validators, retrieval time, checksum. |
| `snapshot_objects` | Snapshot/ordinal, object kind, provider IDs, raw object JSON, object checksum. |
| `card_projections` | Snapshot/ordinal plus indexed normalized identity, name, mana/type/oracle, colors, legalities, images, provider-supplied prices, and provider-supplied popularity ranks. Price/rank fields remain source evidence, never quality scores. |
| `schema_migrations` | Version/checksum/application metadata. |

Tables are append-only for completed snapshots. Refresh inserts a new snapshot.
Delete requires `acknowledgeEvidenceLoss=true`, purges page/object/projection
payloads transactionally, and retains an immutable tombstone containing the
snapshot ID, request fingerprint, content checksum, lineage IDs, deletion UTC,
and `deleted` status. It never cascades into another database; a Tagger/deck
record that cites the ID can still resolve the tombstone and report source
payload unavailable.

Acquisition pages/objects are written only to run-owned staging tables. On
complete, one transaction promotes them and marks the snapshot complete. On
failed, canceled, or too-large, the same completion path deletes staging data
and retains bounded request/status diagnostics only. Startup marks abandoned
pending runs failed and deletes their staging rows. Retry is a new explicit
create/refresh with a new snapshot ID; no run resumes implicitly.

### Output shape

Each object row contains `sourceObject`, `projection`, `ordinal`, and an
evidence descriptor with snapshot ID, source URL, retrieval time, and checksum.
Typed Scryfall source models use `JsonExtensionData` so future fields survive.
Projection fields are additive and nullable; absence remains unknown.
Multi-face projections retain one root projection plus ordered face projections.
Coverage is evaluated independently at root and face level: an explicitly
present empty source array/string is known-empty, while an absent field remains
unknown. Split/transform/modal faces are never flattened into a synthetic root
fact.
Price and rank projections are explicitly labeled Scryfall-supplied
price/popularity evidence and carry the snapshot/source descriptor. They are not
named or exposed as strength, quality, power, or recommendation fields.

The bulk request case stores only the official metadata response (type, update
time, size, URI, and safe extension fields). It never follows the bulk download
URI. The collection fixture pins the observed maximum of 75 identifiers with
its observation date/source; implementation reverifies official documentation
before accepting a changed maximum.

### HTTP policy

One process-wide pacer serializes starts with a 125 ms minimum interval. The
client sends a project-specific User-Agent and JSON Accept header. It follows
provider pagination URLs only after verifying HTTPS and the expected Scryfall
host. It never follows arbitrary URLs from card fields. 403/429 stop; 5xx and
transport failures retry twice at fixed bounded delays with cancellation.

## Toolset And North-Star Design

App assigns all seven tools to the default-enabled `scryfall` toolset. Startup
registration intersects that toolset with operation-mode visibility; it never
changes the acquisition guards. The capability document reports whether the
toolset is enabled and its visible count. The acceptance workflow begins with
an explicit local deck or query, creates or selects immutable source evidence,
reads bounded objects, preserves unknown/unavailable states, and stops before
the LLM's card-selection judgment. No alias, generic provider router, or hidden
refresh path is part of the design.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Live query on every read | Rejected; same request can change and fail. |
| Full local Scryfall query engine | Rejected; would only pretend at syntax parity. |
| Bulk-data-only database | Rejected for v1; does not preserve arbitrary official query semantics. |
| Mutable named cache entries | Rejected; breaks evidence repeatability. |
| Store only normalized cards | Rejected; loses provider fidelity and future fields. |

## Failure Modes

- Invalid Scryfall query returns invalid input with provider error details.
- Network/provider failure creates a failed acquisition record, not a readable
  partial snapshot.
- Size limit returns too-large with counts/bytes observed so far and removes
  staged payload rows.
- Missing snapshot is not found; failed snapshot is unavailable; empty complete
  result is successful empty.
- Corrupt page/object checksum marks snapshot unavailable until deleted and
  reacquired; it is never silently repaired.
- Deleted snapshots resolve to their tombstone; reads never imply the purged
  provider payload remains available.

## Test Architecture

Fake HTTP fixtures cover every endpoint, pagination, errors, headers, pacing,
retry, cancellation, size limits, and unknown fields. Temporary SQLite tests
cover migrations, append-only constraints, checksum validation, staging
cleanup/crash recovery, tombstone deletion, dependent provenance, and lineage.
MCP tests cover schemas, pagination, modes, and network-free reads.
Optional live tests perform read-only acquisition only and are not required for
normal validation.
