# Provider Adapters

## Rules

Each adapter owns its provider transport, authentication, pacing, retries,
payloads, and evidence mapping. Core contains no provider transport type.

Adapters must:

- fix the provider origin;
- sanitize errors;
- pass cancellation;
- bound requests and retries;
- stop on blocking responses;
- preserve provider evidence and unknown fields; and
- keep normal tests offline.

## Scryfall

`MtgMcp.Scryfall` uses official API and bulk-data endpoints.

- API starts share a cross-process 500-millisecond minimum interval.
- `403` and `429` stop immediately.
- Transient transport and 5xx failures retry at most twice.
- Arbitrary searches remain provider-authoritative.
- Exact request snapshots are immutable.
- Corpus sync is explicit and never runs in the background.
- All Cards, Rulings, Oracle Tags, and Art Tags share `scryfall.db` but retain
  separate evidence classes.

The TTL defaults to 24 hours. Configure it with
`--scryfall-ttl-hours`, `MTGMCP__SCRYFALL_TTL_HOURS`, or
`SCRYFALL_TTL_HOURS` in `mtg-mcp.json`.

## Archidekt

`MtgMcp.Archidekt` uses an observed, replaceable web contract for explicit
user-owned operations.

- Requests are serialized per configured account.
- Starts are at least two seconds apart.
- At most 30 starts are allowed in 60 seconds.
- One tool invocation has a 150-request budget.
- `403` and `429` stop the operation.
- Ambiguous writes are never retried.
- Read retries are bounded to two transient failures.

Configure credentials with `MTGMCP__ARCHIDEKT__USERNAME` and
`MTGMCP__ARCHIDEKT__PASSWORD`, or use `~/.mtg-mcp/archidekt.json`.
Credentials and session tokens remain in memory and never appear in output.

## Playgroup

`MtgMcp.Playgroup` pins the official Public API 1.0.0 contract.

- Starts are at least 250 milliseconds apart per credential lane.
- GET requests retry transient failures at most twice.
- A `429` retries once only with a bounded `Retry-After`.
- Writes are single-attempt.
- Missing public operations return unsupported results. The adapter does not
  probe private routes.

Configure `MTGMCP__PLAYGROUP__API_KEY`, or use
`~/.mtg-mcp/playgroup.json`. The two public writes remain fixture-only in live
acceptance because the provider exposes no cleanup.

## Commander Spellbook

`MtgMcp.Spellbook` uses the documented public API for variant and saved-deck
combo evidence. It has no account or write operation.

- It uses only `GET /variants/`, `GET /variants/{id}/`, and
  `POST /find-my-combos` at the fixed public origin.
- It sends the caller's source query unchanged apart from URL escaping.
- It makes at most one upstream request per tool call and never follows a
  provider paging link.
- Starts are at least one second apart across processes that share a data root.
- It has no automatic retries. A `429` records a cooldown of at most 60 seconds
  for later calls.
- It uses a fixed 15-second timeout and a 2 MiB response limit.
- `spellbook.db` stores successful exact-response cache entries for 15 minutes
  by default. It stores a request hash, not a separate deck list or query
  column.

Configure the cache with `--spellbook-ttl-minutes`,
`MTGMCP__SPELLBOOK__TTL_MINUTES`, or `SPELLBOOK_TTL_MINUTES` in
`mtg-mcp.json`. Valid values are whole minutes from 1 through 1,440.

The adapter names Commander Spellbook and links to its public site in each
successful result. It returns source evidence and never ranks cards, evaluates
a deck, or recommends a combo.

## Moxfield

There is no Moxfield network adapter. `MtgMcp.Decks` generates manual Bulk Edit
and tag artifacts with explicit preservation limits.

## Tests

Adapter unit tests use fake HTTP and sanitized fixtures. Live tests require
`Category=Live` and explicit provider opt-in. Remote mutation tests use
disposable state with verified cleanup, or remain fixture-only when cleanup is
not available.
