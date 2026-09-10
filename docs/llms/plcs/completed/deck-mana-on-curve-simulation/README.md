# Deck Mana and On-Curve Estimate PLC Packet

> [!IMPORTANT]
> This packet defines a public, read-only MCP tool. The implementation is
> complete. The tool estimates how often a named card can be cast by a named turn under
> stated simple land rules. It will not judge a deck or choose a card.

## Lifecycle

- Status: Completed
- Folder: docs/llms/plcs/completed/deck-mana-on-curve-simulation/
- Parent: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-08
- Last updated: 2026-09-10
- Current phase: Complete
- Independent design review: Completed 2026-09-10. See
  [REVIEW.md](REVIEW.md).
- Implementation authorized: Yes

## Summary

The planned deck_on_curve_estimate tool answers one bounded question:

> Given this saved deck, this card, these stated land rules, and this turn,
> how often did a sampled set of hands cast the card by that turn?

It uses the real saved deck list and direct Scryfall facts. The caller supplies
one simple rule for each land that may be used as a mana source. The report
shows those rules, the cards left out of the model, the random seed, the
uncertainty range, and the limits of the calculation.

This is not a game simulator. It does not parse card text, use tags as game
rules, infer land behavior, choose a line of play, estimate a win rate, rate a
card, or recommend a deck change. The agent and player make those decisions.

Exact stats_* tools remain the place for exact probability. This new deck_*
tool is visibly a sampled estimate because the result depends on a stated turn
order and land-play rule.

## What The First Version Includes

- One local saved deck and one mainboard target entry, resolved exactly.
- The target's printed mana cost when it uses only generic mana and
  W, U, B, R, G, or C.
- Direct Scryfall facts for every mainboard entry. Candidate-land facts include
  its printing ID, type line, and the exact state of its produced-mana field.
- A caller-supplied rule for every candidate land:
  one-mana-same-turn, one-mana-next-turn, or not-modeled.
- A seven-card opening hand, no mulligan, normal play-or-draw card draw, and
  one modeled land play each turn.
- One fixed, named land-play rule, target-first-v1, with stable tie breaks.
- A supplied or newly generated 16-character hexadecimal seed, a versioned
  random sequence, bounded samples, a 95% Wilson interval, and at most two
  traces.
- A read-only tool in the existing decks toolset.

## What The First Version Does Not Include

- Card-text parsing, an LLM decision, or tags as game behavior.
- Mana creatures, mana rocks, fetch lands, search, cost reduction, mana
  filtering, conditional mana, or extra land plays.
- Mulligan decisions, commanders, opponents, combat, the stack, priority,
  triggered abilities, replacement effects, alternate costs, or rules checks.
- Hybrid, Phyrexian, snow, variable, or other non-simple mana costs.
- A win rate, deck score, card score, matchup claim, recommendation, or
  automatic deck edit.

## Decision Snapshot

| Decision | Status | Reason |
| --- | --- | --- |
| Build a real-deck mana and on-curve estimate. | Chosen | It answers a useful deck question without claiming to simulate a whole game. |
| Add one read-only deck_on_curve_estimate tool. | Chosen | It belongs with deck workflows and is clearly labeled as sampled. |
| Require the caller's current deck revision. | Chosen | The tool must refuse a deck that changed after the caller read it instead of silently analyzing newer cards. |
| Keep exact Statistics unchanged. | Chosen | Exact math and sampled play-order estimates answer different questions. |
| Use direct Scryfall card facts. | Chosen | The tool needs source-backed mana cost, type line, and produced-mana facts. |
| Require caller-supplied land rules. | Chosen | The tool must not guess whether a land is usable, tapped, conditional, or restricted. |
| Model lands only in version 1. | Chosen | This avoids pretending to cast mana creatures or mana rocks. |
| Use one fixed land-play rule. | Chosen | A declared rule is reviewable and repeatable; an inferred play line is not. |
| Use a fixed random sequence and optional seed. | Chosen | A caller can repeat a run and inspect it. |
| Preserve missing, null, and empty produced-mana values separately. | Chosen | The calculation must not turn incomplete source data into a claim that a land produces no mana. |
| Read Scryfall data through cache-only exact lookups. | Chosen | The public tool must not download data or change its local cache in any operation mode. |
| Use a 16-character hexadecimal seed. | Chosen | JSON numbers cannot safely carry every 64-bit value through all MCP clients. |
| Cap a request at 150 mainboard entries. | Chosen | One exact local Scryfall collection lookup supports at most 150 identities. |
| Treat multi-face cards as outside version 1's land model. | Chosen | The tool will not guess which face a player can play. |
| Start with a private toy-card prototype. | Rejected | It would prove only a made-up card model, not a useful deck workflow. |
| Build a full Magic rules engine. | Rejected | That scope is far larger and would make the result less honest. |
| Parse Oracle text or use tags as rules. | Rejected | Neither is a reliable source of executable game behavior. |
| Put sampled results under stats_*. | Rejected | The Statistics tools promise exact calculations. |

