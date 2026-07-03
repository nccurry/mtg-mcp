# Stats Lab Metric Contracts

> Legacy reference: these are sampled heuristic contracts for the current
> server. Stable `0.9.0` implements exact statistics from caller-supplied groups
> in `MtgMcp.Statistics`; it does not retain Stats Lab simulation. See the
> [exact-statistics PLC](llms/plcs/planned/exact-deck-statistics/README.md).

Stats Lab is deterministic Monte Carlo analysis over cached deck data. It is not a Magic rules engine. Metrics describe the behavior of the heuristic sampler for a fixed deck, profile, seed, simulation count, and turn horizon.

## Shared Rules

- Probability rows are proportions in the range `[0, 1]`.
- Confidence intervals use an approximate 95 percent Wilson interval.
- Average rows include the arithmetic mean and nearest-rank p25, p50, and p75 values.
- Turn metrics use the latest simulated state at or before the requested turn.
- A fixed seed must produce stable output for the same deck and inputs when
  `modelVersion`, `deckFingerprint`, `cardDataFingerprint`,
  `profileFingerprint`, simulation count, and turn horizon match.
- `schemaVersion` describes the result shape. `modelVersion` describes the
  deterministic heuristic behavior. A changed `modelVersion` may intentionally
  change probabilities even when the same seed is used.
- `rngKind` identifies the deterministic random source used for replay.
- Related seeded simulation and odds tools use the same `mtgmcp-splitmix64-v1`
  random source, while their model labels still identify their own heuristic family.
- Larger sample counts should narrow confidence intervals for the same metric.
- Mana payment treats each source as exclusive for a payment. A source that can produce multiple colors offers choices, but it can only satisfy one mana symbol before it is spent.
- Mulligan-enabled performance analysis uses a deterministic London mulligan policy that scores functional mana, early plays, early ramp, card flow, interaction, and commander timing. Commander and Brawl workspaces treat the first mulligan as free.
- Profile-aware tools include `profileResolution`, which reports the selected simulation profile, source, auto-profile candidates, deck intent overrides, and non-fatal warnings.

## Replay Metadata

`deck_analyze_performance` and `deck_plan_compare_performance` should be
presented with their replay metadata when results are compared or stored:

- `modelVersion`: deterministic heuristic behavior version.
- `schemaVersion`: result contract shape.
- `deckFingerprint`: sampled deck construction inputs.
- `cardDataFingerprint`: cached card fact inputs.
- `profileFingerprint`: resolved simulation profile inputs.
- `rngKind`, `seed`, `simulations`, `maxTurn`, and `includeMulligans`.

Both tools default to `detailLevel=full` for the raw model payload. Compact
`normal` and `summary` output still carries replay metadata, settings,
commander context, key metrics, failed scenarios, stranded-card risk, warnings,
and assumptions so callers can compare bounded results safely.
Use `mtg://usage/simulation-tool-selection` when deciding whether the task
needs Stats Lab performance, a no-interaction goldfish sequence, board
projection, win-turn estimate, or deck-to-deck goldfish comparison instead.

Treat matching metadata as the condition for deterministic replay. If any of
these values differ, metric changes may come from input or model changes rather
than deck quality alone.

## Opening Hands

- `sevenCardKeepRate`: share of runs that kept the first seven-card hand under the current mulligan heuristic.
- `averageMulligans`: average mulligans taken per run.
- `averageKeptHandSize`: average card count in kept opening hands.
- `averageKeptLands`: average land count in the kept opening hand.
- `noLandSevenRate`: share of first seven-card hands with zero lands.
- `oneLandSevenRate`: share of first seven-card hands with exactly one land.
- `floodedSevenRate`: share of first seven-card hands with six or more lands.
- `mulliganDistribution`: count of runs by mulligans taken.

## Turn Probabilities

- `land-drop-by-turn`: probability that lands in play kept pace with one land per turn, capped at ten turns.
- `ramp-seen-by-turn`: probability that a card classified as ramp had been seen.
- `ramp-cast-by-turn`: probability that a card classified as ramp had been cast.
- `draw-seen-by-turn`: probability that a card classified as draw had been seen.
- `draw-cast-by-turn`: probability that a card classified as draw had been cast.
- `interaction-seen-by-turn`: probability that interaction or a board wipe had been seen.
- `interaction-held-up-by-turn`: probability that interaction was still in hand and payable with unused mana.
- `on-curve-untapped-mana-by-turn`: probability that untapped available mana met or exceeded the turn number.
- `all-deck-colors-by-turn`: probability that every inferred deck color was available.

## Turn Averages

- `available-mana-after-development`: unused mana after the heuristic has played land, cast commander when possible, and cast prioritized spells.
- `cards-in-hand`: cards remaining in hand at end of turn.

