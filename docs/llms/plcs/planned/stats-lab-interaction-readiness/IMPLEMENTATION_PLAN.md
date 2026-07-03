# Stats Lab Interaction Readiness Implementation Plan

> [!CAUTION]
> Historical post-cutover reference only. Do not execute these Stats Lab,
> compatibility, scoring, or calibration phases. A future experimental
> feasibility PLC may adopt only a reviewed subset of the checkpoint/failure
> fixtures after defining its own dependencies.

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Record turn state without changing development policy, then aggregate and expose it additively. Migrate downstream scoring only after the Core result is stable, and recalibrate last.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | State/checkpoint model | SLI-REQ-001, SLI-REQ-002, SLI-REQ-006 | Core analyzer/tests | Microdeck matrix | Four states and failure partition pass | Planned |
| 2 | Metrics/scenario/score/traces | SLI-REQ-003, SLI-REQ-004, SLI-REQ-005, SLI-REQ-009 | Core models/aggregators | Exact/replay/bounds tests | Additive result stable | Planned |
| 3 | Downstream migration | SLI-REQ-007, SLI-REQ-008 | Core services, App | Integration/surface snapshots | New consumers and legacy fields pass | Planned |
| 4 | Calibration/docs/gates | All | Calibration/docs/tests | report/lint/test | Evidence recorded and gates pass | Planned |

## Phase Details

### Phase 1: State and checkpoint model

- Problems solved: current end-of-turn observation conflates four causes.
- Included requirements: SLI-REQ-001, SLI-REQ-002, SLI-REQ-006.
- Out of scope for this phase: public metrics and recommendation changes.
- Expected edits: turn observation, payment call site, failure union, focused tests.
- Validation: SLI-FIX-001 through SLI-FIX-006.
- Exit criteria: every checkpoint and mutually exclusive bucket has a deterministic test.
- Rollback or fallback: leave new observation internal until phase 2; do not alter old metrics.
- Cleanup: remove inferred failure logic replaced by the closed classifier.

### Phase 2: Additive metrics, scenarios, scorecard, and traces

- Problems solved: no public pre-spend access measure.
- Included requirements: SLI-REQ-003, SLI-REQ-004, SLI-REQ-005, and SLI-REQ-009.
- Out of scope for this phase: downstream recommendation weighting.
- Expected edits: result models, accumulators, scenario catalog, scorecard, trace counters.
- Validation: SLI-FIX-007 through SLI-FIX-009 and SLI-FIX-015.
- Exit criteria: exact rates, turn-four mapping, score, determinism, and bounds pass.
- Rollback or fallback: remove additive output while keeping internal observations.
- Cleanup: centralize checkpoint-to-rate aggregation.

### Phase 3: Recommendation and comparison migration

- Problems solved: downstream users still interpret held-up as access.
- Included requirements: SLI-REQ-007, SLI-REQ-008.
- Out of scope for this phase: removing any old key.
- Expected edits: comparison models, recommendation scoring/reasons, presenters, surface/E2E snapshots.
- Validation: SLI-FIX-010 through SLI-FIX-014.
- Exit criteria: new deltas/reasons are exact and all legacy snapshots remain compatible.
- Rollback or fallback: downstream logic may temporarily continue old scoring while additive metrics remain.
- Cleanup: remove duplicate post-development access explanations.

### Phase 4: Calibration, docs, and broad validation

- Problems solved: thresholds and docs may lag corrected semantics.
- Included requirements: all SLI requirements.
- Out of scope for this phase: goldfish or role-policy changes.
- Expected edits: calibration baselines, affected-scenario lists, docs, validation evidence.
- Validation: frozen calibration run, task surface:report, task lint, task test.
- Exit criteria: before/after rationale is recorded and all offline gates pass.
- Rollback or fallback: keep old thresholds if evidence is inconclusive; record the deferral without removing metrics.
- Cleanup: remove docs that describe held-up as pre-spend castability.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Measurement accidentally occurs after spending | 1-2 | Ordered microdeck and trace assertions | Stats Lab |
| Earlier-cast card called mana failure | 1 | Current-hand precedence fixture | Stats Lab |
| Legacy field semantics drift | 2-4 | Frozen compatibility snapshots | App |
| Calibration overfits Jasmine | 4 | Use existing multi-deck calibration suite | Stats Lab |

## Completion Criteria

- [x] Every Must requirement appears in a phase.
- [x] Dependencies on metadata and land classification are explicit.
- [x] Phase 1 is useful internally.
- [x] Every phase has objective validation.
- [x] MCP compatibility is tested.
- [x] No provider work is required.
- [x] Task commands are used where available.
- [x] Documentation cleanup is included.
- [x] Core/App boundaries remain aligned.
- [x] Goldfish/protection deferrals are recorded.
