# Conservative Goldfish V2 Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core simulation, recommendations, MCP surface, and release maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Core compiles trusted snapshots into immutable templates, then executes one conservative event kernel. Closed trigger/effect and mana unions make supported behavior exhaustive. Accumulators produce one analysis object; every public wrapper derives from it. Unsupported abilities are diagnostics, never pressure estimates.

## Goals, Non-Goals, And Design Drivers

Correctness conservatism, deterministic replay, legal payment, bounded output, cancellation, shared analysis, five-second Jasmine performance, and clean Core/App boundaries drive the design. Full rules, opponents with boards, and Stats Lab are excluded.

## Context And Scope

Workspace snapshots and profiles feed the compiler. The kernel models an unopposed game against one to three life totals. App tools present detail-gated views. Read-only Archidekt comparisons import through the existing adapter and never mutate a remote deck.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Patch optimistic model | Add more pressure heuristics | Small initial change | Unsupported text still overclaims behavior | Rejected |
| Keep v1 and v2 public | Model selector and compatibility shim | Easier migration | Conflicting answers and permanent complexity | Rejected |
| Full rules engine | Model priority, stack, targeting, opponents | Broad fidelity | Disproportionate scope | Rejected |
| Conservative closed model | Exact supported effects, zero unsupported contribution | Auditable and extensible | Narrow initial card coverage | Chosen |

## Chosen Design

### Compiler and shared analysis

GoldfishCompiler consumes included primary-category snapshots with coverage from card-snapshot-integrity, land entry facts from LandEntryClassifier, and the resolved profile from simulation-profile-evidence. It emits CompiledDeck plus ordered coverage diagnostics. ConservativeGoldfishAnalyzer compiles once per deck/settings fingerprint, then runs all simulations and exposes one immutable ConservativeGoldfishResult. Wrappers project from that result without rerunning or changing settings.

### Compiled ability model

CompiledCard contains printed mana/type/combat facts, mana abilities, and an immutable list of CompiledAbility(Trigger, Effect, EvidenceTier, SourceTextSpan). Closed triggers:

| Trigger | Timing |
| --- | --- |
| OnCast | After legal payment, before permanent entry |
| OnResolve | Resolution of the spell in the no-stack model |
| OnEnter | After a permanent/token enters |
| OnAttack | When its controller declares it attacking |
| OnCombatDamageToPlayer | After its unblocked damage is assigned |
| OnAttachedTokenAttack | When the explicitly created attached Fractal attacks |
| Static | Continuous supported restriction/modifier |

Closed effects:

| Effect | Semantics |
| --- | --- |
| Sequence | Resolve child effects in printed order |
| ChooseOne | Evaluate legal modes, maximize deterministic lexicographic outcome, break ties by printed mode index |
| CreateFixedToken | Create a token with fixed count/stats/abilities |
| CreateGreatestPowerToken | Create token stats from greatest current controlled creature power |
| Populate | Copy the deterministically selected controlled token; greatest power, then toughness, then oldest permanent ID |
| LoseLife | Reduce designated or each opponent life as specified |
| DrawCards | Draw bounded card count |
| DestroyCreaturesWithAbilities | Remove controlled/opposing creatures matching the exact supported one-sided sweep predicate |
| CreateAttachedFractal | Create the exact supported Fractal and attach automatically to its printed host |
| AddRestrictedMana | Add yield/restriction from a supported mana ability |
| ModifyPowerToughness | Apply supported duration and scope |

Nested Sequence and ChooseOne allow modal/composite cards. Unknown targeting, replacement, attachment, or choice semantics reject that ability only; printed stats remain usable.

### Mana source and payment model

A mana source exposes production alternatives. Each alternative has a yield vector, spend restriction, activation timing, and exhaustion behavior. One activation selects one alternative and exhausts the source unless explicitly repeatable.

