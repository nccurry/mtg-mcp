# Commander Bracket Model

> Legacy reference: this advisory estimator is a removal target for stable
> `0.9.0`. Provider bracket fields may be returned as facts, but the rewrite
> does not infer or recommend a bracket.

`deck_estimate_commander_bracket` is an advisory pregame-discussion aid, not an
official bracket ruling.

The removed estimator used live Scryfall `is:game-changer` names and an offline
calibration corpus. Its source and fixtures remain available in Git history and
the released legacy version; they are not present on this rewrite branch.

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

The historical calibration corpus covered precon-style, upgraded casual,
high-power, and cEDH-density synthetic decks. Stable `0.9.0` will not restore
this advisor or its calibration task.
