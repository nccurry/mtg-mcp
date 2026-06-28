# <Feature Name> Implementation Plan

Use this document to define the order of work: which requirements are solved
first, second, third, and what evidence proves each phase is complete.

## Document Control

- Lifecycle status: Planned
- PLC packet: <link to packet README>
- Owner: mtg-mcp
- Last updated: <YYYY-MM-DD>
- Related SRD: <link>
- Related SADD: <link>

## Implementation Strategy

Summarize the delivery approach. Explain the smallest useful slice, major
dependencies, and why this order reduces risk.

Plan reviewable phases around focused validation. Name any expected Core, App,
adapter, MCP surface, config, persistence, docs, generated artifact, or
downstream client impact.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Phase 1 | <Smallest useful slice> | <REQ IDs> | <Projects/files> | <Checks> | <What must be true before continuing> | Planned |

## Phase Details

### Phase 1: <Name>

- Problems solved: <Which SRD problems/requirements this phase addresses>
- Included requirements: <REQ IDs>
- Out of scope for this phase: <Explicit deferrals>
- Expected edits: <Projects, files, systems>
- Validation: <Commands, tests, inspections>
- Exit criteria: <Objective completion criteria>
- Rollback or fallback: <How to unwind if the phase fails>
- Cleanup: <Obsolete or duplicate code removed in this phase>

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| <Risk> | <Phases> | <Mitigation> | <Owner> |

## Completion Criteria

- [ ] Every Must requirement from the SRD appears in at least one phase.
- [ ] Dependencies between phases are explicit.
- [ ] Phase 1 is useful without requiring all later phases.
- [ ] Every phase has validation and exit criteria.
- [ ] MCP surface, operation-mode, docs, and public contract changes are tested
      or explicitly deferred for each affected area.
- [ ] Provider and adapter changes use fixture-backed tests and keep live tests
      opt-in.
- [ ] Validation uses Task commands rather than one-off shell commands where
      Task has an equivalent.
- [ ] Documentation and readability cleanup are included in the relevant phase.
- [ ] Core/App/adapter boundaries stay aligned with repo architecture.
- [ ] Deferred work is captured in the SRD, SADD, or follow-up plans.