| Cost/source feature | Policy |
| --- | --- |
| Generic | Any unspent mana pays it after mandatory symbols |
| Colored | Exact color required |
| Explicit colorless | Colorless mana required |
| Two-color hybrid | Either printed color pays one symbol |
| Monohybrid | Printed color or two generic |
| Phyrexian | Printed color preferred; otherwise pay two life only when life remains above zero after payment |
| X | Deterministic legal X chosen by effect utility, then largest X, within available mana |
| Production alternatives | Choose one whole alternative; colors in an alternative are not simultaneous unless yield says so |
| Multi-mana yield | Sol Ring-style source adds its full yield |
| Restrictions | Mana can pay only a matching spell/ability predicate |
| Exhaustion | Activated source cannot pay again that turn |
| Tapped/conditional land | Follows shared LandEntryClassifier; conditional entry uses documented conservative consumer policy |

Payment searches mandatory symbols and restrictions before generic/X, uses the fewest restricted resources, then stable source ID for ties. Jasmine mana has predicate spell is creature and compiled ability list is empty. Dynamic or unsupported costs fail compilation for casting purposes and emit diagnostics.

### Kernel turn order

Each game follows:

1. Build library, commander zone, life totals, and paired seeded RNG.
2. Apply London mulligan with the resolved profile; bottom cards in stable policy order.
3. Untap, upkeep, then format-specific draw.
4. Main phase: play at most one legal land; activate sources and cast legal spells/commander under deterministic policy.
5. Begin combat; declare all eligible beneficial attackers against the opponent with highest life, then lowest stable opponent index.
6. Resolve attack triggers, including tokens assigned to other opponents. Those tokens enter under the simulated player and remain after combat unless their effect says otherwise.
7. Assign unblocked combat damage; resolve combat-damage triggers.
8. Second main: repeat legal proactive policy.
9. End step/cleanup and record turn metrics.
10. Stop when all tracked opponents are at zero or less, max turn is complete, or cancellation is requested.

No blocker, response, stack, or target-choice branch is inferred.

### Multiplayer policy

Commander defaults to three opponents at 40 life and draws on turn one; non-Commander defaults to one opponent at 20 life and skips turn-one draw on the play. opponents overrides only count, not format life/draw rules. Normal attackers use highest life then stable index. Adeline-style generated attackers are assigned one to each other opponent in stable index order and remain controlled after combat. Each-opponent life loss applies independently to every tracked life total. Lethal requires all opponents defeated. Modal/target ties use printed mode then stable permanent/opponent IDs.

### Evidence and coverage

EvidenceTier is the Core enum owned by mcp-trust-evidence REQ-005. Coverage status is one of fullyCompiled, partiallyCompiled, metadataMissing, knownUnsupported, or deliberatelyUnsupported. Each ability diagnostic includes cardId/name, abilityIndex, trigger nullable, status, evidenceTier, reasonCode, and bounded message. A card with numeric stats and unsupported text can attack as vanilla while its unsupported ability contributes zero.

### Determinism and retention

Rows sort by severity, normalized card name, stable card ID, face index, then ability index. Retain the first 250 coverage rows, 20 ability diagnostics, and 50 warnings after sorting. Traces retain losing/no-lethal, median-lethal, then fastest-lethal representative runs when available, tie-broken by run index; maximum three. Each trace retains first 120 events in turn/phase/event sequence and reports omittedEventCount. Every collection reports its own omitted count.

### Public result schemas

ConservativeGoldfishResult:

| Field | Type | Null | Meaning |
| --- | --- | --- | --- |
| modelVersion | string | never | conservative-goldfish-v2 |
| workspaceId | string | never | Source workspace |
| deckFingerprint | string | never | Compiled deck identity |
| settingsFingerprint | string | never | Normalized settings identity |
| replayFingerprint | string | never | Deck, settings, seed, compiler version |
| settings | GoldfishSettings | never | profile, turns, simulations, seed, mulligan, opponents, life/draw policy |
| profileResolution | object | never | Corrected profile evidence |
| coverage | CoverageSummary | never | Counts plus bounded coverageRows and omittedCoverageRowCount |
| outcomes | OutcomeSummary | never | lethalRate; lethalTurnMean/median/p10/p90 nullable; completedRuns |
| turnMetrics | array | never | turn, mean lands/mana/creatures/tokens/power/cards, attackDamageRate/mean, opponentsDefeatedMean |
| commandZone | object | never | castRate, firstCastTurnMean nullable, taxPaidMean |
| diagnostics | object | never | abilityDiagnostics, warnings, omittedAbilityDiagnosticCount, omittedWarningCount |
| traces | array | never | At most three bounded traces |
| notes | string array | never | Model caveat and settings notes |

