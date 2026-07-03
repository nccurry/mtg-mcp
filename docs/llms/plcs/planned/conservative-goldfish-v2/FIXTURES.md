# Conservative Goldfish V2 Fixtures And Acceptance Matrix

CGF-FIX-022 consumes the canonical serialization contract owned by
[mcp-trust-evidence FIX-004](../mcp-trust-evidence/FIXTURES.md#fixture-inventory).

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| CGF-FIX-001 | Frozen workspace/deck | tests/MtgMcp.Core.Tests/Fixtures/GoldfishV2/Jasmine22958528/ | Deterministic Jasmine source with manifest/fingerprint | Simulation | Update only by reviewed fixture refresh |
| CGF-FIX-002 | Card compiler case | tests/MtgMcp.Core.Tests/Fixtures/GoldfishV2/Jasmine22958528/abilities.json | Trigger/effect compilation | Simulation | Exact metadata from frozen fixture |
| CGF-FIX-003 | Microdeck | tests/MtgMcp.Core.Tests/ConservativeGoldfishCompilerTests.cs | Modal and Sequence effects | Simulation | One deterministic choice/tie case |
| CGF-FIX-004 | Microdeck | tests/MtgMcp.Core.Tests/ConservativeGoldfishEffectTests.cs | Greatest-controlled-power tokens and populate | Simulation | Preserve token tie rules |
| CGF-FIX-005 | Microdeck | tests/MtgMcp.Core.Tests/ConservativeGoldfishEffectTests.cs | Attack/combat-damage/each-opponent effects | Simulation | Include three opponents |
| CGF-FIX-006 | Microdeck | tests/MtgMcp.Core.Tests/ConservativeGoldfishEffectTests.cs | One-sided sweep and Fractal attachment | Simulation | Exact narrow supported behavior |
| CGF-FIX-007 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Colored/generic payments | Core | Exact legal/illegal plans |
| CGF-FIX-008 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Hybrid/monohybrid/Phyrexian | Core | Include life boundary |
| CGF-FIX-009 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Explicit colorless and X | Core | Include deterministic X tie |
| CGF-FIX-010 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Alternatives, Sol Ring yield, exhaustion | Core | Assert source activations |
| CGF-FIX-011 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Spend restrictions | Core | Include legal and illegal spell kinds |
| CGF-FIX-012 | Mana theory | tests/MtgMcp.Core.Tests/ConservativeGoldfishManaTests.cs | Jasmine restricted mana | Simulation | Empty ability list is required |
| CGF-FIX-013 | Event trace | tests/MtgMcp.Core.Tests/ConservativeGoldfishKernelTests.cs | Turn order and land timing | Simulation | Exact phase/event sequence |
| CGF-FIX-014 | Event trace | tests/MtgMcp.Core.Tests/ConservativeGoldfishKernelTests.cs | London mulligan and bottoming | Simulation | Fixed seed/profile |
| CGF-FIX-015 | Event trace | tests/MtgMcp.Core.Tests/ConservativeGoldfishKernelTests.cs | Command zone, tax, recast | Simulation | Exact mana and cast turns |
| CGF-FIX-016 | Format matrix | tests/MtgMcp.Core.Tests/ConservativeGoldfishKernelTests.cs | Commander/non-Commander draw/life defaults | Simulation | One fixture per format |
| CGF-FIX-017 | Multiplayer trace | tests/MtgMcp.Core.Tests/ConservativeGoldfishMultiplayerTests.cs | Adeline-style tokens and persistence | Simulation | Three stable opponents |
| CGF-FIX-018 | Multiplayer trace | tests/MtgMcp.Core.Tests/ConservativeGoldfishMultiplayerTests.cs | Each-opponent loss and all-opponents lethal | Simulation | Exact life totals |
| CGF-FIX-019 | Choice permutation | tests/MtgMcp.Core.Tests/ConservativeGoldfishMultiplayerTests.cs | Target/modal tie-breaking | Simulation | Shuffle equivalent candidates |
| CGF-FIX-020 | Wrapper/comparison | tests/MtgMcp.Core.Tests/ConservativeGoldfishWrapperTests.cs | Direct/projection/win/comparison equality and paired seeds | Core | Identical settings required |
| CGF-FIX-021 | Partial card | tests/MtgMcp.Core.Tests/ConservativeGoldfishCompilerTests.cs | Numeric stats plus unsupported ability | Simulation | Vanilla damage only |
| CGF-FIX-022 | Coverage/evidence | tests/MtgMcp.Core.Tests/ConservativeGoldfishDiagnosticsTests.cs | Status/evidence tiers and zero unsupported contribution | Core | Sync with trust REQ-005 |
| CGF-FIX-023 | Replay permutation | tests/MtgMcp.Core.Tests/ConservativeGoldfishDeterminismTests.cs | Stable fingerprints/order | Core | Shuffle cards/decks |
| CGF-FIX-024 | Bound stress | tests/MtgMcp.Core.Tests/ConservativeGoldfishDiagnosticsTests.cs | 250/20/50/3x120 caps and omitted counts | Core | Exact boundary plus one |
| CGF-FIX-025 | Known distribution | tests/MtgMcp.Core.Tests/ConservativeGoldfishAggregationTests.cs | Means/rates/percentiles | Core | Hand-computed run outcomes |
| CGF-FIX-026 | No-lethal distribution | tests/MtgMcp.Core.Tests/ConservativeGoldfishAggregationTests.cs | Null conditional lethal values | Core | Zero lethal runs |
| CGF-FIX-027 | MCP schema | tests/MtgMcp.App.Tests/ConservativeGoldfishSurfaceTests.cs | deck_simulate_goldfish | App | Exact contract table |
| CGF-FIX-028 | MCP schema | tests/MtgMcp.App.Tests/ConservativeGoldfishSurfaceTests.cs | deck_project_board_state | App | Specialized view |
| CGF-FIX-029 | MCP schema | tests/MtgMcp.App.Tests/ConservativeGoldfishSurfaceTests.cs | deck_estimate_win_turn | App | Specialized view |
| CGF-FIX-030 | MCP schema | tests/MtgMcp.E2E.Tests/ConservativeGoldfishE2ETests.cs | deck_compare_goldfish | App | Mixed sources/settings |
| CGF-FIX-031 | MCP schema | tests/MtgMcp.E2E.Tests/ConservativeGoldfishE2ETests.cs | archidekt_compare_goldfish | App | Fake HTTP only |
| CGF-FIX-032 | MCP schema | tests/MtgMcp.E2E.Tests/ConservativeGoldfishE2ETests.cs | deck_batch_tuning_report subtree | App | Non-goldfish fields unchanged |
| CGF-FIX-033 | Detail matrix | tests/MtgMcp.App.Tests/ConservativeGoldfishPresenterTests.cs | Summary/normal/full null and bounds | App | Sync with SADD table |
| CGF-FIX-034 | Consumer inventory | docs/llms/plcs/planned/conservative-goldfish-v2/IMPLEMENTATION_PLAN.md | Batch, brainstorm, models, prompts, old-code removal | Maintainers | Zero obsolete references |
| CGF-FIX-035 | Reliability | tests/MtgMcp.Core.Tests/ConservativeGoldfishReliabilityTests.cs | Compile/run cancellation and redacted failures | Core/App | Deterministic cancel points |
| CGF-FIX-036 | Benchmark | tests/MtgMcp.Benchmarks/ConservativeGoldfishBenchmark.cs | Frozen Jasmine 1,000-run Release job | Simulation | Record commit/machine/runtime/settings/median |
| CGF-FIX-037 | Live read-only smoke | tests/MtgMcp.E2E.Tests/ConservativeGoldfishLiveTests.cs | Deck 22958528 import and drift report | Archidekt | Manual only; never mutate |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| CGF-REQ-001 | CGF-FIX-001, CGF-FIX-020 | One analysis/fingerprint across consumers | Integration equality |
| CGF-REQ-002 | CGF-FIX-002 to CGF-FIX-006 | Exact trigger/effect lists and exhaustive switches | Compiler tests |
| CGF-REQ-003 | CGF-FIX-007 to CGF-FIX-011 | Exact legal payment plans | Payment theory |
| CGF-REQ-004 | CGF-FIX-012 | Jasmine mana rejected/accepted by predicate | Payment tests |
| CGF-REQ-005 | CGF-FIX-013 to CGF-FIX-016 | Exact event order and format defaults | Kernel trace tests |
| CGF-REQ-006 | CGF-FIX-002 to CGF-FIX-006, CGF-FIX-017 | Approved effect semantics only | Compiler/kernel tests |
| CGF-REQ-007 | CGF-FIX-017 to CGF-FIX-019 | Stable assignments, life, lethal, ties | Multiplayer tests |
| CGF-REQ-008 | CGF-FIX-021, CGF-FIX-022 | Partial stats work; unsupported effect is zero | Negative regression |
| CGF-REQ-009 | CGF-FIX-019, CGF-FIX-020, CGF-FIX-023 | Results invariant to ordering | Replay/permutation tests |
| CGF-REQ-010 | CGF-FIX-024 | Exact caps and omitted counts | Bound stress test |
| CGF-REQ-011 | CGF-FIX-025, CGF-FIX-026 | Correct means/rates; null no-lethal percentiles | Aggregate tests |
| CGF-REQ-012 | CGF-FIX-027 to CGF-FIX-033 | Exact six-consumer contracts and detail gating | Surface/E2E snapshots |
| CGF-REQ-013 | CGF-FIX-034 | All downstream uses migrated; old inventory zero | Search/docs inspection |
| CGF-REQ-014 | CGF-FIX-035 | Cancellation propagates; failures bounded/redacted | Reliability tests |
| CGF-REQ-015 | CGF-FIX-036 | Release median at most 5.0 seconds on reference host | Benchmark report |
| CGF-REQ-016 | CGF-FIX-037 | Read-only import works; remote drift is reported | Named manual smoke |
| CGF-REQ-017 | CGF-FIX-022, CGF-FIX-033 | Canonical tiers and delegated detail gates | Serialization/surface tests |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| deck_simulate_goldfish | Read-only | Visible | ConservativeGoldfishResult; model selector absent |
| deck_project_board_state | Read-only | Visible | Specialized v2 projection |
| deck_estimate_win_turn | Read-only | Visible | Specialized v2 lethal view |
| deck_compare_goldfish | Read-only/OpenWorld | Visible | Local and optional read-only Archidekt sources |
| archidekt_compare_goldfish | Read-only/OpenWorld | Visible | Up to three read-only references |
| deck_batch_tuning_report | Read-only/OpenWorld | Visible | Only goldfish subtree replaced |

## Provider Fixtures

| Provider | Fixture | Scenario | Sanitization notes |
| --- | --- | --- | --- |
| Archidekt | CGF-FIX-001 | Frozen Jasmine import | No credentials/user data; deterministic local copy |
| Archidekt | CGF-FIX-031 | Comparison fake HTTP | Synthetic IDs and redacted failures |
| Archidekt live | CGF-FIX-037 | Read-only public import | Never invoke update/delete/authenticated mutation |

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| v1 baseline | Frozen Jasmine; Release; 1000 runs; turn 7; seed 1337; mulligan; Commander defaults | 18.4 seconds recorded with commit/host/runtime | Phase 1 report |
| v2 completion | Same fixture/settings/host | Median at most 5.0 seconds; identical replay across repeats | task bench:goldfish-v2 |
| Jasmine semantics | CGF-FIX-001 fixed seed | Frozen expected fingerprint and reviewed metric ranges | Core fixture test |
