# Stats Lab Metric Contracts

Stats Lab is deterministic Monte Carlo analysis over cached deck data. It is not a Magic rules engine. Metrics describe the behavior of the heuristic sampler for a fixed deck, profile, seed, simulation count, and turn horizon.

## Shared Rules

- Probability rows are proportions in the range `[0, 1]`.
- Confidence intervals use an approximate 95 percent Wilson interval.
- Average rows include the arithmetic mean and nearest-rank p25, p50, and p75 values.
- Turn metrics use the latest simulated state at or before the requested turn.
- A fixed seed must produce stable output for the same deck and inputs.
- Larger sample counts should narrow confidence intervals for the same metric.
- Mana payment treats each source as exclusive for a payment. A source that can produce multiple colors offers choices, but it can only satisfy one mana symbol before it is spent.
- Mulligan-enabled performance analysis uses a deterministic London mulligan policy that scores functional mana, early plays, early ramp, card flow, interaction, and commander timing. Commander and Brawl workspaces treat the first mulligan as free.

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

## Combo Assembly

- `combo-assembly-by-turn`: probability that at least two combo-tagged or finisher cards had been seen.
- `tutor-assisted-combo-by-turn`: probability that at least one combo card and one tutor had been seen.
- `averageEarliestAssemblyTurn`: average first assembly turn among successful combo assembly runs.

## Stranded Cards

- `strandedRate`: share of runs where the card remained uncastable at the final simulated turn.
- `manaStrandedRate`: share of runs where total mana was insufficient.
- `colorStrandedRate`: share of runs where color requirements were not satisfied.

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

`compare_plan_performance` applies a persisted edit plan to an in-memory preview, analyzes before and after with the same seed and simulation count, and reports:

- `before`: baseline performance.
- `after`: preview performance.
- `delta`: `after - before`.
- confidence interval bounds for scenario and turn probability deltas when available.
- whether before and after confidence intervals overlap.

Recommendation tests should assert directional changes, not exact point estimates, unless the fixture has an oracle outcome.
