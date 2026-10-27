# Simulation Profile Evidence Implementation Plan

> [!CAUTION]
> Historical post-cutover reference only. Do not execute these legacy profile
> phases. A future feasibility PLC must first establish that simulation belongs
> in an experimental surface and explicitly adopt the relevant fixtures.

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Correct deck selection and evidence math before changing catalog content. Then remove speculative routes and freeze deterministic selection. App descriptions and docs follow the stable Core behavior.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Correct evidence inputs | SPE-REQ-001, SPE-REQ-002, SPE-REQ-003 | Core/tests | Category/count fixtures | Exact counts pass | Planned |
| 2 | Clean routes and resolver | SPE-REQ-004, SPE-REQ-005, SPE-REQ-006 | Core/tests | Snapshots/permutations | No speculative routes; stable ties | Planned |
| 3 | Surface semantics and docs | SPE-REQ-007, all | App/docs | report/lint/test | Descriptions and broad gates pass | Planned |

## Phase Details

### Phase 1: Inclusion and evidence-count correction

- Problems solved: secondary exclusions and overlapping tag inflation.
- Included requirements: SPE-REQ-001, SPE-REQ-002, and SPE-REQ-003.
- Out of scope for this phase: routes and public descriptions.
- Expected edits: resolver input selector, signal accumulator, focused tests.
- Validation: SPE-FIX-001 through SPE-FIX-006.
- Exit criteria: exact per-family identities and quantities match every fixture.
- Rollback or fallback: retain old scoring only until the additive accumulator tests prove parity for unaffected cases.
- Cleanup: remove duplicate category and role-count branches.

### Phase 2: Route/profile cleanup and resolver tests

- Problems solved: invented automatic routes and unstable ties.
- Included requirements: SPE-REQ-004, SPE-REQ-005, and SPE-REQ-006.
- Out of scope for this phase: goldfish action execution.
- Expected edits: catalog metadata, route handling, labels, stable sorting, tests.
- Validation: SPE-FIX-007 through SPE-FIX-010.
- Exit criteria: built-in routes are empty, intent round-trips, and permutations are identical.
- Rollback or fallback: retain explicit user routes; never restore speculative built-in routes as evidence.
- Cleanup: delete obsolete common-route constants.

### Phase 3: Surface descriptions, docs, and validation

- Problems solved: ambiguous evidence and route language.
- Included requirements: SPE-REQ-007 and regression coverage for all.
- Out of scope for this phase: intent schema changes.
- Expected edits: tool/resource/prompt descriptions, simulation-profiles docs, surface tests.
- Validation: SPE-FIX-011, task surface:report, task lint, task test.
- Exit criteria: public descriptions match labels and all offline gates pass.
- Rollback or fallback: additive label can be removed only with its documentation; corrected Core behavior remains.
- Cleanup: remove docs that call built-in routes observed deck plans.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Threshold meaning shifts | 1-2 | Exact before/after fixture counts | Core |
| User route accidentally scores profile | 2 | Explicit non-scoring test | Core |
| Ordering leaks dictionary order | 2-3 | Permutation tests and ordinal sort | Core |

## Completion Criteria

- [x] Every Must requirement appears in a phase.
- [x] Dependencies are explicit.
- [x] Phase 1 is independently useful.
- [x] Every phase has objective validation.
- [x] Surface and documentation changes are tested.
- [x] Provider work is not applicable.
- [x] Task commands are used where available.
- [x] Cleanup is phase-owned.
- [x] Core/App boundaries stay aligned.
- [x] Deferred execution/externalization is recorded.
