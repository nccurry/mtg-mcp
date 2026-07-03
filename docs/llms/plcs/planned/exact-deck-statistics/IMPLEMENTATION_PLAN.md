# Exact Deck Statistics Implementation Plan

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
| 1 | Add rational/combinations and univariate engine. | STAT-001, STAT-002, STAT-013 through STAT-017 | Known vectors/properties and unsupported-detail snapshots pass. |
| 2 | Add membership buckets and multivariate/package analysis. | STAT-003, STAT-004, STAT-008, STAT-013 | Exhaustive comparisons pass. |
| 3 | Add turn, mana, and closed monotone inverse analysis. | STAT-005 through STAT-007, STAT-010, STAT-018 | Small-deck/payment/neighbor/rejection fixtures pass. |
| 4 | Add mulligan and documented zone-based deck summaries. | STAT-009, STAT-011, STAT-012, STAT-019, STAT-020 | Exhaustive, selector, nearest-rank, and zone-partition tests pass. |
| 5 | Add `stats_*` tools and full validation. | All | Surface/E2E, coverage, lint, and tests pass. |

## Rules

- Move this packet to `in-progress/` before implementation.
- Add exhaustive oracle tests before optimizing each engine.
- Never compare rounded decimals or substitute sampling.
- Reject unsupported semantic inference rather than importing legacy classifiers.

## Rollback

The package is read-only and owns no data. Tools can be unregistered and the
module removed without migration.
