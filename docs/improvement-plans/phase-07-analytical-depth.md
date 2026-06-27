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

- **P10 (real fix) - `deck_evaluate_card` is ramp-only.** `CardOperationalFacts` has a
  single typed fact slot, `Ramp?` (`OperationalFacts/RampOperationalModels.cs:21`); the
  scorer returns `Score = 0` for non-ramp cards (`RampContextScorer.cs:26-31`). The tool is
  advertised generically.
- **P12 - bracket estimator is a coarse max-signal floor.**
  `EstimatedBracket = max(signal.SuggestedBracket)` (`DeckServiceBase.AnalysisMetrics.cs:571-574`);
  one mass-land-denial card forces bracket 4 with little density sensitivity.
- **P13 (real fix) - goldfish determinism.** Goldfish family uses `System.Random`
  (`DeckSimulationService.Goldfish.cs:249`) while Stats Lab/race use
  `DeterministicSimulationRandom`.
- **P14 - offline combo fallback is 3 hardcoded combos**
  (`Analysis/DeckAnalysisService.Combos.cs:351-353`).

## 2. Goals / non-goals

Goals:
- A general, honest card-evaluation framework covering more than ramp, or a clearly scoped
  set of per-role evaluators with no misleading `Score: 0`.
- A density-aware, still-advisory, still-explainable Commander bracket estimate.
- One determinism model across all simulation tools.
- A meaningful local combo dataset (catalog-first, dataset fallback).
- Calibration/benchmark coverage so analytical changes are regression-safe.

Non-goals:
- No full rules engine, no opaque ML model, no claim of true matchup win rates. Keep every
  high-level result carrying assumptions/warnings/confidence (the existing Stats Lab
  pattern).

## 3. Current state (investigation)

- Operational facts are ramp-only by type (`CardOperationalFacts.Ramp?`), so the evaluator
  cannot represent draw/interaction/removal/tutor/payoff value. Evidence/Warnings lists
  exist and are reusable.
- Bracket signals are real (live Game Changers via `is:game-changer`, fast mana, tutors,
  stax, combo, extra-turn, mass-land-denial) but combined by max, not density.
- Determinism: `DeterministicSimulationRandom` (SplitMix64) is already used by
  `DeckPerformanceAnalyzer` and the race; the goldfish/board/win-turn/optimistic-compare
  paths and `DeckStatistics` draw odds use `System.Random`.
- Calibration harness exists: `tests/MtgMcp.Calibration/` (runner, report writer, corpus
  loader, benchmark JSON e.g. `kinnan-benchmark.json`, `niv-mizzet-benchmark.json`,
  `expanded-public-benchmarks.json`) + `tests/MtgMcp.Calibration.Tests/StatsLabCalibrationTests.cs`
  and a `task calibrate:stats-lab` workflow. This is the vehicle for regression safety.
- Combos are catalog-first (Commander Spellbook) with clearly labeled local heuristics;
  only the no-catalog fallback is thin.

## 4. Workstreams

### 4.1 General card evaluation framework
- Extend `CardOperationalFacts` from a single `Ramp?` slot to a family of operational fact
  kinds, modeled as a union/closed set (coordinate with Phase 4). Each kind has a
  deterministic extractor and a scorer.
- **Explicit first scope: `Draw` and `Interaction`** (in addition to the existing `Ramp`).
  Land these three first; `Removal`, `Tutor`, `Payoff`, `Protection`, etc. are later,
  incremental slices. Keep each addition small.
- **Output must declare coverage.** Every evaluation result lists which roles are supported
  and explicitly states when a card's role is *not yet supported* (e.g.
  `evaluatedRoles: ["ramp","draw","interaction"]`, `unsupportedRole: true` with a clear
  note), instead of returning a misleading `Score: 0`. Remove the ramp-only short-circuit.
- Hard guardrail: extractors/scorers are **deterministic text/facet classifiers and
  bounded heuristics only** - no stack/sequencing/interaction simulation. This is a
  rubric, not a rules engine (see Risks).
- Coordinate with Phase 5 (extract a `CardEvaluationService`) and Phase 1/2 (`detailLevel`).
- Rename timing (resolved, see Phase 1): Phase 1 already renamed the ramp-only tool to an
  honest ramp-scoped name (e.g. `deck_evaluate_ramp_card`). This phase introduces the
  *general* evaluator under `deck_evaluate_card` (or a clear general name) and deprecates the
  interim ramp-scoped name through the normal window. Every shipped version stays honest.

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
- Switch the goldfish family and `DeckStatistics` Monte Carlo from `System.Random` to
  `DeterministicSimulationRandom`, and stamp `RngKind` on their results (Phase 0 added the
  field/labeling; Phase 7 makes the generator actually deterministic-stable).
- One documented determinism model: same seed -> stable output across .NET versions for
  every simulation tool. Update `docs/stats-lab-metrics.md` / `docs/simulation-profiles.md`.

### 4.4 Local combo dataset
- Replace the 3 hardcoded combos with a small checked-in dataset under `docs/reference/`
  (the architecture doc already sanctions local fixtures there), still catalog-first and
  still labeled `local-pattern`/heuristic. Keep it bounded and attribution-aware.

### 4.5 Calibration + regression safety
- Expand the calibration corpus (more archetypes/colors/power levels) and wire calibration
  thresholds into a CI-runnable check so evaluation/bracket/simulation changes can't
  silently regress.

## 5. Files to create / change

- Change: `OperationalFacts/RampOperationalModels.cs` (-> general fact family),
  `RampContextScorer.cs` (-> role-aware scorer/dispatcher) and new per-kind extractors/
  scorers; `DeckServiceBase.AnalysisMetrics.cs` (bracket model);
  `Simulation/DeckSimulationService.Goldfish.cs` + `DeckStatistics.cs` (RNG);
  `Analysis/DeckAnalysisService.Combos.cs` (dataset fallback).
- Create: `docs/reference/local-combos.json` (+ loader), bracket/eval benchmark JSON in
  `tests/MtgMcp.Calibration/Corpus/`, docs updates.
- Tests: per-kind evaluator tests; bracket benchmark tests; determinism tests (same seed
  -> identical results) for goldfish family; combo-fallback tests.

## 6. Testing

- Calibration suite extended and run in CI (or as a gated task) for eval/bracket/sim.
- Determinism: snapshot a seeded goldfish run and assert byte-stable repeat.
- Evaluator: non-ramp cards (removal, draw, finisher) now return meaningful, role-correct
  output rather than `Score: 0`.
- All offline.

## 7. Definition of done

- `deck_evaluate_card` (or its renamed successor) evaluates ramp + draw + interaction,
  declares supported/unsupported roles in output, and has no misleading `Score: 0`;
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

- `deck_evaluate_card` rename timing is decided (see Phase 1 and 4.1 above): ramp-scoped
  rename in Phase 1, general evaluator under the general name here, one deprecation of the
  interim name. No longer open.
- Bracket model: rules-of-thumb thresholds vs a small weighted score - which best matches
  the Commander brackets beta guidance the prompt cites? (Decide alongside the agreed
  benchmark expectations in 4.2.)
