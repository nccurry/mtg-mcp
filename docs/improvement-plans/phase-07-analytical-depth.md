# Phase 7 - Analytical Depth and Correctness

| | |
|---|---|
| Effort | L |
| Risk | Medium (analytical-output changes) |
| Depends on | Phase 4 (typing) helpful; Phase 0 (labels) prerequisite for the honesty story |
| Unblocks | trustworthy recommendations; Phase 8 features build on real evaluation |
| Target version | 0.15.0 |

Goal: deepen the honest-but-shallow heuristics and finish the determinism story - without
becoming a Magic rules engine (a stated non-goal).

## 1. Problems addressed

- **P10 (real fix) - `deck_evaluate_card` is no longer ramp-only.** Phase 7 broadened
  `CardOperationalFacts` to ramp, draw, and interaction facts, and the scorer now declares
  `evaluatedRoles`, selects `evaluatedRole`, and returns `unsupportedRole=true` for roles
  outside the current deterministic rubric.
- **P12 - bracket estimator is a coarse max-signal floor.**
  `EstimatedBracket = max(signal.SuggestedBracket)` (`Analysis/DeckAnalysisMetrics.cs:529-601`);
  one mass-land-denial card forces bracket 4 with little density sensitivity.
- **P13 (real fix) - goldfish determinism.** Phase 7 now routes goldfish-family shuffles
  and `DeckStatistics` Monte Carlo through `DeterministicSimulationRandom`; keep
  deterministic replay metadata, docs, and tests aligned as analytical models evolve.
- **P14 - offline combo fallback is now dataset-backed.** Phase 7 replaced the three
  hardcoded fallback pairs with a bounded embedded dataset sourced from
  `docs/reference/local-combos.json`.

## 2. Goals / non-goals

Goals:
- A general, honest card-evaluation framework covering ramp, draw, and interaction first,
  with future roles added only when they can be scored honestly.
- A density-aware, still-advisory, still-explainable Commander bracket estimate.
- One determinism model across all simulation tools.
- A meaningful local combo dataset (catalog-first, dataset fallback).
- Calibration/benchmark coverage so analytical changes are regression-safe.

Non-goals:
- No full rules engine, no opaque ML model, no claim of true matchup win rates. Keep every
  high-level result carrying assumptions/warnings/confidence (the existing Stats Lab
  pattern).

## 3. Current state (investigation)

- Operational facts now include `Ramp`, `Draw`, and `Interaction` slots. The evaluator
  still deliberately does not score tutors, payoffs, finishers, or other future roles; those
  return an explicit unsupported-role status instead of an unexplained zero.
- Bracket signals are real (live Game Changers via `is:game-changer`, fast mana, tutors,
  stax, combo, extra-turn, mass-land-denial) but combined by max, not density.
- Determinism: `DeterministicSimulationRandom` (SplitMix64) is used by Stats Lab, the
  rules-backed race, the goldfish/board/win-turn family, and `DeckStatistics` draw/land
  odds Monte Carlo. Goldfish and odds outputs now stamp the same RNG label where those
  result shapes carry RNG metadata.
- Calibration harness exists: `tests/MtgMcp.Calibration/` (runner, report writer, corpus
  loader, benchmark JSON e.g. `kinnan-benchmark.json`, `niv-mizzet-benchmark.json`,
  `expanded-public-benchmarks.json`) + `tests/MtgMcp.Calibration.Tests/StatsLabCalibrationTests.cs`
  and a `task calibrate:stats-lab` workflow. This is the vehicle for regression safety.
- Combos are catalog-first (Commander Spellbook) with clearly labeled local heuristics;
  the no-catalog fallback now reads a small checked-in local-pattern dataset.

## 4. Workstreams

### 4.1 General card evaluation framework
- Done in the third Phase 7 slice: extend `CardOperationalFacts` from a single `Ramp?`
  slot to supported ramp/draw/interaction fact slots, each with deterministic extraction
  and scoring.
- **Explicit first scope landed: `Ramp`, `Draw`, and `Interaction`.** `Removal` is handled
  inside interaction; `Tutor`, `Payoff`, broader `Protection`, etc. remain later,
  incremental slices.
- **Output declares coverage.** Every evaluation result lists supported roles
  (`evaluatedRoles: ["ramp","draw","interaction"]`), selects `evaluatedRole` when a
  supported fact is scored, and sets `unsupportedRole: true` with a clear warning for
  roles outside the current scope.
- Hard guardrail: extractors/scorers are **deterministic text/facet classifiers and
  bounded heuristics only** - no stack/sequencing/interaction simulation. This is a
  rubric, not a rules engine (see Risks).
- Coordinate with Phase 5 (`DeckCardEvaluationService` already exists) and Phase 1/2
  (`detailLevel` already accepts `summary`, `normal`, `full`, with compact alias).
- Rename timing resolved by implementation: the tool stayed `deck_evaluate_card`. Phase 0
  made the ramp-only implementation honest in its description; Phase 7 broadened the
  existing tool under the same name.