CoverageRow scalars are cardId string nullable, cardName string non-null, faceIndex integer nullable, status string non-null, compiledAbilityCount integer, ignoredAbilityCount integer, missingGroups string array, unsupportedMechanics string array. TraceEvent scalars are sequence integer, turn integer, phase string, eventKind string, cardName string nullable, opponentIndex integer nullable, amount integer nullable, detail string nullable.

ProjectedBoardStateV2 returns modelVersion, workspaceId, deck/settings/replay fingerprints, settings, turn integer, boardState object, coverage summary, diagnostics, and notes. boardState contains means for lands, untappedMana, creatures, tokens, totalPower, handSize, commanderCastRate, attackDamage, and opponentsDefeated. It is a specialized view of the matching turnMetrics and never runs a separate model.

WinTurnEstimateV2 returns modelVersion, workspaceId, fingerprints, settings, outcomes, coverage summary, diagnostics, and notes. Percentile fields follow the conditional/null policy.

GoldfishComparisonV2 returns modelVersion, normalized shared settings, baselineLabel, ordered decks, failures, and notes. Each deck row contains label, source, input nullable, workspaceId, name, archidektDeckId nullable, cardCounts nullable, analysis, and deltaFromBaseline nullable. Delta scalars are candidate minus baseline for lethalRate, conditional median lethal turn nullable, target-turn mean attack damage, board power, and commander cast rate. Failures contain label/input/source/reasonCode/message with redaction. Input order determines deck order; comparison order never changes analysis.

Detail levels: summary omits rows, ability diagnostics, warnings, and traces but retains counts/omitted counts and caveat; normal includes bounded coverage/warnings without traces; full includes all bounded collections. Trust REQ-008 delegates this goldfish gating to this table.

### Atomic consumer migration

The cutover transaction migrates five tools, batch goldfish subtrees, BrainstormModels, WorkflowReportModels, services, presenters, prompts, resources, surface inventory, README, CHANGELOG, and versioning note. It then deletes GoldfishSimulationResult, optimistic engine files, race-v1 engine/model selection, pressure scores, common/speculative routes, and obsolete presenters. No commit exposes both public models.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| GoldfishCompiler | Snapshot to immutable deck | Per deck fingerprint | Core internal | Coverage, land/profile facts, evidence enum | Compiler fixtures |
| ManaPaymentSolver | Legal activation/payment plan | Cast decision | Core internal | Compiled sources/costs | Payment matrix |
| ConservativeGoldfishKernel | One unopposed game | Run | Event stream | Compiled deck/RNG | Event traces |
| GoldfishAccumulator | Aggregate runs and retain representatives | Analysis request | Core result | Kernel events | Known-distribution tests |
| Goldfish projections | Specialized wrapper views | Request | Core view models | Result only | Equivalence tests |
| App presenters | Detail-gated JSON | MCP request | Six public surfaces | Core results | Surface/E2E tests |

## Runtime And Data Flow

Compile once, create deterministic run seeds from the request seed and run index, execute runs with periodic cancellation checks, update primitive accumulators, retain only bounded representative candidates, freeze and sort diagnostics, compute fingerprints, then project the requested view. Comparisons compile/analyze each deck with the same normalized settings and paired run seeds.

## MCP Surface, Schemas, And Diagnostics

All tools remain read-only with current OpenWorld annotations. archidekt_compare_goldfish and deck_compare_goldfish remain open-world because of optional read-only imports. Request changes and exact nested result fields are specified above. Invalid opponents, simulations, turns, profiles, or detail levels return structured validation errors before simulation.

## Adapter And Provider Contracts

No new provider DTOs. Archidekt imports use the completed card-snapshot-integrity mapping/hydration path. Normal tests use the frozen fixture. task test:live:jasmine-goldfish is added as an explicit read-only filter and asserts no mutation method is called. Provider errors are redacted and isolated per reference deck.

## Cross-Cutting Concepts

Cancellation propagates from App through compile and run loops. All diagnostics are bounded and redacted. Fingerprints include compiler/model version. No caches cross incompatible fingerprints. The deck-count-contracts summary may populate comparison cardCounts if complete, but absence does not block this packet.

