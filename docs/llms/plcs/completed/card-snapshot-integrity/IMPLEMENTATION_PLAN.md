# Card Snapshot Integrity Implementation Plan

> [!CAUTION]
> Historical legacy-repair plan only. Do not execute these workspace JSON,
> Moxfield, or refresh-orchestration phases. The rewrite children linked from
> README.md own the retained trust requirements.

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Land the persisted trust model first, then teach each adapter to populate it, then change import and refresh orchestration. This prevents App behavior from depending on an incomplete persistence model and allows every phase to merge with focused offline validation.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Coverage, migration, cloning, fingerprints | CSI-REQ-001, CSI-REQ-002, CSI-REQ-009 | Core models/workspaces | Focused Core tests | Old/new documents and deterministic copies pass | Planned |
| 2 | Provider coverage and readiness | CSI-REQ-003 to CSI-REQ-005 | Archidekt, Moxfield, Scryfall, Core | Offline fixture tests | All provider and multi-face cases pass | Planned |
| 3 | Safe hydration and refresh | CSI-REQ-006 to CSI-REQ-008 | Core services, App tools | Fake repository/provider and surface tests | Failure, cancellation, redaction, and scope cases pass | Planned |
| 4 | Public validation | All | Docs and test inventory | task lint; task test | Documentation and broad offline gates pass | Planned |

## Phase Details

### Phase 1: Coverage model and migration

- Problems solved: ambiguous empty values and state loss during persistence operations.
- Included requirements: CSI-REQ-001, CSI-REQ-002, CSI-REQ-009.
- Out of scope for this phase: provider mapping and App changes.
- Expected edits: Core snapshot models, serializer/upgrader, clone, quality, fingerprint helpers, and tests.
- Validation: CSI-FIX-001 through CSI-FIX-004 and CSI-FIX-013.
- Exit criteria: version table behavior, recursive face copies, and stable fingerprints are proven.
- Rollback or fallback: revert the additive model before any adapter writes schema version 2.
- Cleanup: remove duplicate manual copies that omit newly owned fields.

### Phase 2: Provider mapping and readiness

- Problems solved: demonstrated provider gaps and weak presence-based readiness.
- Included requirements: CSI-REQ-003, CSI-REQ-004, CSI-REQ-005.
- Out of scope for this phase: import sequencing and refresh surface.
- Expected edits: adapter mapping files, sanitized fixtures, readiness evaluator.
- Validation: CSI-FIX-002, CSI-FIX-003, and CSI-FIX-005 through CSI-FIX-008.
- Exit criteria: valid, empty, malformed, dynamic, and multi-face cases have exact expected states.
- Rollback or fallback: retain unknown coverage for any disputed payload path.
- Cleanup: remove superseded presence-only readiness logic.

### Phase 3: Import hydration and refresh

- Problems solved: import loss after hydration failure and unsafe scope fallback.
- Included requirements: CSI-REQ-006, CSI-REQ-007, CSI-REQ-008.
- Out of scope for this phase: broad public documentation.
- Expected edits: workspace import/refresh workflows, scope parser, warnings, App surface tests.
- Validation: CSI-FIX-009 through CSI-FIX-012 plus cancellation tests.
- Exit criteria: raw save precedes hydration, cancellation propagates, errors are redacted, and unknown scopes do no work.
- Rollback or fallback: disable best-effort hydration while preserving raw import; do not restore permissive scope fallback.
- Cleanup: remove implicit default-to-all selection.

### Phase 4: App surface, docs, and validation

- Problems solved: public discoverability and full regression confidence.
- Included requirements: all CSI requirements.
- Out of scope for this phase: live mutations.
- Expected edits: tool/docs descriptions, surface snapshots, validation evidence.
- Validation: focused tests, task lint, task test, task surface:report, link inspection.
- Exit criteria: all gates pass offline and the PLC records evidence.
- Rollback or fallback: revert additive presentation fields independently of persisted coverage.
- Cleanup: remove stale docs describing value-presence readiness.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Old JSON defaults promote facts | 1-2 | Explicit unknown default and migration fixtures | Core |
| Adapter property presence is malformed | 2 | JSON-kind and value validation | Adapter owners |
| Failure path overwrites raw import | 3 | Ordered repository assertions | Workspace owner |

## Completion Criteria

- [x] Every Must requirement from the SRD appears in at least one phase.
- [x] Dependencies between phases are explicit.
- [x] Phase 1 is useful without requiring all later phases.
- [x] Every phase has validation and exit criteria.
- [x] MCP surface, operation-mode, docs, and public contract changes are tested or explicitly deferred.
- [x] Provider changes use fixture-backed tests and keep live tests opt-in.
- [x] Validation uses Task commands where available.
- [x] Documentation and readability cleanup are included.
- [x] Core/App/adapter boundaries stay aligned.
- [x] Deferred work is captured.