### 4.2 Bracket estimator depth
- **Gate: agree benchmark expectations before implementation.** First, add bracket
  benchmark cases to the calibration corpus with maintainer-agreed expected bracket ranges
  for known decks (precon/casual/tuned/high-power/cedh examples). Only after those
  expectations are signed off should the model change land - the benchmarks define
  "correct," so they come first, not after.
- Then move from `max(SuggestedBracket)` to a density/threshold model: weight signal counts
  and combinations (number of tutors, fast-mana density, Game Changer count, presence of
  multiple bracket-4 signals) rather than a single floor. Keep it advisory; keep the
  rationale and confidence; keep Game Changer data live from Scryfall.
- Document the model and its benchmark expectations in `docs/`.

### 4.3 Determinism unification
- Done in the first Phase 7 slice: the goldfish family and `DeckStatistics` Monte Carlo
  use `DeterministicSimulationRandom`, and odds outputs now expose `RngKind`.
- Keep one documented determinism model: same seed -> stable output across .NET versions
  for every simulation tool. Keep `docs/stats-lab-metrics.md` /
  `docs/simulation-profiles.md` current when future analytical models change.

### 4.4 Local combo dataset
- Done in the second Phase 7 slice: replace the three hardcoded combos with a small
  checked-in dataset under `docs/reference/` (the architecture doc already sanctions local
  fixtures there), still catalog-first and still labeled `local-pattern`/heuristic. Keep
  it bounded and attribution-aware.

### 4.5 Calibration + regression safety
- Expand the calibration corpus (more archetypes/colors/power levels) and wire calibration
  thresholds into a CI-runnable check so evaluation/bracket/simulation changes can't
  silently regress.

## 5. Files to create / change

- Changed: `OperationalFacts/RampOperationalModels.cs` (supported fact family),
  `RampOperationalFactExtractor.cs`, `RampContextScorer.cs` (role-aware scorer/dispatcher),
  and `RecommendationTools.cs` card-evaluation presenter.
- Changed in earlier Phase 7 slices: `Simulation/DeckSimulationService.Goldfish.Run.cs` +
  `Analysis/DeckStatistics.cs` (RNG), and `Analysis/DeckAnalysisService.Combos.cs`
  (dataset fallback).
- Still to change: `Analysis/DeckAnalysisMetrics.cs` (bracket model).
- Created: `docs/reference/local-combos.json` (+ embedded loader).
- Still to create: bracket benchmark JSON in `tests/MtgMcp.Calibration/Corpus/`, docs
  updates for that model change, and broader evaluator calibration fixtures if a future
  role expansion needs them.
- Tests: per-kind evaluator tests now cover ramp/draw/interaction and unsupported-role
  output; bracket benchmark tests; determinism tests (same seed
  -> identical results) for goldfish family; combo-fallback tests.

## 6. Testing

- Calibration suite extended and run in CI (or as a gated task) for eval/bracket/sim.
- Determinism: snapshot a seeded goldfish run and assert byte-stable repeat.
- Evaluator: draw and interaction/removal cards now return meaningful, role-correct output;
  unsupported roles such as finishers return `unsupportedRole=true` rather than an
  unexplained `Score: 0`.
- All offline.

## 7. Definition of done

- `deck_evaluate_card` evaluates ramp + draw + interaction, declares
  supported/unsupported roles in output, and has no misleading unsupported-role zero;
  covered by tests.
- Bracket estimate is density-aware and advisory, with maintainer-agreed benchmark
  expectations added to the calibration corpus *before* the model change and checked in CI.
- All simulation tools share the deterministic RNG and stamp `RngKind`; docs describe one
  determinism model.
- Local combo fallback is a real dataset, catalog-first.

## 8. Risks & mitigations

- Risk: changing analytical outputs surprises users / breaks snapshots. Mitigation: gate
  behind the calibration suite; changelog the model changes; keep assumptions/warnings.
- Risk (primary): the general evaluator drifts into a mini rules engine. Mitigation: hard
  guardrail that extractors/scorers are deterministic text/facet classifiers and bounded
  heuristics only - no stack, targeting, sequencing, or interaction modeling; land kinds
  incrementally (ramp -> draw -> interaction -> ...), each honestly scoped with declared
  coverage in output. If a role can't be evaluated deterministically, it stays
  "unsupported" rather than being faked.
- Risk: determinism switch changes numeric results. Mitigation: re-baseline calibration
  expectations in the same PR; document as a one-time recalibration.

## 9. Open questions

- `deck_evaluate_card` rename timing is decided by implementation: no ramp-scoped alias was
  introduced; the existing tool is now the general supported-role evaluator.
- Bracket model: rules-of-thumb thresholds vs a small weighted score - which best matches
  the Commander brackets beta guidance the prompt cites? (Decide alongside the agreed
  benchmark expectations in 4.2.)
