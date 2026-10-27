# Stats Lab Interaction Readiness Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Stats Lab, recommendation, and MCP surface maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Extend the existing turn observation with four explicit booleans, aggregate two new rate series, and add one turn-four scenario and scorecard dimension. A closed precedence table classifies failures. Existing metrics remain independently computed through 0.9.

## Goals, Non-Goals, And Design Drivers

The design must separate density, current-hand availability, mana/color access, and sequencing; reuse existing mana and land facts; remain deterministic; and preserve public Stats Lab keys. It does not share the goldfish combat kernel.

## Context And Scope

Stats Lab is a heuristic analyzer with its own development policy. It receives metadata from card snapshots and land entry facts. Recommendations and comparisons consume its scorecard and scenario results.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Rename held-up metric | Replace old value | Simple end state | Breaks clients and loses compatibility | Rejected |
| Infer failure from seen/held | Derive without new state | Small storage | Misclassifies previously cast cards | Rejected |
| Add checkpoints | Record state at two turn phases | Exact and additive | More aggregation fields | Chosen |

## Chosen Design

### Turn checkpoints

For each turn:

1. Draw according to existing play/draw policy.
2. Play a land according to existing heuristic.
3. Record SeenByEndOfTurn if interaction has entered any observed zone by this point.
4. Record InHandBeforeSpending if an Interaction-role card is currently in hand.
5. Record CastableBeforeSpending if at least one such card can legally be paid from currently available sources.
6. Execute existing proactive and command-zone development.
7. Record HeldAndPayableAfterDevelopment if an interaction remains in hand and can be paid from unexhausted sources.

Protection and wipe roles do not become Interaction in this packet.

### Failure precedence

When HeldAndPayableAfterDevelopment is false, increment exactly one:

| Order | Condition | Bucket |
| ---: | --- | --- |
| 1 | SeenByEndOfTurn is false | neverSeen |
| 2 | InHandBeforeSpending is false | previouslySeenButUnavailable |
| 3 | CastableBeforeSpending is false | inHandButUncastable |
| 4 | Otherwise | castableButNotHeld |

This classifies interaction cast on an earlier turn as previously seen but unavailable rather than mana failure.

### Metric aggregation

interaction-in-hand-by-turn and interaction-castable-by-turn are success count divided by completed simulations for each executed turn. The turn-four scenario reads the castable turn-four element. interaction-access scores the castable curve against documented targets. The old readiness dimension continues to read the old held-up metric through 0.9.

### Downstream propagation

Comparisons add candidate-minus-baseline deltas for the new metrics/dimension. Recommendation scoring uses interaction-access when recommending interaction density/mana access changes, with reason text keyed to the failure buckets. Trace summaries add checkpoint and failure counters within current limits. Calibration identifies all affected scenarios explicitly.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| InteractionTurnObservation | Four checkpoint booleans | Simulation turn | Core internal | Hand/mana state | State tests |
| InteractionFailureKind | Closed failure alternatives | Failed checkpoint | Trace/aggregate | Observation | Exhaustiveness tests |
| Stats accumulator | Per-turn rates/counts | Analysis run | Metrics | Observations | Exact-rate tests |
| Score/scenario calculators | New headline values | Result | Public fields | Metrics | Score tests |
| Downstream adapters | Deltas/recommendations/presentation | Request | Public outputs | Core result | Integration tests |

## Runtime And Data Flow

The analyzer records observations without changing development decisions. Accumulators update primitive counts, then freeze rate arrays after all simulations. Cancellation and completed-run denominators follow existing analyzer policy. Comparison and recommendation services consume the frozen result.

## MCP Surface, Schemas, And Diagnostics

Successful Stats Lab outputs add:

| Name | Container and exact shape | Null | Semantics |
| --- | --- | --- | --- |
| interaction-in-hand-by-turn | One PerformanceProbability row per executed turn: name string, turn integer, probability double, lowConfidenceInterval double, highConfidenceInterval double, sampleSize integer | Row fields never null | Current-hand success rate |
| interaction-castable-by-turn | One PerformanceProbability row per executed turn with the same scalar shape | Row fields never null | Pre-spend castable success rate |
| interaction-castable-by-turn-4 | One ScenarioPerformance row: name string, targetTurn integer 4, successRate/confidence bounds double, sampleSize integer, relevantCards string array, failureDrivers string array, failureDriverCounts integer map | Row fields never null; arrays/maps may be empty | Turn-four castable rate |
| interaction-access | One PerformanceScorecardDimension row: name string, score double from 0 to 1, sourceMetric string, rationale string | Fields never null | Castable-curve score |

Existing fields stay unchanged. Detailed traces use stable failure names and existing row limits.

## Adapter And Provider Contracts

None. Metadata dependency is cross-linked to card-snapshot-integrity; land facts depend on land-entry-classification.

## Cross-Cutting Concepts

Reuse PerformanceMana payment logic and LandEntryClassifier facts. Deterministic ordering, seed policy, cancellation, and output bounds follow current Stats Lab conventions. Comparisons calculate both decks with identical settings.

## Project Boundaries

Stats logic remains in Core; App owns schema descriptions and presenters. The conservative goldfish kernel remains separate. No new project references.

## Readability And Documentation

Use a named observation value and exhaustive switch for failure classification. Avoid four loosely related accumulator booleans spread across the turn loop. XML comments must name measurement timing.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| SLI-REQ-001, SLI-REQ-002 | Ordered checkpoint pipeline | SLI-FIX-001 to SLI-FIX-006 |
| SLI-REQ-003 to SLI-REQ-005 | Explicit aggregate/scenario/score | SLI-FIX-007 to SLI-FIX-009 |
| SLI-REQ-006 | Closed precedence table | SLI-FIX-001 to SLI-FIX-005 |
| SLI-REQ-007, SLI-REQ-008 | Additive downstream migration | SLI-FIX-010 to SLI-FIX-014 |
| SLI-REQ-009 | Stable counters and existing bounds | SLI-FIX-015 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Analyzer state/payment tests | SLI-REQ-001, SLI-REQ-002, SLI-REQ-006 | Checkpoint and bucket matrix passes |
| 2 | Metrics/scenarios/score/traces | SLI-REQ-003 to SLI-REQ-005, SLI-REQ-009 | Exact result/replay tests pass |
| 3 | Comparisons/recommendations/presenters | SLI-REQ-007, SLI-REQ-008 | New outputs plus legacy snapshots pass |
| 4 | Calibration/docs/broad gates | All | Calibration and task gates pass |

## Test Architecture

Deterministic microdecks isolate never drawn, previously cast, color blocked, proactively spent, and successfully held interaction. Payment tests share real helper behavior. Aggregate tests use fixed seeds and exact small-run counts. Downstream tests assert deltas, reasons, presentation, compatibility, trace bounds, and cancellation.

## Framework And External Notes

No external services are needed. Frozen local calibration decks are the only completion evidence.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Legacy metrics coexist | Decision | Temporary parallel concepts | Preserve through 0.9 |
| Protection semantics | Deferred | Protection is not auto-counted | Separate role-policy change |
| Calibration drift | Risk | Score/recommendation changes | Phase 4 before/after evidence |

## Glossary

- Pre-spend: after draw and land play, before proactive spell or command-zone spending.
- Held: still in hand and payable after development.
- Previously seen but unavailable: observed earlier but absent from hand at the pre-spend checkpoint.