## Evidence And Research

The design borrows bounded, repeatable ideas from other work while keeping a
clear line between facts and guesses:

- [landlord](https://docs.rs/landlord/latest/landlord/) models draw, mulligan,
  and on-curve probabilities. Its public documentation supports the value of a
  small, focused on-curve calculation.
- [landlord-ts](https://github.com/lggarrison/landlord-ts/blob/develop/wiki/concepts/landlord-ts-api.md)
  documents seeded Monte Carlo runs, cancellation, confidence intervals, and
  failure reasons such as draw, mana, and timing.
- [Mana on Curve](https://www.manaoncurve.com/) presents repeated castability
  trials and reasons a card missed its turn.
- [mindcrank](https://github.com/dylanlott/mindcrank) shows the value of a
  narrow deck calculation with repeatable random runs. Its free-form tags are
  deliberately not part of this design.
- [mystic-forge](https://github.com/Kautiontape/mystic-forge) and
  [auto-goldfish](https://github.com/jmusiel/auto-goldfish) show why automatic
  card-behavior guesses and recommendation features must stay out of this
  tool.
- [Cowling, Ward, and Powley](https://pure.york.ac.uk/portal/en/publications/ensemble-determinization-in-monte-carlo-tree-search-for-the-imper/)
  show that declared policies and clear game models matter in Magic research.
  Their work does not validate this tool as a real-game predictor.
- [NIST's interval reference](https://www.itl.nist.gov/div898/software/dataplot/refman1/auxillar/propconf.htm)
  describes the Wilson interval used to show sampling uncertainty.
- [Forge](https://github.com/Card-Forge/forge) and
  [Magic: The Gathering is Turing Complete](https://arxiv.org/abs/1904.09828)
  show why a complete Magic rules engine is not a reasonable hidden dependency
  for a narrow deck estimate.

This research supports a bounded castability estimate. It does not support a
claim about real-game strength, card quality, or win percentage.

## Packet Contents

- [SRD.md](SRD.md): Requirements, request fields, response facts, and tests.
- [SADD.md](SADD.md): Project boundary, data flow, model rules, and rejected
  designs.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): Review gate and small
  delivery steps.
- [FIXTURES.md](FIXTURES.md): Sanitized deck facts, replay cases, and
  independent reference checks.
- [REVIEW.md](REVIEW.md): Independent review findings and their resolutions.

## Owner Gates

Before implementation began:

1. An independent reviewer checks this revised packet for scope, accuracy, and
   testability.
2. The reviewer findings are fixed or recorded with a reason.
3. The owner changes Implementation authorized to Yes.

All three gates are complete. The final review gate in
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) also passed before this packet
was completed.

After implementation, the owner decides whether the output is clear enough to
keep as a public tool. That decision must rely on working code, offline tests,
an end-to-end tool check, and the stated limits—not on a toy-card experiment.

## Planning Readiness Checklist

- [x] The tool asks one real-deck question.
- [x] Exact and sampled results stay separate.
- [x] The caller, not the MCP tool, supplies the uncertain land behavior.
- [x] Tags and Oracle text have no game-behavior role.
- [x] The model has a fixed turn order and stable tie breaks.
- [x] The report carries source facts, run details, uncertainty, and limits.
- [x] The project and dependency boundaries are clear.
- [x] The old toy-card direction is recorded as rejected.
- [x] An independent reviewer has checked this revised packet.
- [x] The owner has authorized implementation.
