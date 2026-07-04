# Scryfall Corpus And Evidence PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/scryfall-corpus-and-evidence/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: AMEND-004 review

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

- [Proposed AMEND-004](../../in-progress/evidence-first-mcp-rewrite-program/README.md#program-amendments)
- [Completed rewrite foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Completed local deck store](../../completed/local-deck-store/README.md)
- [Completed MCP capability toolsets](../../completed/mcp-capability-toolsets/README.md)

## Current-State Disposition

The current runtime has no Scryfall production assembly, tools, or database.
Reuse the foundation evidence/result contracts, static toolset registration,
standard data root, redaction, packaging, and test wiring. Rebuild provider
transport and persistence from this contract; do not revive the removed legacy
gateway or copy the former snapshot/Tagger child abstractions by default.

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Use official bulk card, ruling, Oracle-tag, and art-tag datasets. | Proposed | One supported provider contract replaces unsupported Tagger scraping and redundant per-card traffic. |
| Store joined evidence in one `scryfall.db`. | Proposed | The datasets share provider, update cadence, identity keys, and rebuild lifecycle. |
| Keep current and previous complete corpus generations. | Proposed | Atomic activation and one explicit rollback remain bounded. |
| Require explicit corpus synchronization. | Proposed | A normal card or query call must never trigger a multi-gigabyte download. |
| Use one configurable 24-hour TTL with explicit freshness policies. | Proposed | Cache eligibility is predictable without expiring immutable evidence. |
| Use authoritative hybrid search behavior. | Proposed | Exact cached searches are reusable; new arbitrary syntax stays owned by Scryfall. |
| Coordinate acquisition and pacing through SQLite. | Proposed | Separate MCP processes sharing one data root must not duplicate work or exceed provider pacing collectively. |
| Preserve raw JSON plus normalized projections. | Proposed | Current tools retain fidelity and a future local-query PLC can add indexes without changing identity contracts. |

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
headers, stay below Scryfall's published ten-requests-per-second ceiling, and
prefer bulk files for large data needs; the child deliberately uses a more
conservative cross-process pace. Bulk prices older than 24 hours are stale
provider evidence, not current sales data.

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

- Status: Draft; AMEND-004 review required
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