## Castability

- `castable-nonland-hand-rate`: average share of nonland cards in hand that are payable with current untapped mana sources using exclusive source assignment.
- `source-{color}-by-turn`: probability that the named color was available by turn.

## Commander

- `commander-cast-by-turn`: probability that a commander-category card was payable and cast by turn.
- `commander-protected-by-turn`: probability that the commander had been cast and protection was either held up or on board.
- `averageEarliestCastTurn`: average first commander cast turn among runs where the commander was cast.
- `background-cast-by-turn`: probability that a command-zone Background was payable and cast by turn.
- `commander-with-background-online-by-turn`: probability that both a non-Background commander and command-zone Background were online by turn.
- `commandZone.averageCommanderCastTurn`: average first non-Background commander cast turn among runs where one was cast.
- `commandZone.averageBackgroundCastTurn`: average first Background cast turn among runs where one was cast.
- `commandZone.averageCommanderWithBackgroundOnlineTurn`: average first turn where commander plus Background were both online.

## Combo Assembly

- `combo-assembly-by-turn`: probability that at least two combo-tagged or finisher cards had been seen.
- `tutor-assisted-combo-by-turn`: probability that at least one combo card and one tutor had been seen.
- `averageEarliestAssemblyTurn`: average first assembly turn among successful combo assembly runs.

Goldfish win estimates prefer exact combo evidence and configured win routes
before fallback pressure heuristics. Route evidence records matched and missing
requirements so callers can distinguish configured routes from low-confidence
fallback wins.

## Stranded Cards

- `strandedRate`: share of runs where the card remained uncastable at the final simulated turn.
- `manaStrandedRate`: share of runs where total mana was insufficient.
- `colorStrandedRate`: share of runs where color requirements were not satisfied.

## Scorecard

`scorecard.dimensions` derives scan-friendly dimensions from the metrics above.
It is not a universal deck power score and should not be averaged into one.
Present dimensions as separate metric evidence tied to a deck goal, a benchmark
expectation, or a before/after plan comparison.

- `mana-stability`: early land drops, untapped mana, and all-color access when
  color identity is known.
- `early-development`: early ramp, draw, and retained hand resources.
- `interaction-readiness`: interaction held up by the profile-relevant early
  turn.
- `route-assembly`: combo or tutor-assisted route assembly.
- `castability`: average nonland hand castability at the simulated horizon.
- `stranded-resilience`: inverted high-mana stranded-card risk, so higher is
  better.

## Trace Summary

`traceSummary` is bounded replay context, not a full game log. It includes
aggregate counters across all runs plus a small deterministic sample of
run summaries with per-run seed, mulligans, land drops,
command-zone timing, route assembly timing, stranded-card count, and turns
where interaction was held up.

Aggregate counters distinguish `no-mulligan-runs` from `kept-seven-runs`;
Commander and Brawl first free mulligans can still keep a seven-card hand.

Trace samples are useful for explaining why a metric moved. They are not
representative game transcripts and should not be treated as complete action
logs.

## Scenarios

- `commander-by-turn-4`: probability of commander deployment by the profile-adjusted target turn.
- `commander-with-protection-by-turn-5`: probability of commander deployment with protection by the profile-adjusted target turn.
- `graveyard-hate-by-turn-3`: probability of seeing graveyard hate by the profile-adjusted target turn.
- `all-colors-by-turn-3`: probability of all inferred deck colors being available by the profile-adjusted target turn.
- `hold-up-interaction-by-turn-4`: probability of being able to hold up interaction by the profile-adjusted target turn.
- `combo-or-tutor-assembly-by-turn-5`: probability of tutor-assisted combo assembly by the profile-adjusted target turn.
- `stranded-high-mana-risk-by-max-turn`: risk rate for ending the simulation with one or more stranded high-mana cards. Lower is better.

Each scenario includes relevant cards, assumptions, failure drivers, and observed failure-driver counts derived from run states.

## Plan Comparison

`deck_plan_compare_performance` applies a persisted edit plan to an in-memory preview, analyzes before and after with the same seed and simulation count, and reports:

- `before`: baseline performance.
- `after`: preview performance.
- `delta`: `after - before`.
- confidence interval bounds for scenario and turn probability deltas when available.
- whether before and after confidence intervals overlap.

Recommendation tests should assert directional changes, not exact point estimates, unless the fixture has an oracle outcome.

## Markdown Summaries

Internal developer artifacts may use `DeckPerformanceMarkdownSummary` to render
one analysis as Markdown. The summary includes replay metadata, scorecard
dimensions, key scenarios, bounded trace context, warnings, and advisory
language that explicitly rejects objective power ranking.
