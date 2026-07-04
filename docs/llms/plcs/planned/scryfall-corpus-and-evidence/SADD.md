# Scryfall Corpus And Evidence Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Scryfall` owns official HTTP/JSONL contracts, cross-process pacing,
mapping, request snapshots, and `scryfall.db`. Core retains only
provider-neutral identity/evidence contracts. App owns typed MCP wrappers,
toolset registration, operation modes, and composition. Decks and Scryfall do
not reference one another.

Scryfall requires no credential. Requests use a product/version User-Agent and
documented Accept header; diagnostics redact local paths, download URLs, and
raw response fragments. Redirects or pagination to unexpected hosts fail
closed rather than forwarding headers.

### Persistence model

| Table group | Responsibility |
| --- | --- |
| `corpus_generations`, `corpus_datasets` | Active/previous identity, provider metadata, hashes, byte/count totals, status, and activation/rollback audit. |
| `card_objects`, `card_faces`, normalized indexes | Lossless Scryfall card JSON plus identity, printing, language, rules, legality, price, and lookup fields. |
| `rulings` | Ordered raw/normalized rulings joined by Oracle identity. |
| `tags`, `tag_relations`, `tag_assignments` | Oracle/art metadata, hierarchy, aliases, weights, annotations, and direct assignments. |
| `request_snapshots`, `snapshot_pages`, `snapshot_members` | Immutable canonical requests, raw pages, ordered objects, checksums, cursors, and lineage. |
| `acquisition_leases`, `provider_pacing` | Cross-process ownership, crash expiry, duplicate suppression, and next legal request start. |
| `schema_migrations` | Transactional schema version and checksum history. |

Completed request snapshots retain content-addressed payload references
independently of corpus-generation cleanup. Removing the oldest corpus cannot
break snapshot replay.

### Explicit bulk synchronization

1. Acquire the database-backed corpus lease and inspect local status.
2. Respect the 24-hour metadata-check TTL unless an explicit force-check was
   requested.
3. Fetch official metadata for the fixed four-dataset profile.
4. If provider versions match the active generation, record the check and
   return a no-op result.
5. Preflight a conservative disk requirement without exposing the path.
6. Stream compressed JSONL through bounded parsers into generation-owned
   staging rows while hashing bytes and validating identities/relations.
7. Validate counts, checksums, required fields, tag graph integrity, and raw
   round trips.
8. Atomically mark the generation active and the former active generation
   previous.
9. Remove generations older than previous only after activation succeeds and
   no immutable snapshot payload depends on them.

Cancellation, process loss, checksum failure, malformed JSONL, insufficient
disk, or one failed dataset leaves the prior active generation untouched.

### Request and freshness flow

The canonical request fingerprint includes operation kind, losslessly encoded
inputs, provider options, and adapter schema version. It does not rewrite or
approximate an arbitrary search expression. `default` reuses eligible exact
request/card evidence until 24 hours. `cache-only` performs no HTTP and may
return stale evidence with an explicit state. In `local`/`remote`, `default`
acquires a miss and `refresh` attempts the provider, with both creating new
lineage. In `read-only`, a miss or `refresh` returns local-write-required before
HTTP because the completed foundation forbids cache, snapshot, lease, and
pacing-table writes in that mode.

Identity-oriented operations consult the active corpus first. Collections
deduplicate identifiers, preserve caller order/errors, and send only
ineligible misses to the official collection endpoint. New arbitrary search
syntax never evaluates against local card tables. Exact prior search requests
may reuse their snapshot.

All API request starts in write-authorized modes share a database-backed minimum
125-millisecond interval across processes. If provider refresh fails, the
operation returns the provider failure and may name a stale snapshot as
available evidence; it never substitutes it silently.

### Tags and evidence composition

Oracle assignments join through `oracle_id`; art assignments join through
`illustration_id`. Direct rows remain direct. Inherited results are computed by
deterministic hierarchy traversal and include the path that produced the
match. Card responses may include joined tags, but card and tag sections carry
separate source descriptors and coverage states.

No deck category rule or role inference belongs in this assembly. The later
categorization child receives already labeled tag evidence through composition.

### MCP contracts and pagination

All eighteen exact tools in README are explicitly registered; no request-kind
router or assembly scanning is used. The fourteen read tools are visible in
every mode, but any branch that would acquire and persist provider evidence is
guarded as a local write. Corpus sync/rollback/delete and snapshot delete are
also guarded local writes.

