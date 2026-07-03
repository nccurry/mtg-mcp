# Scryfall Tagger Cache Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 0 | Recheck policy/contract and record owner risk acceptance. | TAG-008, TAG-012, TAG-021 | Robots/terms/contract evidence is dated and owner sign-off is complete. |
| 1 | Pin observed HTML/GraphQL fixture contract and adapter-only dependency. | TAG-004, TAG-008, TAG-012, TAG-020 | Sanitized fixture and dependency review pass. |
| 2 | Add SQLite read model and cache-only reads. | TAG-001 through TAG-004, TAG-013, TAG-014, TAG-017 | Temporary-DB tests pass. |
| 3 | Add bounded acquisition, pacing, request/time budgets, and circuit breaker. | TAG-005 through TAG-012, TAG-019 | Fake HTTP/clock/request-count tests pass. |
| 4 | Add card/deck composition and MCP surface. | TAG-015 through TAG-018 | Surface/E2E and architecture tests pass. |
| 5 | Run optional one-card live proof and full offline validation. | All | Live discovery and full gates pass. |

## Rules

- Recheck robots, terms, HTML, and GraphQL before implementation.
- Do not implement acquisition before the owner accepts the provider risk;
  cache-only work may proceed only if separately authorized and dependency-safe.
- Do not loosen one-second, 100-card, or five-print upper bounds through config.
- Do not loosen the 120-request or two-minute invocation bounds through config.
- Never add implicit refresh or retries.
- A policy objection or blocking response disables acquisition, not cache reads.

## Rollback

Disable/unregister refresh while preserving read access to existing snapshots.
`tagger.db` is rebuildable and may be deleted only with explicit user consent.
