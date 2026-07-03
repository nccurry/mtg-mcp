# Conservative Goldfish V2 Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core simulation, recommendations, MCP surface, and release maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Current goldfish paths estimate pressure from tags and prose that are not executed game actions. This PLC builds one deterministic conservative effect-model for unopposed games, supports the Jasmine fixture through explicit abilities, exposes partial coverage, and atomically replaces every public and downstream goldfish consumer. It is not a full Magic rules engine.

## Audience

Maintainers of simulation, recommendations, MCP tools, prompts/resources, release compatibility, and deterministic benchmarks.

## References

- src/MtgMcp.Core/Simulation/DeckSimulationService.Goldfish.cs
- src/MtgMcp.Core/Simulation/Race
- src/MtgMcp.Core/PerformanceMana.cs
- src/MtgMcp.App/Tools/Simulation/SimulationTools.cs
- src/MtgMcp.Core/Recommendations/DeckBatchTuningService.cs
- src/MtgMcp.Core/Recommendations/DeckBrainstormingService.cs
- docs/versioning.md
- [Card snapshot prerequisite](../card-snapshot-integrity/README.md)
- [Land entry prerequisite](../land-entry-classification/README.md)
- [Profile evidence prerequisite](../simulation-profile-evidence/README.md)
- [Trust evidence vocabulary](../mcp-trust-evidence/SRD.md#requirements)
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)
- Public read-only deck: https://archidekt.com/decks/22958528/jasmine_boreal_of_the_seven_vanilla

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Conservative estimates | Unsupported abilities add zero modeled damage and are reported | No pressure score fallback |
| One answer everywhere | Direct, projection, win-turn, comparison, batch, and brainstorm consumers share the same analysis | Cross-wrapper equality tests |
| Legal mana | Color, yield, restriction, exhaustion, hybrid, Phyrexian, colorless, and X cases obey policy | Jasmine mana restriction is enforced |
| Reproducible operation | Same deck/settings/seed yields identical fingerprint, metrics, diagnostics, and traces | Comparison order does not alter results |
| Bounded usable output | Coverage, warnings, diagnostics, and traces have deterministic caps and omitted counts | Detail gating follows trust vocabulary |

## System Overview

A Core compiler turns trusted card metadata into immutable card templates containing printed facts and zero or more CompiledAbility values. The kernel runs unopposed games under explicit phase, mana, combat, multiplayer, and mulligan policies. One frozen analysis feeds specialized wrappers and comparison/batch presenters.

## Scope And Non-Scope

- In scope: compiler, trigger/effect unions, payment, Jasmine abilities, vanilla combat, command zone, mulligans, multiplayer target assignment, diagnostics, deterministic outputs, six-consumer cutover, old-code removal, docs, fixture, benchmark, and live read-only smoke.
- Out of scope: blockers, stack, priority, opponent boards or decisions, commander damage, generic equipment/attachment choices, matchup win rates, and Stats Lab changes.
- Compatibility target: atomic replacement in 0.9 under the correctness exception below; retained request parameters keep names/defaults unless the contract table explicitly removes or adds them.
- Explicit non-goals: claiming rules-backed completeness, estimating unsupported effects, or keeping two selectable goldfish models.

## Compatibility Exception

The existing optimistic-goldfish-model and rules-backed-goldfish-race-v1 shipped in 0.8.0, but their pressure and route estimates can claim unexecuted lethal behavior. Under docs/versioning.md, this packet records a broken-correctness-contract exception: the six goldfish surfaces replace their result schemas atomically without a compatibility shim. README, CHANGELOG, MCP inventory, and a 0.9 migration note shall list removed fields and replacement fields. Unrelated count, role, Stats Lab, and trust-evidence contracts are outside this exception.

## Stakeholders And Affected Systems

