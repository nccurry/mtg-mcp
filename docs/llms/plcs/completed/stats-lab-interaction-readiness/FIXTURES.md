# Stats Lab Interaction Readiness Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| SLI-FIX-001 | Microdeck | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Interaction never seen | Stats Lab | Keep minimal and deterministic |
| SLI-FIX-002 | Microdeck | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Interaction seen/cast earlier, absent now | Stats Lab | Proves current-hand bucket |
| SLI-FIX-003 | Microdeck | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Interaction in hand, wrong colors | Stats Lab | Reuse legal payment |
| SLI-FIX-004 | Microdeck | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Interaction castable before development, mana spent | Stats Lab | Proves sequencing bucket |
| SLI-FIX-005 | Microdeck | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Interaction held and payable | Stats Lab | Success control |
| SLI-FIX-006 | Turn order | tests/MtgMcp.Core.Tests/StatsLabInteractionReadinessTests.cs | Draw, land play, then castability | Stats Lab | Assert event ordering |
| SLI-FIX-007 | Aggregate run | tests/MtgMcp.Core.Tests/StatsLabInteractionMetricsTests.cs | Exact by-turn rates | Stats Lab | Update only with semantics |
| SLI-FIX-008 | Scenario result | tests/MtgMcp.Core.Tests/StatsLabInteractionMetricsTests.cs | New turn-four key | Stats Lab | Exact equality to by-turn index |
| SLI-FIX-009 | Scorecard result | tests/MtgMcp.Core.Tests/StatsLabInteractionMetricsTests.cs | New and legacy dimensions | Stats Lab | Version target changes explicitly |
| SLI-FIX-010 | Comparison | tests/MtgMcp.Core.Tests/StatsLabInteractionDownstreamTests.cs | Candidate-minus-baseline deltas | Core | Pair identical settings/seeds |
| SLI-FIX-011 | Recommendation | tests/MtgMcp.Core.Tests/StatsLabInteractionDownstreamTests.cs | Correct score and reason | Core | Keep one case per failure family |
| SLI-FIX-012 | Trace | tests/MtgMcp.Core.Tests/StatsLabInteractionMetricsTests.cs | Checkpoint/failure counters | Core | Respect current limits |
| SLI-FIX-013 | Calibration | tests/MtgMcp.Calibration/Fixtures/stats-lab-interaction-readiness.json | Affected scenarios and baseline changes | Stats Lab | Update with reviewed evidence |
| SLI-FIX-014 | MCP JSON | tests/MtgMcp.App.Tests/StatsLabInteractionSurfaceTests.cs | Old and new fields together | App | Freeze old fields through 0.9 |
| SLI-FIX-015 | Replay/bounds | tests/MtgMcp.Core.Tests/StatsLabInteractionMetricsTests.cs | Determinism, cancellation, bounded output | Core/App | Update only with global bound policy |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| SLI-REQ-001 | SLI-FIX-001 to SLI-FIX-005 | Exact four checkpoint booleans | Analyzer tests |
| SLI-REQ-002 | SLI-FIX-003, SLI-FIX-004, SLI-FIX-006 | Legal pre-spend castability after land | Turn/payment tests |
| SLI-REQ-003 | SLI-FIX-007 | Exact mean rates per turn | Aggregate test |
| SLI-REQ-004 | SLI-FIX-008 | Scenario equals turn-four castable rate | Scenario test |
| SLI-REQ-005 | SLI-FIX-009 | New access and old readiness coexist | Scorecard test |
| SLI-REQ-006 | SLI-FIX-001 to SLI-FIX-005 | Failures partition exactly once | Failure tests |
| SLI-REQ-007 | SLI-FIX-010 to SLI-FIX-013 | Deltas, reasons, traces, calibration updated | Integration/calibration tests |
| SLI-REQ-008 | SLI-FIX-014 | Existing keys/types/semantics unchanged | Surface/E2E snapshot |
| SLI-REQ-009 | SLI-FIX-015 | Replays identical; limits/cancellation honored | Replay/bounds tests |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| Stats Lab analysis output | Read-only | Visible | Adds two series, scenario, dimension |
| Comparison output | Read-only | Visible | Adds candidate-minus-baseline deltas |
| Recommendation output | Existing | Unchanged | Reasons use new access evidence |
| Detailed traces | Read-only | Detail-gated | Adds bounded checkpoint/failure counters |

## Provider Fixtures

None.

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| Frozen Stats Lab suite | Existing calibration decks, existing seed/job settings | New rates recorded; unrelated scenarios stable within documented tolerance | Calibration report |
