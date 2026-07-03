# Commander Bracket Model

> Legacy reference: this advisory estimator is a removal target for stable
> `0.9.0`. Provider bracket fields may be returned as facts, but the rewrite
> does not infer or recommend a bracket.

`deck_estimate_commander_bracket` is an advisory pregame-discussion aid, not an
official bracket ruling.

The estimator uses live Scryfall `is:game-changer` names when the tool performs a live
lookup. Offline calibration supplies an explicit Game Changer set in
`tests/MtgMcp.Calibration/Corpus/bracket-benchmarks.json`.

## Signals

The model records visible signals for:

- Game Changers
- fast mana
- tutors
- combo pieces
- stax effects
- extra turns
- mass land denial

It then estimates from signal density and combinations rather than assigning the bracket
from the single largest signal. For example, one mass-land-denial signal is high-pressure
evidence but no longer forces bracket 4 by itself.

## Calibration

Bracket ranges are checked by `bracket-range` expectations in the calibration corpus. The
current bracket fixtures cover precon-style, upgraded casual, high-power, and cEDH-density
synthetic decks. Run `task calibrate:stats-lab -- --validate-only` for a fast corpus check,
or `task calibrate:stats-lab` for the full offline report.