Core simulation/compiler/payment and recommendation models; App tools, presenters, prompts, resources, and tool registry; Archidekt read-only import adapter; docs and release notes; unit, fixture, surface, E2E, live, and benchmark tests; MCP clients consuming the six named surfaces.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| CGF-REQ-001 | Must | Architecture | One internal compiler and kernel shall produce the single-deck analysis used by all direct, wrapper, comparison, batch, and brainstorming goldfish consumers. | Multiple semantics caused drift. | CGF-FIX-001 and CGF-FIX-020 prove cross-wrapper equality. |
| CGF-REQ-002 | Must | Model | A compiled card shall contain zero or more CompiledAbility values, each with one closed Trigger and one closed Effect that supports sequence and modal composition. | Timing and multiple abilities must be explicit. | CGF-FIX-002 to CGF-FIX-006 compile exhaustively. |
| CGF-REQ-003 | Must | Mana | Payment shall model source alternatives, yield, restrictions, exhaustion, generic, colored, two-color hybrid, monohybrid, Phyrexian, explicit colorless, and X according to the policy table. | Unrestricted one-mana assumptions are incorrect. | CGF-FIX-007 to CGF-FIX-011 pass exact legal/illegal casts. |
| CGF-REQ-004 | Must | Mana | Jasmine Boreal mana shall be spendable only on creature spells whose compiled ability list is empty. | Commander text imposes a restriction central to this deck. | CGF-FIX-012 rejects noncreature and ability-bearing creatures. |
| CGF-REQ-005 | Must | Simulation | The kernel shall execute the documented phase order, land timing and entry class, source exhaustion, mulligans, command-zone casting/tax, vanilla combat, and format draw/life defaults. | Results require stable game semantics. | CGF-FIX-013 to CGF-FIX-016 pass event traces. |
| CGF-REQ-006 | Must | Effects | The kernel shall conservatively support the frozen Jasmine ability matrix: cast/enter/attack/combat-damage/static timing, fixed and greatest-controlled-power tokens, populate, modal/composite effects, each-opponent life loss, one-sided creature sweeps, and the narrow automatic Fractal attachment. | These are the approved deck behaviors. | CGF-FIX-002 to CGF-FIX-006 and CGF-FIX-017 pass. |
| CGF-REQ-007 | Must | Multiplayer | Opponents, life totals, target assignment, attached-token attack ownership, each-opponent effects, and deterministic modal/target ties shall follow the multiplayer policy table. | Ambiguity changes lethal timing. | CGF-FIX-017 to CGF-FIX-019 pass. |
| CGF-REQ-008 | Must | Diagnostics | Coverage shall distinguish compiled abilities, ignored abilities, missing metadata, known-but-unsupported values, and deliberately unsupported mechanics at card and ability level; unsupported effects add zero estimated behavior. | Partial card support must remain visible. | CGF-FIX-021 and CGF-FIX-022 pass exact rows and zero-contribution regressions. |
| CGF-REQ-009 | Must | Determinism | Deck/settings/replay fingerprints, card retention, modal choices, targets, comparisons, warnings, traces, and rows shall use documented stable ordering and paired seeds. | Replays and comparisons must be invariant. | CGF-FIX-019, CGF-FIX-020, and CGF-FIX-023 pass permutations. |
| CGF-REQ-010 | Must | Bounds | Results shall retain at most 250 coverage rows, 20 ability diagnostics, 50 warnings, and 3 traces of 120 events each, with a nonnegative omitted count for every bounded collection. | Outputs must remain usable. | CGF-FIX-024 hits every cap. |
| CGF-REQ-011 | Must | Metrics | Turn metrics shall be arithmetic means/rates across completed runs; lethal-turn percentiles shall be conditional on lethal runs and null when no run is lethal. | Numeric interpretation must be exact. | CGF-FIX-025 and CGF-FIX-026 pass known distributions. |
| CGF-REQ-012 | Must | API | The five goldfish tools and goldfish subtree of deck_batch_tuning_report shall atomically adopt the request/response contracts in SADD and remove model selection, old pressure fields, and speculative routes. | A partial cutover exposes conflicting models. | CGF-FIX-027 to CGF-FIX-033 surface/E2E snapshots pass. |
| CGF-REQ-013 | Must | Downstream | Batch tuning, brainstorming, presenters, prompts, resources, README, CHANGELOG, versioning notes, and all GoldfishSimulationResult consumers shall migrate before old models and engines are deleted. | Hidden consumers otherwise retain unsafe output. | CGF-FIX-034 inventory reaches zero obsolete references. |
| CGF-REQ-014 | Must | Reliability | Compilation and simulation shall honor CancellationToken, preserve bounded redacted failures, and keep normal tests offline and non-mutating. | Long simulations and provider errors need safe exits. | CGF-FIX-035 cancellation/error tests pass. |
| CGF-REQ-015 | Must | Performance | The frozen Jasmine benchmark shall complete the defined 1,000-run job in at most five seconds on the recorded reference machine in Release, report-only in CI. | The replacement must be practical. | CGF-FIX-036 records baseline and v2 median. |
| CGF-REQ-016 | Must | Validation | A named read-only live Jasmine smoke shall import deck 22958528 without mutation and compare its fingerprint/count to the frozen fixture while allowing documented remote drift. | Live integration is useful but non-deterministic. | CGF-FIX-037 passes manually after offline gates. |
| CGF-REQ-017 | Must | Evidence | Compiled ability diagnostics shall use the canonical evidence tiers owned by mcp-trust-evidence REQ-005 and detail gating delegated from trust REQ-008. | Evidence vocabulary must not fork. | CGF-FIX-022 and reciprocal trust links pass. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Requirements are independently traceable.
- [x] Metrics, caps, and performance are measurable.
- [x] True implementation constraints are explicit.
- [x] No unresolved planning placeholders remain.

