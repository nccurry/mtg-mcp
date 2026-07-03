# Stats Lab Interaction Readiness Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Stats Lab, recommendation, and MCP surface maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

The current held-up metric is observed only after development and cannot say whether interaction was never drawn, already spent, color-blocked, or voluntarily consumed by sequencing. This PLC adds explicit turn checkpoints, additive metrics, and mutually exclusive failures while retaining current public keys through 0.9.

## Audience

Maintainers and clients of Stats Lab scorecards, comparisons, recommendations, traces, and calibration.

## References

- src/MtgMcp.Core/DeckPerformanceAnalyzer.Simulation.cs
- src/MtgMcp.Core/DeckPerformanceAnalyzer.Scorecard.cs
- src/MtgMcp.Core/PerformanceMana.cs
- docs/stats-lab-metrics.md
- [Card snapshot dependency](../card-snapshot-integrity/README.md)
- [Land classification dependency](../land-entry-classification/README.md)
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Explain access failures | Every failed turn lands in exactly one named bucket | Buckets are exhaustive |
| Measure usable interaction | Castable is measured after draw/land, before proactive spending | Uses legal payment helper |
| Preserve clients | Existing keys and semantics remain through 0.9 | New keys are additive |
| Align recommendations | Comparisons and recommendations use the new access signal | Legacy values remain visible |

## System Overview

Stats Lab simulates heuristic development by turn. The analyzer records state after draw and land play, then chooses proactive development, then observes remaining mana. Aggregators produce by-turn rates, turn-four scenarios, scorecards, comparisons, recommendations, traces, and calibration reports.

## Scope And Non-Scope

- In scope: four interaction checkpoints, additive metrics/scenario/dimension, disjoint failures, downstream migration, calibration, presenters, and docs.
- Out of scope: goldfish combat, protection semantics, and removal of legacy metrics.
- Compatibility target: interaction-seen-by-turn, interaction-held-up-by-turn, hold-up-interaction-by-turn-4, and interaction-readiness remain available with current semantics through 0.9.
- Explicit non-goals: redefining board wipes or protection as Interaction, or forcing Stats Lab through the goldfish kernel.

## Stakeholders And Affected Systems

Core performance analyzer and mana helper, scorecard/scenario models, comparisons, recommendation scoring, calibration baselines, trace output, App presenters/surface tests, stats-lab docs, and MCP clients.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| SLI-REQ-001 | Must | State | Each simulated turn shall record interaction seen by end of turn, present in hand before spending, castable after draw and land play before proactive or command-zone spending, and held/payable after development. | The checkpoints answer different questions. | SLI-FIX-001 to SLI-FIX-005 have exact booleans. |
| SLI-REQ-002 | Must | Semantics | Pre-spend castability shall use current-hand interaction and legal available mana/color after the turn draw and land play, before any proactive spell or command-zone cost. | This isolates access from sequencing. | SLI-FIX-003, SLI-FIX-004, SLI-FIX-006 pass. |
| SLI-REQ-003 | Must | Metrics | Stats Lab shall add interaction-in-hand-by-turn and interaction-castable-by-turn as per-turn simulation rates from zero to one. | Clients need additive checkpoint data. | SLI-FIX-007 returns exact rates and null policy. |
| SLI-REQ-004 | Must | Scenario | Stats Lab shall add interaction-castable-by-turn-4 based on the turn-four pre-spend castable checkpoint. | A stable headline access scenario is needed. | SLI-FIX-008 maps exactly to turn four. |
| SLI-REQ-005 | Must | Scorecard | Stats Lab shall add interaction-access derived from the castable-by-turn target curve, while retaining interaction-readiness unchanged through 0.9. | New scoring should use the corrected signal without breaking clients. | SLI-FIX-009 proves both dimensions. |
| SLI-REQ-006 | Must | Diagnostics | Failed held interaction shall be classified exactly once in precedence order: never seen; previously seen but unavailable now; in hand but uncastable; castable before spending but not held after development. | Buckets must be disjoint and explain earlier-cast cards. | SLI-FIX-001 to SLI-FIX-005 partition all failures. |
| SLI-REQ-007 | Must | Downstream | Comparison deltas, recommendation scoring, trace counters, calibration affected-scenario lists, and presenters shall include the new access metrics and use them where interaction access is intended. | The corrected metric must propagate consistently. | SLI-FIX-010 to SLI-FIX-013 pass. |
| SLI-REQ-008 | Must | Compatibility | Existing interaction metrics, scenario key, scorecard dimension, JSON types, and documented semantics shall remain through 0.9. | These are public non-goldfish contracts. | SLI-FIX-014 compatibility snapshots pass. |
| SLI-REQ-009 | Must | Determinism | New rates, failures, comparisons, recommendations, and traces shall be deterministic for the same seed, and trace rows/counters shall stay within existing output bounds. | Diagnostics must remain reproducible and bounded. | SLI-FIX-015 replay and bound tests pass. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Requirements are atomic.
- [x] Rates and checkpoints are precisely defined.
- [x] Shared helper reuse is a correctness constraint.
- [x] No unresolved items remain.

