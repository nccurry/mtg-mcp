# Land Entry Classification Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Correct and lock the classifier first, then verify all consumers use it, then update calibration notes and broad validation. Each phase is independently reviewable.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Classifier correction | LEC-REQ-001, LEC-REQ-002, LEC-REQ-003, LEC-REQ-004 | Core/tests | Text matrix | Exact classes pass | Planned |
| 2 | Consumer verification | LEC-REQ-005 | Stats/goldfish tests | Focused integration | No parallel parsing | Planned |
| 3 | Documentation and gates | LEC-REQ-006, all | Docs/tests | lint/test/inspection | All gates pass | Planned |

## Phase Details

### Phase 1: Classifier and tests

- Problems solved: reveal/pay/discard consequence text is missed.
- Included requirements: LEC-REQ-001, LEC-REQ-002, LEC-REQ-003, and LEC-REQ-004.
- Out of scope for this phase: calibration and consumer cleanup.
- Expected edits: LandEntryClassifier and table-driven Core tests.
- Validation: LEC-FIX-001 through LEC-FIX-009.
- Exit criteria: every positive, negative, regression, and face case returns its exact enum.
- Rollback or fallback: revert the phrase family as one unit while retaining new regression fixtures.
- Cleanup: consolidate overlapping existing conditions without changing precedence.

### Phase 2: Consumer and calibration-impact verification

- Problems solved: hidden local classification drift.
- Included requirements: LEC-REQ-005.
- Out of scope for this phase: full recalibration.
- Expected edits: Stats Lab and simulation call sites/tests only where reuse is absent.
- Validation: LEC-FIX-010 plus affected focused tests.
- Exit criteria: code inspection finds one classifier owner and consumer assertions pass.
- Rollback or fallback: keep consumer behavior conservative while routing through the classifier.
- Cleanup: delete duplicate text checks.

### Phase 3: Documentation and broad validation

- Problems solved: correctness-change discoverability.
- Included requirements: LEC-REQ-006 and regression coverage for all.
- Out of scope for this phase: public schema changes.
- Expected edits: stats-lab-metrics and simulation behavior docs, validation evidence.
- Validation: LEC-FIX-011, task lint, task test, git diff --check.
- Exit criteria: docs describe classification-only semantics and all offline gates pass.
- Rollback or fallback: documentation reverts with code if classification is reverted.
- Cleanup: remove stale statements that reveal lands are normally untapped.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Broad phrase false positive | 1 | Require choice and consequence clauses; negative fixtures | Core |
| Consumer metric drift | 2-3 | Focused tests and calibration impact note | Stats/simulation |

## Completion Criteria

- [x] Every Must requirement appears in a phase.
- [x] Phase dependencies are explicit.
- [x] Phase 1 is independently useful.
- [x] Each phase has objective validation.
- [x] No MCP contract change is hidden.
- [x] Provider changes are not applicable.
- [x] Task commands are used where available.
- [x] Documentation cleanup is included.
- [x] Core ownership is preserved.
- [x] General rules parsing is explicitly excluded.