## Project Boundaries

Compiler, payment, kernel, results, and downstream domain services live in Core. App owns MCP contracts and presentation. Archidekt owns HTTP. Core never references App or adapters. Stats Lab shares only existing facts/payment primitives where they are truly reusable and remains a separate analyzer.

## Readability And Documentation

Use C# closed unions and exhaustive switches for triggers/effects/payment symbols/outcomes. Prefer immutable compiled records and straightforward phase methods. XML comments state timing and restrictions. Delete replaced abstractions in phase 4 rather than leaving compatibility code.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| CGF-REQ-001, CGF-REQ-002 | One compiler/result and closed ability model | CGF-FIX-001 to CGF-FIX-006, CGF-FIX-020 |
| CGF-REQ-003, CGF-REQ-004 | Explicit source/cost solver | CGF-FIX-007 to CGF-FIX-012 |
| CGF-REQ-005 to CGF-REQ-007 | Ordered kernel and multiplayer tables | CGF-FIX-013 to CGF-FIX-019 |
| CGF-REQ-008 to CGF-REQ-011 | Coverage, stable retention, caps, exact aggregates | CGF-FIX-021 to CGF-FIX-026 |
| CGF-REQ-012, CGF-REQ-013 | Atomic schema/downstream inventory | CGF-FIX-027 to CGF-FIX-034 |
| CGF-REQ-014 | Cancellation/redacted failures | CGF-FIX-035 |
| CGF-REQ-015, CGF-REQ-016 | Named benchmark/live targets | CGF-FIX-036, CGF-FIX-037 |
| CGF-REQ-017 | Shared evidence enum and detail gating | CGF-FIX-022 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Fixtures/benchmark task | CGF-REQ-015, CGF-REQ-016 | Frozen fingerprint and baseline metadata |
| 2 | Private Core compiler/mana/kernel/results | CGF-REQ-001 to CGF-REQ-005, CGF-REQ-008 to CGF-REQ-011, CGF-REQ-014, CGF-REQ-017 | Internal suite passes with no public cutover |
| 3 | Jasmine effects/wrappers/comparison | CGF-REQ-006, CGF-REQ-007 | Fixture and equivalence suite passes |
| 4 | App/downstream/docs removal | CGF-REQ-012, CGF-REQ-013 | Atomic surfaces pass and obsolete inventory zero |
| 5 | Benchmark/live/broad gates | All | Performance, smoke, lint, test, docs complete |

## Test Architecture

Compiler tests assert exact abilities and partial support. Payment theory covers every cost/source row. Kernel microdecks assert event traces. Frozen Jasmine tests assert deterministic ranges and fingerprints. Wrapper/comparison tests compare shared analysis objects and paired seeds. Surface snapshots cover all detail levels and null rules. Cancellation, bounds, no-lethal, and negative unsupported regressions are mandatory.

## Framework And External Notes

The public Jasmine deck may change. Its checked-in fixture and fingerprint are deterministic truth; the live read-only smoke reports remote drift rather than rewriting the fixture. The reference benchmark records commit df719e7ab4693adfb8d1cf06544154d9615f4e90, Intel Core Ultra 9 285HX, 24 logical processors, Windows 11 Pro 10.0.26200 x64, .NET 11.0.100-preview.5.26302.115, Release build, 1,000 simulations, target turn 7, seed 1337, mulligan enabled, Commander defaults. The observed v1 baseline is 18.4 seconds.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Correctness break without shim | Decision | Clients must migrate in 0.9 | Atomic cutover and migration note |
| Full rules behavior | Deferred | Results remain an unopposed model | Always return caveat |
| Unsupported deck coverage | Risk | Low/no lethal rate may be incomplete | Coverage and diagnostics stay prominent |
| Benchmark variability | Risk | Cross-machine time differs | Five seconds is reference-machine absolute; CI is report-only |

## Glossary

- Conservative effect-model: a simulator that executes only explicitly supported behavior and never estimates unsupported effects.
- Compiled ability: one trigger/effect/evidence tuple derived from trusted card metadata.
- Paired seeds: the same per-run random seed sequence used for every compared deck.
- Conditional lethal percentile: a percentile calculated only among runs that achieved lethal.
