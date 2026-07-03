# Scryfall Evidence Snapshots Implementation Plan

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
| 1 | Add typed requests, source objects, evidence-labeled root/face projections, and pinned collection fixture. | SCRY-001 through SCRY-004, SCRY-008, SCRY-019 through SCRY-021 | Mapping/contract/schema and multi-face coverage tests pass. |
| 2 | Add safe official HTTP client and pacing. | SCRY-010 through SCRY-013, SCRY-016 | Fake HTTP/clock tests pass. |
| 3 | Add immutable SQLite repository, staging cleanup, and tombstone deletion. | SCRY-005 through SCRY-009, SCRY-017, SCRY-018 | Temporary-DB crash/retry/delete tests pass. |
| 4 | Compose acquisition, refresh, and cached reads. | SCRY-002, SCRY-003, SCRY-006, SCRY-014, SCRY-017 | Integration tests pass. |
| 5 | Add `scryfall_*` MCP surface and validation. | SCRY-015 | Surface/E2E and full offline gates pass. |

## Rules

- Move this packet to `in-progress/` before implementation.
- Pin fixture capture dates and sanitize headers/identifiers.
- Do not add random cards, Tagger, local query parsing, or background refresh.
- Never mark a partially acquired snapshot complete.

## Rollback

The module and `scryfall.db` are rebuildable. Rollback unregisters tools and
removes the preview database only with explicit user consent; immutable export
artifacts remain readable as JSON evidence.