## Interfaces, Data, States, And Modes

### Request contracts

| Surface | Retained parameters and defaults | Added | Removed |
| --- | --- | --- | --- |
| deck_simulate_goldfish | workspaceId string; simulationProfile string=auto; targetTurn integer=7; simulations integer=1000; seed integer=1337; mulligan boolean=true | detailLevel string=normal; opponents nullable integer | None |
| deck_project_board_state | workspaceId string; simulationProfile string=auto; turn integer=5; simulations integer=1000; seed integer=1337 | mulligan boolean=true; detailLevel string=normal; opponents nullable integer | None |
| deck_estimate_win_turn | workspaceId string; simulationProfile string=auto; maxTurn integer=12; simulations integer=1000; seed integer=1337 | mulligan boolean=true; detailLevel string=normal; opponents nullable integer | None |
| deck_compare_goldfish | workspaceIds string array; archidektDeckIdsOrUrls nullable string array; detailLevel string=summary; simulationProfile string=auto; targetTurn integer=7; simulations integer=1000; seed integer=1337; mulligan boolean=true | opponents nullable integer | model string |
| archidekt_compare_goldfish | workspaceId string; deckIdOrUrl1 string; deckIdOrUrl2 and deckIdOrUrl3 nullable string; detailLevel string=summary; simulationProfile string=auto; targetTurn integer=7; simulations integer=1000; seed integer=1337; mulligan boolean=true | opponents nullable integer | None |
| deck_batch_tuning_report | workspaceIds string array; maxBudget nullable decimal; detailLevel string=summary; simulationProfile string=auto; targetTurn integer=7; simulations integer=1000; seed integer=1337 | mulligan boolean=true; opponents nullable integer | None |

opponents is integer 1 through 3. When omitted it is derived once from the baseline format: Commander defaults to 3, other formats to 1; every compared deck uses that value. Commander games start opponents at 40 and draw on turn one. Non-Commander games start the opponent at 20 and assume the simulated deck is on the play, skipping its turn-one draw.

### Result policy

