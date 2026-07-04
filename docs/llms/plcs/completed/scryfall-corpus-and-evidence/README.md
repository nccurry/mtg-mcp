# Scryfall Corpus And Evidence PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/scryfall-corpus-and-evidence/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: Phase 5 completed

## Summary

This packet defines the shared official Scryfall evidence boundary. An explicit
bulk synchronization streams All Cards, Rulings, Oracle Tags, and Art Tags into
one reusable `scryfall.db`. Ordinary provider operations use a deterministic
24-hour cache policy, preserve immutable request snapshots, and consult the
local corpus before performing supported identity-oriented reads.

New arbitrary Scryfall search syntax remains provider-authoritative. The local
corpus never guesses query membership or ordering. Card facts and community
Tagger evidence share storage and acquisition but remain separately labeled.

## Dependencies

- [Accepted AMEND-004](../../in-progress/evidence-first-mcp-rewrite-program/README.md#program-amendments)
- [Completed rewrite foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Completed local deck store](../../completed/local-deck-store/README.md)
- [Completed MCP capability toolsets](../../completed/mcp-capability-toolsets/README.md)

## Current-State Disposition

The runtime now contains `MtgMcp.Scryfall`, the exact eighteen-tool surface,
and versioned `scryfall.db` storage. It reuses foundation evidence/result
contracts, static toolset registration, the standard data root, redaction,
packaging, and test wiring without reviving the removed legacy gateway or
former snapshot/Tagger abstractions.

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Use official bulk card, ruling, Oracle-tag, and art-tag datasets. | Accepted and implemented | One supported provider contract replaces unsupported Tagger scraping and redundant per-card traffic. |
| Store joined evidence in one `scryfall.db`. | Accepted and implemented | The datasets share provider, update cadence, identity keys, and rebuild lifecycle. |
| Keep current and previous complete corpus generations. | Accepted and implemented | Atomic activation and one explicit rollback remain bounded. |
| Require explicit corpus synchronization. | Accepted and implemented | A normal card or query call never triggers a multi-gigabyte download. |
| Use one configurable 24-hour TTL with explicit freshness policies. | Accepted and implemented | Cache eligibility is predictable without expiring immutable evidence. |
| Use authoritative hybrid search behavior. | Accepted and implemented | Exact cached searches are reusable; new arbitrary syntax stays owned by Scryfall. |
| Coordinate acquisition and pacing through SQLite. | Accepted and implemented | Separate MCP processes sharing one data root do not duplicate exact requests or exceed provider pacing collectively. |
| Preserve raw JSON plus normalized projections. | Accepted and implemented | Current tools retain fidelity and a future local-query PLC can add indexes without changing identity contracts. |

## Public Surface

Provider and evidence reads:

- `scryfall_search`
- `scryfall_card_get`
- `scryfall_card_collection`
- `scryfall_card_prints`
- `scryfall_card_rulings`
- `scryfall_sets`
- `scryfall_catalog`
- `scryfall_autocomplete`
- `scryfall_bulk_metadata`

Local corpus lifecycle:

- `scryfall_corpus_status`
- `scryfall_corpus_sync`
- `scryfall_corpus_rollback`
- `scryfall_corpus_delete`

Immutable request evidence:

- `scryfall_snapshot_list`
- `scryfall_snapshot_get`
- `scryfall_snapshot_delete`

Community tag evidence:

- `scryfall_tag_search`
- `scryfall_cards_by_tag`

Fourteen tools are visible in `read-only`; all eighteen are visible in `local`
and `remote`. In `read-only`, they use already stored corpus/snapshot evidence
and perform zero database writes; a policy requiring acquisition returns an
explicit local-write-required state. In `local`/`remote`, provider acquisition,
cache/snapshot persistence, corpus sync/rollback/delete, and snapshot delete are
guarded local writes.
There are no Scryfall resources or prompts. These are tools because each read or
lifecycle action has caller parameters, bounded output, typed failure states,
and in four cases explicit mutation authority.

`scryfall_card_collection` accepts up to 150 ordered identity rows. It resolves
the selected corpus generation first, globally deduplicates provider misses,
and submits sequential official batches of no more than 75 identifiers. The
result uses 25-row default pages, up to 100 compact rows or 25 raw rows, with a
cursor bound to the exact corpus generation, provider snapshot, ordered input,
result checksum, and offset. Continuation never performs HTTP or refreshes its
evidence.

## Official Contract Evidence

The contract observed on 2026-07-04 exposes `all_cards`, `rulings`,
`oracle_tags`, and `art_tags` metadata objects with `download_uri` JSON arrays
and `jsonl_download_uri` gzip streams. Tag objects expose IDs, labels, slugs,
type, description, parent/child IDs, aliases, and taggings. Oracle assignments
join by `oracle_id`; art assignments join by `illustration_id`; assignments
carry a weight and may carry an annotation. Bulk taggings are direct and
ancestor matches require explicit hierarchy traversal.

- [Scryfall Bulk Data](https://scryfall.com/docs/api/bulk-data)
- [Scryfall Tags](https://scryfall.com/docs/api/tags)
- [Scryfall API usage guidance](https://scryfall.com/docs/faqs/i-m-having-trouble-accessing-the-scryfall-api-or-i-m-blocked-17)

Implementation must reverify this contract before activation and preserve a
dated sanitized fixture. API requests must use explicit User-Agent and Accept
headers, honor Scryfall's documented two-requests-per-second search and
collection ceiling, and prefer bulk files for large data needs. The child uses
that conservative 500-millisecond interval for every supported API request
instead of maintaining route-specific pacing. Bulk prices older than 24 hours
are stale provider evidence, not current sales data.

The implementation re-observed the official endpoint on 2026-07-04. Metadata
included all four required `jsonl_download_uri` gzip streams. Sampled Oracle
and art records matched the documented ID, hierarchy, aliases, weighted
taggings, and Oracle/illustration join fields. Normal tests use a sanitized
dated miniature of that shape and never contact Scryfall.

The [full-corpus acceptance](FULL_CORPUS_ACCEPTANCE.md) subsequently installed
and retained all four current datasets. It also established that a present
empty ruling comment is valid source evidence and must not be classified as a
missing required field.

## Implementation Evidence

- All eighteen tools are explicitly registered under only `scryfall`; mode
  discovery is exactly 14/18/18.
- Provider tests cover exact TTL boundaries, immutable lineage, complete
  multi-page capture, later-page failure, local-first identities, collection
  partition/deduplication, 150-row pagination, 75-identifier provider batches,
  retained/pruned cursor evidence, blocking responses, bounded
  retry, headers, concurrent refresh ownership, and stale-candidate reporting.
- Corpus tests cover four-dataset streaming, bounded size, unchanged metadata,
  current/previous retention, third-generation pruning, rollback/delete guards,
  abandoned-staging cleanup, malformed input, cancellation, dangling/cyclic
  rejection, face-level art-tag joins, alias lookup, and second-instance reuse.
- SQLite tests cover migration checksum validation, crash-expiring leases,
  content-addressed payload reuse, duplicate request ownership, and global
  500-millisecond pacing.
- Official-client tests cover exact schemas/annotations, all operation modes,
  fixture corpus/tag/ruling evidence, compact/checksummed and raw immutable
  replay, and guarded deletion.
- The final offline suite passes 205 tests. Production line coverage is
  94.64% for App, 100% for Core, 94.34% for Decks, and 93.75% for Scryfall.
  Lint, exact-surface, package, process-smoke, official-client MCP smoke, and
  installed-tool MCP smoke gates all pass.
- The bounded live official API check and the full MCP Red/White Weenies
  workflow passed on 2026-07-04. The workflow created and structurally
  validated a 60-card deck, resolved all twelve unique card names in one
  collection request, checked color/price evidence, persisted both SQLite
  stores, and replayed the resulting immutable snapshot.
- The bounded opt-in live test and the environment-gated full-corpus workflow
  are discoverable under `Category=Live`. The multi-gigabyte acceptance passed
  on 2026-07-04, retains its chosen data directory, and records only redacted
  aggregate evidence.

## North-Star Acceptance

- Player outcome: an LLM can resolve a local deck to rich card, printing,
  ruling, and community-tag evidence with provenance and explicit freshness.
- Determinism: a snapshot ID replays byte-identically; cache selection follows
  the documented TTL and freshness policy.
- Unknown states: missing corpus, stale cache, provider failure, incomplete
  acquisition, corrupt generation, absent tag group, and not-cached query remain
  distinct.
- Decision boundary: no card role, category meaning, recommendation, or search
  membership is inferred.
- Offline boundary: `cache-only` performs no network request and returns an
  explicit miss when authoritative evidence is absent.
- Mode boundary: `read-only` performs zero persistence or pacing writes; the
  default `local` mode is required to acquire and record new Scryfall evidence.

## Guardrail Conformance

This child returns provider facts and separately labeled community evidence; it
does not classify deck roles, choose categories, recommend cards, or evaluate
uncached arbitrary queries locally. It uses only `scryfall_*`, the `scryfall`
toolset, the three program operation modes, one unified `scryfall.db`, explicit
bulk synchronization, deterministic offline tests, and the clean-break data
root. It adds no migration or compatibility alias.

## Planning Approval

- Status: Approved
- Reviewed by: Repository owner
- Review date: 2026-07-04
- Reviewed revision: `aa46416`
- Implementation authorized: Yes