| Tool | Required input contract | Result-specific fields |
| --- | --- | --- |
| `scryfall_search` | Raw query; provider `unique`, `order`, `direction`, extras/multilingual/variation flags; freshness policy; output page size/detail. | Snapshot ID when persisted, total count, ordered first page, next cursor, provider warnings. |
| `scryfall_card_get` | Exactly one lookup union case: Scryfall/provider ID, exact/fuzzy name, or set plus collector number; freshness policy; detail. | One lossless card object and normalized projection, joined tag coverage, snapshot metadata when acquired. |
| `scryfall_card_collection` | Ordered identifiers using the same ID/name/printing cases, maximum 75; freshness policy; detail. | Ordered found/not-found/error rows, local/provider origin per row, snapshot lineage for provider misses. |
| `scryfall_card_prints` | Stable card or Oracle identity; freshness policy; output page size/detail. | Complete ordered printing membership captured in one snapshot, then locally paginated. |
| `scryfall_card_rulings` | Stable card or Oracle identity; freshness policy; output page size/detail. | Complete ordered ruling membership captured in one snapshot, then locally paginated. |
| `scryfall_sets` | Optional exact set code or ID; freshness policy; output page size/detail. | One set or complete provider set list with snapshot/cursor metadata. |
| `scryfall_catalog` | Exact documented catalog name; freshness policy; output page size. | Ordered catalog values and snapshot/cursor metadata. |
| `scryfall_autocomplete` | Query text and explicit include-extras flag; freshness policy; output page size. | Ordered suggestions and snapshot/cursor metadata. |
| `scryfall_bulk_metadata` | Freshness policy. | The fixed four-dataset metadata profile, eligibility, and snapshot metadata. |
| `scryfall_corpus_status` | No input. | Active/previous generations, dataset states, integrity, age, bytes, and refresh eligibility. |
| `scryfall_corpus_sync` | `default` or `refresh` metadata policy and optional expected active generation. | No-op or activated generation, dataset counts/hashes, prior generation, and bounded diagnostics. |
| `scryfall_corpus_rollback` | Expected active/previous generation IDs and explicit activation-change acknowledgement. | New active/previous IDs and verification summary. |
| `scryfall_corpus_delete` | Expected active generation ID and explicit data-loss acknowledgement. | Deleted generation IDs and final corpus state. |
| `scryfall_snapshot_list` | Optional operation/date filters; opaque cursor; page size. | Ordered snapshot summaries and next cursor. |
| `scryfall_snapshot_get` | Snapshot ID; opaque cursor; page size; detail/raw-source selection. | Immutable request/result page, checksum, lineage, and next cursor. |
| `scryfall_snapshot_delete` | Snapshot ID, expected checksum, and explicit data-loss acknowledgement. | Deleted snapshot identity and verification state. |
| `scryfall_tag_search` | Query text; optional `oracle`/`art` type; opaque cursor; page size. | Exact tag metadata matches from the active generation and next cursor. |
| `scryfall_cards_by_tag` | Tag ID or exact slug plus type; descendant policy; minimum weight; opaque cursor; page size/detail. | Direct/inherited assignments, hierarchy paths, card identities, generation, and next cursor. |

All request shapes reject unknown enum values, mutually exclusive lookup cases,
blank required text, invalid cursors, and out-of-range page sizes. Every result
uses the shared closed operation-result envelope and preserves explicit
not-cached, unavailable, partial, unsupported, invalid, and conflict states.

Provider operations return snapshot identity, source, retrieval time,
freshness, completeness, warnings, normalized evidence, and the first bounded
page only after all provider pages have been captured successfully.
`scryfall_snapshot_get` replays later pages using an opaque cursor bound
to snapshot ID, checksum, and last ordinal. Default page size is 25, maximum
100, and raw source inclusion reduces the maximum to 25.

### Future local query boundary

Raw JSON and additive normalized indexes make future evaluation feasible, but
this child introduces no query parser/evaluator interface. A separately
reviewed PLC must define syntax coverage and differential parity. Until then,
uncached arbitrary queries remain provider-authoritative.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Separate community-tag database and site scraper | Rejected; official Scryfall bulk data now provides the required assignments. |
| Automatic first-use corpus download | Rejected; bandwidth/disk mutation must be explicit. |
| Keep every bulk generation | Rejected; current plus previous provides bounded rollback. |
| Query the partial local cache first | Rejected; unseen cards create silent false negatives. |
| Implement Scryfall syntax now | Deferred to its own feasibility/implementation PLC. |
| Store only projections | Rejected; loses provider fidelity and future migration input. |

## Failure Modes

- Missing corpus returns `not-cached`, never empty success.
- Stale corpus remains readable under `cache-only` and visibly stale.
- Foreign pagination/download hosts, 403, or 429 stop acquisition.
- Lease expiry allows recovery after a dead process without concurrent owners.
- Corrupt active evidence is unavailable until rollback, resync, or guarded
  deletion; no silent repair occurs.
- Tag graph cycles/dangling IDs fail generation validation rather than producing
  invented inherited matches.

## Test Architecture

Small fake compressed JSONL datasets exercise the complete import pipeline.
Fake HTTP/clock tests cover TTL, retry/stop policy, exact searches, collection
partitioning, and headers. Temporary SQLite and spawned-process tests cover
leases, WAL reads, activation, rollback, pruning, corruption, and replay.
Official-client tests cover all modes, toolsets, pagination, redaction, and a
local Commander deck resolved entirely from fixture corpus evidence.

Optional live tests fetch metadata and a small provider response. A separate
manual acceptance workflow may install the real corpus but is never part of
normal CI.
