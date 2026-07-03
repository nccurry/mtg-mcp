# Stats Lab Interaction Readiness PLC Packet

> [!WARNING]
> **Rewrite disposition: superseded/post-cutover experimental — do not implement
> before `0.9.0`.** Stable statistics are exact and exclude sequencing; this
> packet extends a removed heuristic simulator and depends on superseded card-
> snapshot and land-classifier mechanisms. Preserve only the disjoint failure
> taxonomy and access-versus-sequencing distinction for a future
> [`experimental-goldfish-feasibility`](../../in-progress/evidence-first-mcp-rewrite-program/README.md#post-cutover-registry)
> PLC. Reviewed against the rewrite on 2026-07-03; lifecycle movement is
> deferred to authorized foundation implementation.

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/stats-lab-interaction-readiness/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: post-cutover reference; implementation retired

## Summary

Add explicit pre-spend and post-development interaction checkpoints so Stats Lab can distinguish density, current-hand access, mana/color access, and sequencing failures without breaking existing 0.9 metrics.

## Packet Contents

- [SRD.md](SRD.md): metric, checkpoint, and compatibility requirements.
- [SADD.md](SADD.md): turn timing, bucket precedence, and downstream design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): four delivery phases.
- [FIXTURES.md](FIXTURES.md): deterministic turn-state and calibration cases.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Measure before and after proactive spending | Accepted | Mana access and sequencing are different failures | [Turn checkpoints](SADD.md#turn-checkpoints) |
| Add new metrics and retain old ones | Accepted | Stats Lab is public beyond goldfish | [Compatibility](SRD.md#scope-and-non-scope) |
| Keep Stats Lab heuristic | Accepted | One goldfish kernel does not replace this analyzer | [Boundaries](SADD.md#project-boundaries) |

## Project And Surface Impact

MtgMcp.Core Stats Lab simulation, scorecard, comparison, recommendations, traces, and calibration change. App presenters and documentation add metrics and preserve old keys through 0.9. No goldfish contracts change.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements have acceptance cases.
- [x] Alternatives and tradeoffs are recorded.
- [x] Metric semantics are measurable.
- [x] Core/App boundaries are explicit.
- [x] Public compatibility is additive through 0.9.
- [x] Adapter impacts are not applicable.
- [x] Shared mana and land helpers are reused.
- [x] Traceability is complete.
- [x] Phase exits are objective.
- [x] Protection and goldfish behavior are deferred.

## Implementation Checklist

- [x] Independent lifecycle move retired; packet remains post-cutover reference pending foundation reconciliation.
- [ ] Current phase named.
- [ ] SRD/SADD updated for metric policy changes.
- [ ] Validation/calibration evidence recorded.
- [ ] Duplicate metric computations removed.
- [ ] Requirements marked complete or deferred.
- [ ] Final review title is outcome-oriented.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Checkpoint and disjoint failure semantics specified |

## Completion Notes

Do not move this packet to `in-progress`. A future feasibility packet must
re-establish any needed experimental snapshot/source model and sequencing
kernel. Recommendation-score propagation remains excluded unless a separate
experimental decision-model PLC explicitly approves it; legacy `0.9` key
compatibility does not constrain the clean rewrite.
