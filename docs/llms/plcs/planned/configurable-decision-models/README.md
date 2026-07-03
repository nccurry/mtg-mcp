# Configurable Decision Models PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/configurable-decision-models/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

## Summary

This packet plans a deterministic, configuration-driven policy layer for
goldfish and other bounded decision models. It extends simulation profiles; it
does not add a general-purpose rules engine or arbitrary executable rules.

## Packet Contents

- [SRD.md](SRD.md): requirements, boundaries, and acceptance criteria.
- [SADD.md](SADD.md): evaluator, configuration, trace, and dependency design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): independently green phases.
- [FIXTURES.md](FIXTURES.md): replay, conflict, budget, and calibration cases.

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Extend simulation profiles. | Proposed | Existing profile ownership is narrower and safer than a new rules framework. |
| Permit only allowlisted declarative predicates and parameters. | Proposed | Configuration must remain auditable, bounded, and free of `eval`. |
| Resolve equal priorities with stable explicit tie-breakers. | Proposed | Deterministic output requires more than deterministic input order. |
| Return typed payload unions for distinct decision outcomes. | Proposed | Chosen, rejected, unsupported, and indeterminate cases carry different evidence. |
| Keep comprehensive Magic rules out of scope. | Accepted | Stack, priority, layers, and replacement effects belong to a different product decision. |

## Project And Surface Impact

Expected work is concentrated in `MtgMcp.Core` simulation policy models and
evaluation, `MtgMcp.App` configuration and bounded presentation, simulation
fixtures, calibration tests, and profile documentation. No third-party rules
engine is planned as a Core dependency.

## Current Open Questions

| Question | Impact | Resolution plan |
| --- | --- | --- |
| Which existing goldfish choices should become configurable first? | Controls the smallest useful phase. | Inventory current hard-coded mulligan, sequencing, and target-selection choices before implementation. |
| Which trace fields belong in summary versus normal/full output? | Affects MCP response size. | Establish fixture-backed output budgets before adding a public schema. |

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements have acceptance criteria.
- [x] Dependency and rules-engine boundaries are explicit.
- [x] Determinism, safety, and replay requirements are traceable.
- [ ] Initial configurable choices and output budgets are approved.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Foundation follow-up packet drafted | Passed | Planning only; no runtime behavior changed. |

## Completion Notes

Move this packet to `in-progress` only when the first configurable choice and
its compatibility contract are approved for implementation.
