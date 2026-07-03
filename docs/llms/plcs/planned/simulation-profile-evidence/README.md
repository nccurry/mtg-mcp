# Simulation Profile Evidence PLC Packet

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/simulation-profile-evidence/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

## Summary

Make automatic simulation-profile selection derive evidence from included primary-category cards, count each card once per signal family, and stop presenting speculative built-in win routes as deck facts.

## Packet Contents

- [SRD.md](SRD.md): profile evidence and compatibility requirements.
- [SADD.md](SADD.md): signal aggregation, routes, and tie-breaking.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): three delivery phases.
- [FIXTURES.md](FIXTURES.md): category, overlap, route, and tie cases.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Reuse DeckCategoryInclusion | Accepted | Inclusion semantics already have a Core owner | [Input selection](SADD.md#input-selection) |
| Deduplicate per signal family | Accepted | Overlapping tags are evidence, not extra cards | [Evidence aggregation](SADD.md#evidence-aggregation) |
| Remove built-in speculative routes | Accepted | Automatic route prose is not source-backed | [Route policy](SADD.md#route-policy) |

## Project And Surface Impact

MtgMcp.Core SimulationProfileCatalog and resolver change, with updated profile tests. App descriptions and docs clarify evidence labels and route provenance. User-authored intent formats remain unchanged.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements have fixtures.
- [x] Alternatives and tradeoffs are recorded.
- [x] Determinism is measurable.
- [x] Core/App boundaries are explicit.
- [x] Surface descriptions are included.
- [x] Adapter impacts are not applicable.
- [x] Existing category helper reuse is required.
- [x] Traceability is complete.
- [x] Phase exits are objective.
- [x] Profile externalization is deferred.

## Implementation Checklist

- [ ] Packet moved to docs/llms/plcs/in-progress/simulation-profile-evidence/.
- [ ] Current phase named.
- [ ] SRD/SADD updated for any selection-policy change.
- [ ] Validation evidence recorded.
- [ ] Duplicate inclusion and route logic removed.
- [ ] Requirements marked complete or deferred.
- [ ] Final review title is outcome-oriented.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Evidence and deterministic tie policy specified |

## Completion Notes

Implementation evidence will be added before completion.
