# Provider Evidence Workflows Architecture And Design

## Constraints And Strategy

Each adapter owns authentication, URI construction, wire DTOs, retry, pacing,
cache behavior, and provider-specific error mapping. Core owns a compact
normalized evidence vocabulary. App owns MCP descriptions, bounded schemas,
detail levels, operation-mode visibility, and secret-safe presentation.

Normalization preserves meaning; it does not erase source identity or combine
incompatible populations.

## Building Blocks

- Adapter response DTOs that mirror documented provider contracts.
- Adapter evidence mapper that attaches provider key, retrieval/cache state,
  freshness inputs, sample/population fields, and permission sensitivity.
- Core evidence record for source identity, observed values, limitations, and
  typed availability state.
- Separate Core types for raw Playgroup observations and heuristic local-meta scores.
- App presenters that group rows by source and bound detail without dropping caveats.
- Existing `OperationModeGuard`, redaction, checkpoint, and writeback boundaries for Archidekt.

## Runtime And Data Flow

1. App requests a named source through the owning adapter/service.
2. The adapter checks credentials and cache policy before supported HTTP calls.
3. Provider payloads are mapped to normalized evidence with retrieval context.
4. Core computes only deterministic summaries explicitly labeled as derived.
5. App groups evidence by source/population and includes availability warnings.
6. Archidekt writes take a separate apply-only path with checkpoint and sanitized errors.

## Source Semantics

| Source | Classification | Important limits |
| --- | --- | --- |
| Scryfall card fields | Source facts | Provider freshness and missing metadata still matter. |
| Workspace contents | Source facts | Represents saved/imported state at a point in time. |
| Scryfall Tagger | Source evidence | Human/community classification, not oracle truth. |
| EDHREC | Source evidence | Population-specific popularity, not quality. |
| TopDeck/EDHTop16 | Source evidence | Tournament and format selection effects apply. |
| Playgroup | Source evidence | Local observed games; permission and sample size matter. |
| Local-meta score | Heuristic inference | Must reference but remain separate from observations. |

## Failure Modes

| Failure | Response |
| --- | --- |
| Missing credentials | Permission-sensitive unavailable state; no secret echo. |
| Rate limit or transient failure | Bounded retry/cache policy and typed partial source status. |
| Stale cache | Return stale evidence only when policy permits and label it. |
| Missing sample metadata | Preserve unknown; do not infer zero. |
| Provider schema drift | Adapter fixture fails; Core contract remains stable. |
| Archidekt write conflict | Stop with sanitized conflict/checkpoint guidance. |

## Alternatives

- One blended popularity score: rejected because source populations and biases
  differ; any future blend must be an explicit versioned heuristic model.
- Provider DTOs in Core: rejected because it couples domain logic to wire churn.
- Scraping missing data: rejected because it is brittle, permission-sensitive,
  and difficult to attribute safely.
- One global freshness TTL: rejected because source volatility and cost differ.

## Test Architecture

Adapter tests use fake HTTP and checked-in provider fixtures for happy, stale,
partial, permission, rate-limit, and schema-drift cases. Core tests cover
normalization semantics and non-merging. App tests cover grouping, detail
levels, source statuses, mode visibility, and redaction. Archidekt mutation
tests use fake adapters and never mutate real decks.

## Deferred Work

New provider selection, blended recommendation models, browser automation,
bulk historical ingestion, and public provider credential mutation require
separate review and are not authorized by this packet.
