# Conservative Goldfish V2 Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Complete card-snapshot-integrity, land-entry-classification, and simulation-profile-evidence first; deck-count-contracts is preferred but nonblocking. Freeze the Jasmine input and v1 performance before code changes. Build v2 privately, prove Jasmine and wrapper equivalence, then perform one atomic public/downstream cutover and delete both old engines.

The evidence enum prerequisite is owned by
[mcp-trust-evidence phase 4](../mcp-trust-evidence/IMPLEMENTATION_PLAN.md#phase-4-minimal-evidence-tier-vocabulary).

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Freeze fixture and benchmark v1 | CGF-REQ-015, CGF-REQ-016 | Fixtures/Taskfile/docs | Fingerprint and benchmark | Reproducible baseline recorded | Planned |
| 2 | Private compiler/kernel | CGF-REQ-001, CGF-REQ-002, CGF-REQ-003, CGF-REQ-004, CGF-REQ-005, CGF-REQ-008, CGF-REQ-009, CGF-REQ-010, CGF-REQ-011, CGF-REQ-014, CGF-REQ-017 | Core/tests | Compiler/payment/kernel suite | Private analysis is deterministic and bounded | Planned |
| 3 | Jasmine coverage and wrappers | CGF-REQ-006, CGF-REQ-007, CGF-REQ-001 | Core/tests | Jasmine/equivalence/comparison | All supported effects and wrappers agree | Planned |
| 4 | Atomic cutover and deletion | CGF-REQ-012, CGF-REQ-013 | Core/App/docs/tests | Surface/E2E/inventory | Six consumers migrated; obsolete references zero | Planned |
| 5 | Performance and completion | All | Bench/live/docs/gates | Benchmark, live smoke, lint/test | Five seconds and all completion gates pass | Planned |

## Phase Details

### Phase 1: Freeze fixture and formally benchmark v1

- Problems solved: live deck drift and irreproducible performance claims.
- Included requirements: CGF-REQ-015, CGF-REQ-016.
- Out of scope for this phase: kernel implementation.
- Expected edits: sanitized Jasmine workspace/card fixture, manifest/fingerprint, benchmark job, named live-smoke task definition.
- Validation: CGF-FIX-001, CGF-FIX-036, and dry-run discovery of CGF-FIX-037.
- Exit criteria: fixture deck ID, card metadata, fingerprint, commit, machine/runtime, Release command, settings, and 18.4-second baseline are recorded.
- Rollback or fallback: fixture additions can revert without touching production behavior.
- Cleanup: none.

### Phase 2: Internal compiler, mana model, kernel, diagnostics, and tests

- Problems solved: heuristic effects, illegal payment, inconsistent diagnostics, and unbounded replay data.
- Included requirements: CGF-REQ-001, CGF-REQ-002, CGF-REQ-003, CGF-REQ-004, CGF-REQ-005, CGF-REQ-008, CGF-REQ-009, CGF-REQ-010, CGF-REQ-011, CGF-REQ-014, and CGF-REQ-017.
- Entry condition: prerequisite PLCs complete and trust REQ-005 Core vocabulary available, or the enum lands first with reciprocal packet updates.
- Out of scope for this phase: App/public cutover and complete Jasmine effect coverage.
- Expected edits: private Core compiler/union models/payment/kernel/accumulator/results and focused tests.
- Validation: CGF-FIX-002 through CGF-FIX-016 and CGF-FIX-021 through CGF-FIX-026, CGF-FIX-035.
- Exit criteria: all mana/turn/partial-support/no-lethal/determinism/bound/cancellation cases pass; no public surface references v2.
- Rollback or fallback: delete private v2 implementation; v1 public behavior remains intact.
- Cleanup: extract only genuinely shared payment primitives; do not alter Stats Lab behavior.

### Phase 3: Jasmine effect coverage and deterministic wrappers

- Problems solved: frozen deck abilities and cross-wrapper semantic drift.
- Included requirements: CGF-REQ-006, CGF-REQ-007, and wrapper portion of CGF-REQ-001.
- Out of scope for this phase: public tool models.
- Expected edits: compiler abilities/effects, multiplayer policy, internal projection/win/comparison functions, tests.
- Validation: CGF-FIX-002 through CGF-FIX-006, CGF-FIX-017 through CGF-FIX-020, frozen Jasmine analysis.
- Exit criteria: all approved Jasmine effects compile/execute, unsupported effects remain zero contribution, and wrapper/comparison analyses are equal and order invariant.
- Rollback or fallback: mark disputed ability unsupported rather than estimate it.
- Cleanup: consolidate effect-specific decisions into closed effect handlers.

### Phase 4: Atomic MCP/downstream cutover and old-code removal

- Problems solved: public conflicting models and hidden GoldfishSimulationResult consumers.
- Included requirements: CGF-REQ-012, CGF-REQ-013.
- Out of scope for this phase: final performance acceptance/live smoke.
- Expected edits: six tool consumers, request/response models, batch/brainstorm services/models, presenters, prompts, resources, registry, README, CHANGELOG, versioning/migration docs, surface/E2E tests; deletion of old engines/models.
- Validation: CGF-FIX-027 through CGF-FIX-034, task surface:report, focused E2E tests.
- Exit criteria: exact request/output snapshots pass at all detail levels; model parameter is absent; repository search finds no obsolete models/labels/pressure routes.
- Rollback or fallback: revert the entire cutover commit as one unit; no partial surface rollback.
- Cleanup: delete optimistic engine, race-v1, model selector, GoldfishSimulationResult, pressure scores, speculative routes, and obsolete presenters/tests/docs.

### Phase 5: Performance evidence, live smoke, and completion

- Problems solved: completion confidence, performance, and real adapter integration.
- Included requirements: all CGF requirements, especially CGF-REQ-015 and CGF-REQ-016.
- Out of scope for this phase: expanding effect coverage due only to remote deck drift.
- Expected edits: performance tuning without semantic change, evidence tables, final docs.
- Validation: CGF-FIX-036 benchmark, CGF-FIX-037 named read-only smoke, task lint, task test, documentation/link inspection.
- Exit criteria: reference median is at most five seconds; offline gates pass; live smoke records success or explicitly documented external drift; no mutation path executes.
- Rollback or fallback: if performance misses, keep packet in progress and optimize accumulators; do not weaken correctness or caps.
- Cleanup: remove temporary v1/v2 benchmark adapters after final comparative evidence is captured, retaining the v2 benchmark.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Prerequisite semantics change | 2-3 | Require completed packet links/fingerprints | PLC owners |
| Effect support expands into rules engine | 2-3 | Closed fixture-driven effects and non-scope review | Simulation |
| Public cutover becomes partial | 4 | One migration checklist/commit and obsolete inventory gate | App/Core |
| Benchmark optimized by semantic shortcut | 5 | Replay fingerprints and acceptance fixtures before/after | Simulation |
| Live deck changes | 1, 5 | Frozen fixture is truth; smoke reports drift only | Archidekt |

## Completion Criteria

- [x] Every Must requirement appears in at least one phase.
- [x] Packet prerequisites and trust enum dependency are explicit.
- [x] Phase 1 creates durable evidence independently.
- [x] Every phase has validation and objective exits.
- [x] MCP cutover and operation modes are surface-tested.
- [x] Provider tests are fixture-backed and live validation is named/read-only.
- [x] Task commands are planned for broad gates and new benchmark/live targets.
- [x] Documentation and complete old-code removal are phase-owned.
- [x] Core/App/adapter boundaries remain aligned.
- [x] Full-rules and Stats Lab deferrals are recorded.