## Interfaces, Data, States, And Modes

New by-turn values are mean success rates across simulations at each turn and are non-null for simulated turns. interaction-castable-by-turn-4 is a rate using the same numerator as turn 4 of interaction-castable-by-turn. interaction-access is a normalized scorecard dimension using documented target curves. Existing fields remain non-null/nullable exactly as today. Comparison deltas use candidate minus baseline. Failure counts sum to total failed held-up checkpoints.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Compatibility | Existing 0.9 response consumed | Old keys/types/semantics unchanged |
| Exhaustiveness | Held-up failure analyzed | Exactly one bucket increments |
| Determinism | Same fixture and seed replayed | Identical metrics, traces, recommendations |
| Bounded output | Maximum detail requested | Existing row and trace limits are not exceeded |
| Offline testability | Calibration and E2E | Frozen local decks only |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Turn state/checkpoints | SLI-REQ-001, SLI-REQ-002, SLI-REQ-006 | State and partition fixtures pass |
| 2 | Additive metrics/score/traces | SLI-REQ-003 to SLI-REQ-005, SLI-REQ-009 | Metric, scenario, score, replay tests pass |
| 3 | Downstream migration | SLI-REQ-007, SLI-REQ-008 | Comparison/recommendation/presenter compatibility passes |
| 4 | Calibration/docs/broad gates | All | Calibration reviewed; surface, lint, test pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| SLI-REQ-001 | Turn checkpoints | Simulation state tests | SLI-FIX-001 to SLI-FIX-005 |
| SLI-REQ-002 | Castability | Mana/turn ordering tests | SLI-FIX-003, SLI-FIX-004, SLI-FIX-006 |
| SLI-REQ-003 | Metric aggregation | Exact-rate tests | SLI-FIX-007 |
| SLI-REQ-004 | Scenario/scorecard | Scenario test | SLI-FIX-008 |
| SLI-REQ-005 | Scenario/scorecard | Scorecard test | SLI-FIX-009 |
| SLI-REQ-006 | Failure classification | Partition tests | SLI-FIX-001 to SLI-FIX-005 |
| SLI-REQ-007 | Downstream propagation | Integration/surface tests | SLI-FIX-010 to SLI-FIX-013 |
| SLI-REQ-008 | Compatibility | JSON snapshot/docs tests | SLI-FIX-014 |
| SLI-REQ-009 | Determinism and bounds | Replay/bounds tests | SLI-FIX-015 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Calibration values move | Risk | Recommendation thresholds may shift | Stats Lab | Record before/after frozen-deck evidence in phase 4 |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused Stats Lab and recommendation tests, calibration report, App surface/E2E tests, task surface:report, task lint, task test, docs inspection, and git diff --check.

## Definition Of Done

- [ ] Must requirements implemented or explicitly deferred.
- [ ] Acceptance and calibration evidence recorded.
- [ ] Traceability current.
- [ ] SADD reflects implementation.
- [ ] Residual metric risks recorded.
