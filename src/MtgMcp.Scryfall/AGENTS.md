# Scryfall Adapter Instructions

Root and `src/AGENTS.md` remain authoritative. This file adds defaults for
`MtgMcp.Scryfall`.

## Provider Boundary

- Use only the official Scryfall API and bulk-data contracts. Do not add
  Tagger-site HTML, CSRF, GraphQL, or browser automation.
- Keep arbitrary search expressions provider-authoritative. Exact request
  snapshots may be reused, but the partial local corpus must never approximate
  Scryfall query membership or ordering.
- Send honest product/version and Accept headers, coordinate starts through
  the shared SQLite pacing table, stop immediately on 403 or 429, and retain
  bounded retries for transport and 5xx failures only.
- Validate pagination and download origins before forwarding provider headers.
  Never expose local paths, response fragments, or transient download URLs in
  errors.

## Evidence And Persistence

- Keep card facts, community tags, prices/ranks, corpus generations, and exact
  request snapshots visibly distinct even though they share `scryfall.db`.
- Preserve raw provider objects and explicit absent-versus-empty projections.
  Use the provider dataset timestamp, not merely local download time, when
  deciding whether bulk price/rank evidence is stale.
- Corpus synchronization is explicit. Ordinary reads and process startup must
  never initiate bulk downloads or background refresh.
- Preserve active-plus-previous corpus activation, immutable content-addressed
  snapshots, schema checksum validation, crash-expiring leases, and atomic
  failure behavior.
- Tag coverage labels must describe what was actually joined. Never present a
  filtered tag result as a complete assignment set or a community tag as card
  oracle truth.

## Validation

- Keep normal tests offline with fake HTTP, clocks, compressed JSONL, and
  temporary databases. Mark bounded provider and full-corpus checks `Live`.
- Test current official contract changes with sanitized fixtures before
  accepting them. Full-corpus acceptance requires an explicit retained data
  directory and must never run as part of ordinary CI.
