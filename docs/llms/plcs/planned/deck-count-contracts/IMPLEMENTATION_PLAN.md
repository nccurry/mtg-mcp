# Deck Count Contracts Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Prove the partition as a pure Core value before exposing it. Add the same value to every surface in one phase, while snapshotting the untouched legacy contract.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Core count value | DCC-REQ-001, DCC-REQ-002, DCC-REQ-003, DCC-REQ-004 | Core/tests | Count matrix | Invariants and edge cases pass | Planned |
| 2 | Additive MCP output | DCC-REQ-005, DCC-REQ-006 | App/tests | Surface and E2E tests | Three surfaces agree; legacy unchanged | Planned |
| 3 | Docs and validation | All | Docs/task gates | report, lint, test | Compatibility docs and broad gates pass | Planned |

## Phase Details

### Phase 1: Core count value and invariants

- Problems solved: divergent count math and ambiguous category handling.
- Included requirements: DCC-REQ-001, DCC-REQ-002, DCC-REQ-003, and DCC-REQ-004.
- Out of scope for this phase: MCP response edits.
- Expected edits: Core count model, partition function, and unit tests.
- Validation: DCC-FIX-001 through DCC-FIX-007.
- Exit criteria: every fixture has exact counts and both invariants hold.
- Rollback or fallback: remove the unused additive Core type before App integration.
- Cleanup: consolidate any Core-local duplicate canonical partition logic.

### Phase 2: Additive MCP output

- Problems solved: clients lack a reliable public partition.
- Included requirements: DCC-REQ-005, DCC-REQ-006.
- Out of scope for this phase: legacy removal or renaming.
- Expected edits: workspace and deck summary response models/presenters, surface snapshots, E2E tests.
- Validation: DCC-FIX-008 and DCC-FIX-009.
- Exit criteria: identical cardCounts and byte-for-byte compatible legacy fields.
- Rollback or fallback: remove only additive cardCounts fields; Core value may remain.
- Cleanup: use one presentation helper where existing architecture supports it.

### Phase 3: Compatibility documentation and broad validation

- Problems solved: migration discoverability and regression confidence.
- Included requirements: all DCC requirements.
- Out of scope for this phase: future deprecation schedule.
- Expected edits: README/tool docs/version notes and evidence table.
- Validation: task surface:report, task lint, task test, link inspection.
- Exit criteria: all offline gates pass and docs name cardCounts as canonical.
- Rollback or fallback: revert documentation with the corresponding surface change.
- Cleanup: remove contradictory count descriptions.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Included Sideboard classified by name | 1 | Inclusion decision precedes aliases | Core |
| Legacy values accidentally replaced | 2 | Before/after snapshots | App |
| Surface drift | 2-3 | Equality E2E test across all surfaces | App |

## Completion Criteria

- [x] Every Must requirement appears in a phase.
- [x] Phase dependencies are explicit.
- [x] Phase 1 is independently useful.
- [x] Every phase has validation and exit criteria.
- [x] MCP and compatibility changes are tested.
- [x] Provider changes are not applicable.
- [x] Task commands are used where available.
- [x] Documentation cleanup is included.
- [x] Core/App boundaries remain aligned.
- [x] Deferred legacy removal is recorded.
