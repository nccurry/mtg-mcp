# Manual Deck Interchange Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-06
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Add native JSON contract and exact round trip. | XCHG-001 through XCHG-005, XCHG-016 | Catalog/schema and golden/property tests pass. | Completed |
| 2 | Add generic text preview/export. | XCHG-002 through XCHG-006, XCHG-012, XCHG-013, XCHG-015, XCHG-016 | Parser/formatter, partial-opt-in, and boundary tests pass. | Completed |
| 3 | Add Archidekt artifact bundle. | XCHG-007, XCHG-008, XCHG-011, XCHG-017 | Exact bundle golden and dated manual acceptance pass. | Completed |
| 4 | Add Moxfield Bulk Edit bundle. | XCHG-009 through XCHG-011, XCHG-017 | Exact bundle golden and dated manual acceptance pass. | Completed |
| 5 | Add the four MCP wrappers, one interchange catalog, and current north-star validation. | XCHG-003, XCHG-014 through XCHG-018 | Current mode surface, dummy-deck workflow, bounds, manual-record, and full offline gates pass; the dependent toolset child owns profile filtering. | Completed |

## Rules

- Do not add or call provider HTTP clients.
- Keep provider syntax in small formatter modules with dated acceptance notes.
- Treat the 2026-07-03 syntax research as candidate evidence; enable each
  provider formatter only after its manual disposable-deck record passes.
- Any UI drift changes the preservation state to unsupported until reverified.
- Never weaken native losslessness to match a provider text format.
- Do not restore direction-duplicated catalog aliases or add a generic router.

## Rollback

Formats are additive. A drifted provider formatter can be removed from the
format catalog without affecting native JSON or local decks.
