# Scryfall Tagger Cache Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Tagger` owns `AngleSharp` HTML parsing, cookie/CSRF session state,
GraphQL transport records, a global pacer/circuit breaker, and `tagger.db`.
Core owns only provider-neutral Oracle/printing IDs and evidence records. App
resolves local decks and Scryfall snapshot printing facts before calling the
Tagger acquisition service.

`AngleSharp` is pinned centrally in `Directory.Packages.props`, referenced only
by `MtgMcp.Tagger`, and subject to the repository dependency review and
architecture tests. No HTML or GraphQL type crosses into Core.

### SQLite schema

| Table | Responsibility |
| --- | --- |
| `tag_definitions` | Tagger ID, slug/name/type, description, raw extension JSON, first/last seen. |
| `card_tag_snapshots` | Immutable snapshot ID, Oracle ID, printing, status, source, retrieval, version, checksum, predecessor. |
| `card_tag_assignments` | Snapshot/tag FKs, direct/ancestor relation, accepted/rejected state, ordering, raw extension JSON. |
| `card_latest_snapshots` | Oracle ID to latest completed snapshot; updated transactionally after completion. |
| `acquisition_runs` | Requested/deduplicated/completed/missing/failed counts and sanitized stop reason. |
| `schema_migrations` | Version/checksum/application metadata. |

Old snapshots remain addressable. Rejected associations are stored but returned
only when `includeRejected=true`. Direct and ancestor tags are separate arrays;
an ancestor is never relabeled direct.

### Acquisition flow

1. Validate explicit card/deck scope, required Scryfall snapshot, and bounds.
2. Resolve/deduplicate Oracle IDs and deterministic paper-print candidates.
3. Open one cookie session; GET supported Tagger HTML and parse CSRF metadata.
4. For each Oracle ID, try at most five candidates in fixed order and POST the
   observed `FetchCard` GraphQL operation.
5. Stop on the first recognized card response, cache exact assignments in one
   transaction, and advance latest pointer only after completion.
6. Record `not_present` when all bounded candidates are authoritatively unknown;
   record unavailable/unsupported separately for transport or contract failure.
7. On 403/429, trip the process circuit and stop all remaining work.

The invocation also stops before request 121 or when elapsed time reaches two
minutes, whichever occurs first. It commits each completed card snapshot
independently, returns `budget_exhausted` with completed/not-present/failed/not-
attempted Oracle IDs, and schedules no continuation. With one-second pacing,
the worst-case call is bounded to approximately two minutes rather than the
theoretical 500 requests implied by 100 IDs and five printings.

Cookies and CSRF tokens are process-memory only and never logged or persisted.
The 403/429 circuit is also process-memory only. Restarting clears the circuit,
but no work resumes automatically: a new explicit refresh invocation is still
required and the latest refusal remains visible as sanitized acquisition-run
metadata in `tagger.db`. Persistent cooldown or automatic resume would require
a reviewed follow-up change.

### Public surface

- `tagger_cache_status`
- `tagger_tag_list`
- `tagger_card_tags_get`
- `tagger_deck_tags_get`
- `tagger_refresh_cards`
- `tagger_refresh_deck`

Read outputs include source snapshot IDs and canonical ordering by tag type,
slug, relation, and ID. Deck reads aggregate per-card cache status but do not
combine tags into deck categories or scores.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Scryfall `otag:` searches | Rejected for per-card cache; query membership is not complete card assignment evidence. |
| Bulk crawl the Tagger catalog | Rejected; abusive and unnecessary. |
| Refresh on cache miss | Rejected; reads must remain deterministic and network-free. |
| Share `scryfall.db` | Rejected; unsupported cache has independent lifecycle and deletion risk. |
| Automatically map tags to user categories | Rejected; mapping is an LLM/user decision. |
| Persist session cookies | Rejected; unnecessary credential/security risk. |

## Failure Modes

- Missing printing evidence returns unsupported dependency, no Tagger request.
- Tagger does not know bounded printings returns completed `not_present`.
- Missing CSRF or schema drift returns unsupported and retains prior snapshot.
- Transport failure returns unavailable; the caller may explicitly start a
  later invocation, except the current process remains disabled after 403/429.
- Request/time budget exhaustion returns partial run status with explicit not-
  attempted IDs; completed card snapshots remain valid and addressable.
- Corrupt cache snapshot returns unavailable and never triggers refresh.

## Test Architecture

A fake server provides HTML shells, CSRF metadata, cookies, GraphQL success,
unknown-card, rich tags, ancestors, rejected tags, 403/429, malformed JSON, and
schema drift. Fake clocks prove global serialization. Temporary SQLite tests
prove migrations, immutable snapshots, latest pointers, and corruption handling.
Architecture tests forbid Decks/Scryfall adapter references from Tagger.
Package tests also prove `AngleSharp` is centrally pinned and absent from Core.
