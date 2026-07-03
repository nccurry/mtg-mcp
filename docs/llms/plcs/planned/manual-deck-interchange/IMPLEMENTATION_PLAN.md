# Manual Deck Interchange Implementation Plan

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
| 1 | Add native JSON contract and exact round trip. | XCHG-001 through XCHG-005, XCHG-016 | Catalog/schema and golden/property tests pass. |
| 2 | Add generic text preview/export. | XCHG-002 through XCHG-006, XCHG-012, XCHG-013, XCHG-015, XCHG-016 | Parser/formatter, partial-opt-in, and boundary tests pass. |
| 3 | Add Archidekt artifact bundle. | XCHG-007, XCHG-008, XCHG-011, XCHG-017 | Exact bundle golden and dated manual acceptance pass. |
| 4 | Add Moxfield Bulk Edit bundle. | XCHG-009 through XCHG-011, XCHG-017 | Exact bundle golden and dated manual acceptance pass. |
| 5 | Add MCP wrappers and final validation. | XCHG-003, XCHG-014 through XCHG-017 | Surface/E2E, bounds, manual-record, and full offline gates pass. |

## Rules

- Do not add or call provider HTTP clients.
- Keep provider syntax in small formatter modules with dated acceptance notes.
- Treat the 2026-07-03 syntax research as candidate evidence; enable each
  provider formatter only after its manual disposable-deck record passes.
- Any UI drift changes the preservation state to unsupported until reverified.
- Never weaken native losslessness to match a provider text format.

## Rollback

Formats are additive. A drifted provider formatter can be removed from the
format catalog without affecting native JSON or local decks.