deck_simulate_goldfish returns ConservativeGoldfishResult. Projection and win-turn return specialized views extracted from that analysis, not the complete result. Comparisons return ordered deck analyses and deltas. Batch retains its non-goldfish report fields and replaces only each goldfish subtree. Exact nested scalar/null schemas are in SADD.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Conservatism | Unsupported ability present | Zero behavior contribution plus diagnostic |
| Determinism | Same input/seed under wrappers and order permutations | Identical analysis fingerprint and metrics |
| Performance | CGF-FIX-036 Release job | Median at most 5.0 seconds on reference machine |
| Cancellation | Cancel during compile or run | OperationCanceledException propagates promptly |
| Compatibility clarity | 0.9 client migration | Removed/replacement inventory in surface docs and changelog |
| Offline safety | Normal validation | No network or Archidekt mutation |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Freeze fixture and v1 benchmark | CGF-REQ-015, CGF-REQ-016 | Fingerprinted fixture and reproducible 18.4s baseline recorded |
| 2 | Private compiler/kernel | CGF-REQ-001 to CGF-REQ-005, CGF-REQ-008 to CGF-REQ-011, CGF-REQ-014, CGF-REQ-017 | Internal rules, diagnostics, determinism, cancellation pass |
| 3 | Jasmine effects/wrappers | CGF-REQ-006, CGF-REQ-007 and wrapper part of CGF-REQ-001 | Frozen Jasmine and cross-wrapper/comparison fixtures pass |
| 4 | Atomic public cutover | CGF-REQ-012, CGF-REQ-013 | Six surfaces migrated; obsolete inventory zero |
| 5 | Performance and completion | CGF-REQ-015, CGF-REQ-016 and all | Five-second criterion, docs, live smoke, and broad gates pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| CGF-REQ-001 | Compiler and shared analysis | Wrapper equivalence | CGF-FIX-001, CGF-FIX-020 |
| CGF-REQ-002 | Compiled ability model | Compiler fixtures | CGF-FIX-002 to CGF-FIX-006 |
| CGF-REQ-003 | Mana source/payment model | Payment matrix | CGF-FIX-007 to CGF-FIX-011 |
| CGF-REQ-004 | Restricted mana | Jasmine mana tests | CGF-FIX-012 |
| CGF-REQ-005 | Kernel turn order | Event trace tests | CGF-FIX-013 to CGF-FIX-016 |
| CGF-REQ-006 | Effect semantics | Jasmine effect matrix | CGF-FIX-002 to CGF-FIX-006, CGF-FIX-017 |
| CGF-REQ-007 | Multiplayer policy | Target/trigger tests | CGF-FIX-017 to CGF-FIX-019 |
| CGF-REQ-008 | Evidence and coverage | Partial/negative tests | CGF-FIX-021, CGF-FIX-022 |
| CGF-REQ-009 | Determinism | Replay/permutation tests | CGF-FIX-019, CGF-FIX-020, CGF-FIX-023 |
| CGF-REQ-010 | Output bounds | Cap tests | CGF-FIX-024 |
| CGF-REQ-011 | Metric semantics | Known distribution tests | CGF-FIX-025, CGF-FIX-026 |
| CGF-REQ-012 | Public API contracts | Surface/E2E snapshots | CGF-FIX-027 to CGF-FIX-033 |
| CGF-REQ-013 | Downstream migration | Reference inventory/docs | CGF-FIX-034 |
| CGF-REQ-014 | Reliability | Cancellation/redaction tests | CGF-FIX-035 |
| CGF-REQ-015 | Performance | Reproducible benchmark | CGF-FIX-036 |
| CGF-REQ-016 | Live validation | Named read-only smoke | CGF-FIX-037 |
| CGF-REQ-017 | Evidence vocabulary | Serialization/links | CGF-FIX-022 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Supported effect list is intentionally narrow | Assumption | Some decks produce little modeled damage | Simulation | Diagnostics expose exact omissions; add effects through fixtures |
| Breaking cutover surprises clients | Risk | 0.8 clients require migration | App/release | Atomic release note and surface inventory |
| Evidence enum unavailable at phase 2 | Risk | Compiler cannot type evidence | PLC owners | Complete trust REQ-005 first, or introduce Core enum and update both packets before compiler merge |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused compiler/payment/kernel tests, wrapper/comparison/batch/brainstorm tests, task surface:report, task lint, task test, named benchmark, named read-only live smoke after offline gates, documentation/link inspection, and git diff --check.

## Definition Of Done

- [ ] Every Must requirement is implemented or explicitly owner-deferred.
- [ ] Fixture, benchmark, surface, and live-smoke evidence is recorded.
- [ ] Traceability is current.
- [ ] SADD matches the implementation.
- [ ] Old goldfish engines/models and speculative output are removed.
