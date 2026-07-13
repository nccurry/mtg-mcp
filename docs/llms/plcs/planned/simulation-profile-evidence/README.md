# Simulation Profile Evidence PLC Packet

> [!WARNING]
> **Rewrite disposition: superseded/post-cutover experimental — do not implement
> before `0.9.0`.** Automatic simulation-profile selection and its legacy base
> are absent from the stable rewrite. A future
> [`experimental-goldfish-feasibility`](../../completed/evidence-first-mcp-rewrite-program/README.md#post-cutover-registry)
> PLC must absorb this packet with `configurable-decision-models`,
> `stats-lab-interaction-readiness`, and `conservative-goldfish-v2`. Retain only
> the per-family deduplication, stable tie, and no-invented-routes fixtures.
> Reviewed against the rewrite on 2026-07-03; lifecycle movement is deferred to
> authorized foundation implementation.

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/simulation-profile-evidence/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: post-cutover reference; implementation retired

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

- [x] Independent lifecycle move retired; packet remains post-cutover reference pending foundation reconciliation.
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

Do not move this packet to `in-progress`. If simulation feasibility is later
approved, use the rewrite's shared evidence descriptor rather than introducing
a profile-only `evidenceKind`, and require removal of built-in speculative route
claims before exposing any experimental profile output.
