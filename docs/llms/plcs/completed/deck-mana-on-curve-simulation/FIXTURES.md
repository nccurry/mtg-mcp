# Deck Mana and On-Curve Estimate Fixtures

## Fixture Rules

All normal tests must run offline. Fixtures use small saved-deck records and
sanitized Scryfall-shaped facts. They may use real card names and printed costs
when that makes the example easier to read, but they must use fake local IDs,
fake snapshot details, and no user deck data.

Every fixture that models a land includes:

- Saved deck entry ID and copy count.
- Exact resolved printing ID.
- Type line.
- The produced-mana source state: missing, null, or listed values. A values
  state may hold an empty list.
- The caller's stated land rule.

No fixture gives a card behavior through Oracle text, a tag, a name, or a card
type other than identifying a land candidate.

## Fixture Inventory

| ID | Scenario | Purpose | Expected result |
| --- | --- | --- | --- |
| OCE-FIX-001 | Eight-card basic-land deck | Prove opening hand, on-the-play draw, simple one-color cast, and target-not-drawn result. | An ordered hand with Giant Growth and Forest casts on turn one. A library with Giant Growth eighth does not cast by turn one. Exact opening-hand chance is seven out of eight before sampling. |
| OCE-FIX-002 | Timing and color cases | Prove same-turn versus next-turn lands, color matching, generic payment, and target-first-v1 ties. | Temple of Mystery marked next-turn plus Forest can cast Grizzly Bears by turn two in the stated ordered hand. Two active sources that cannot pay Garruk's Companion's two green symbols report missing-required-color. |
| OCE-FIX-003 | Caller-rule and source-state coverage | Prove that the tool does not guess land behavior or erase incomplete source data. | Missing Temple of Mystery rule is invalid input. Explicit not-modeled keeps that entry visible and never plays it. Missing or null source data for a single-faced land is unavailable. |
| OCE-FIX-004 | Unsupported target cost | Prove early refusal of a cost outside the model. | Hydroid Krasis with X in its cost returns unsupported before sampling. A separate hybrid-cost case does the same. |
| OCE-FIX-005 | Random replay and fingerprint | Lock random values, shuffle order, hexadecimal seed handling, request ordering, same-seed replay, and concurrent isolation. | Known SplitMix64 values and one shuffle order match the checked-in reference. Equivalent ordered inputs share a modeled-input fingerprint. Changing a source fact, rule, or filler quantity changes it. |
| OCE-FIX-006 | Counts, interval, traces, and cancellation | Prove the sampled report is complete, bounded, and honest. | Zero, full, and mixed Wilson references match independently calculated values. Failure labels total every miss. Cancellation after trial 17 throws and returns no report. |
| OCE-FIX-007 | App and MCP path | Prove exact saved-deck/Scryfall joining, source details, cache-only behavior, and no download. | The tool resolves every mainboard entry by exact local identity, returns unavailable for missing local facts, and exposes the full sampled-result fields in every operation mode. |
| OCE-FIX-008 | Named performance case | Provide a repeatable review baseline after correctness. | A sanitized 99-card deck, fixed target, seed, samples, and turn limit runs in Release with recorded environment and result. |

## OCE-FIX-001: Basic Land And Target Draw

### Saved deck and source facts

| Entry | Copies | Resolved card fact | Type line | Produced mana | Caller rule |
| --- | --- | --- | --- | --- | --- |
| entry-forest | 7 | Forest printing forest-a | Basic Land — Forest | G | one-mana-same-turn |
| entry-growth | 1 | Giant Growth printing giant-growth-a | Instant | None | Not applicable; target only |

Target: entry-growth. Printed cost: G. Turn limit: 1. On the play: true. No
mulligan.

### Ordered-library checks

| Library order | Expected turn-one result | Reason |
| --- | --- | --- |
| Giant Growth first, then Forest | Success | The opening hand contains the target and a same-turn green source. |
| Seven Forest cards, then Giant Growth | Miss: target-not-drawn | The player does not draw on turn one when on the play. |

The eight possible omitted cards from a seven-card opening hand give an exact
reference chance of seven out of eight. This checks trial behavior; the public
tool still reports a sampled estimate when it runs many shuffled hands.

## OCE-FIX-002: Timing, Color, And Tie Breaks

### Timing case

| Entry | Copies | Type line | Produced mana | Caller rule |
| --- | --- | --- | --- | --- |
| entry-temple | 1 | Land | G, U | one-mana-next-turn |
| entry-forest | 1 | Basic Land — Forest | G | one-mana-same-turn |
| entry-bears | 1 | Creature — Bear | None | Target; printed cost 1G |
| filler entries | Enough copies | Non-land | None | Not modeled |

The ordered hand has Grizzly Bears and Temple of Mystery. The turn-two draw is
Forest. The trace must show:

1. Temple of Mystery played on turn one and not usable that turn.
2. Forest played on turn two.
3. Temple and Forest together pay one generic and one green mana.
4. Grizzly Bears cast by turn two.

### Color case

Use two active lands that together provide one green and one blue mana. The
target is Garruk's Companion with printed cost GG. The trial must not pretend
that blue can pay green. It ends with missing-required-color when all other
miss-label checks have lower priority.

### Tie-break case

Use two eligible same-turn lands that give the same current-turn target
progress. Their source facts and rules differ only by entry ID. The lower entry
ID must be played.

## OCE-FIX-003: Caller Rule Coverage

Use a saved deck with Forest, Temple of Mystery, Giant Growth, and filler cards.

| Request variation | Expected result |
| --- | --- |
| No rule for Temple of Mystery | Invalid input that names the missing entry. |
| Temple of Mystery marked not-modeled | Successful sampled report. Its entry and rule appear in landRuleCoverage. No trace plays it. |
| A rule for Llanowar Elves | Invalid input because the entry is not a land candidate, even though its source fact might list green mana. |
| A rule names a land with an explicit empty produced-mana list | Invalid input. |
| Same land has two rules | Invalid input. |
| A single-faced land has a missing produced-mana field | Unavailable. The result must not treat missing as an empty list. |
| A single-faced land has a null produced-mana field | Unavailable. The result must not treat null as an empty list. |
| A single-faced land has an explicit empty produced-mana list | Successful sampled report. The land is visible as known-empty, has no rule, and never supplies mana. |
| A rule names a known-empty or multi-face land | Invalid input because neither is a version 1 candidate. |

This fixture proves that a person supplies the uncertain game behavior. It does
not say that Temple of Mystery or Llanowar Elves are unusable in real Magic.

## OCE-FIX-004: Unsupported Costs

| Target | Printed cost | Expected result |
| --- | --- | --- |
| Hydroid Krasis | XGU | Unsupported before a trial starts because X is outside version 1. |
| A fixture card with a hybrid symbol | Hybrid cost | Unsupported before a trial starts. |
| A fixture card with a Phyrexian symbol | Phyrexian cost | Unsupported before a trial starts. |
| Giant Growth | G | Accepted when land rules are complete. |

The exact source fact must be shown in the error details without revealing a
local cache path or private data.

## OCE-FIX-005: Replay And Fingerprint

The implementation checks in:

- A named list of SplitMix64 version 1 values for at least two seeds.
- One exact Fisher-Yates shuffle order for an eight-card library.
- A same-seed multi-trial report reference.
- A concurrent-run case that uses two requests with different seeds.
- A fingerprint case where entry ordering changes but resolved facts and rules
  do not.
- A fingerprint case where candidate-land selection, one land rule, target
  printing, or source fact changes.
- A fingerprint case where a filler entry's quantity changes.
- A 16-character seed case with leading zeros, the all-`f` maximum, and
  malformed text.

The fixed input list uses stable entry IDs and printing IDs. Tests must never
rely on the current framework random-number algorithm.

## OCE-FIX-006: Report Math, Bounds, And Cancellation

### Wilson interval references

| Successes | Samples | Expected check |
| --- | --- | --- |
| 0 | 100 | Lower bound is zero; upper bound matches an independent Wilson calculation. |
| 100 | 100 | Upper bound is one; lower bound matches an independent Wilson calculation. |
| 50 | 100 | Center and bounds match an independent Wilson calculation. |

Use enough decimal places in the fixture helper to avoid accepting a different
interval by accident. Apply the documented final rounding rule only when the
report is displayed.

### Bounds and output

| Input | Expected result |
| --- | --- |
| 99 samples | Invalid input. |
| 100,001 samples | Invalid input. |
| Turn 0 or 13 | Invalid input. |
| More than 500 deck copies or more than 150 distinct mainboard entries | Invalid input. |
| More than two stored traces or 64 events in one trace | The runner caps output and records omitted events. |

### Cancellation

The test runner has a test-only checkpoint. It cancels after trial 17 and
expects OperationCanceledException before trial 18. There is no report object
to inspect because a partial count could look like a completed estimate.

## OCE-FIX-007: App And MCP Integration

Use one temporary local deck store and a Scryfall fixture store. The deck uses
the OCE-FIX-001 cards plus one explicitly not-modeled land.

Check:

- The request must name the current deck revision; a changed revision returns
  a conflict before Scryfall lookup or sampling.
- Exact deck, entry, and printing IDs are required for every mainboard entry.
- A sideboard target is rejected.
- The report includes deck revision, target fact, source facts, every land
  rule, seed, model/policy/random versions, interval, limits, and traces.
- The tool is in the decks toolset and is read-only in every operation mode.
- A missing, null, or not-locally-stored required source fact returns
  unavailable and does not make an HTTP request, write the local cache, or
  start a card-data download in read-only, local, or remote mode.
- The capability record and end-to-end tool count are updated deliberately.

## OCE-FIX-008: Performance Baseline

The benchmark fixture is a sanitized 99-card saved deck with:

- A declared mainboard target.
- Fully listed candidate land rules.
- A fixed seed.
- A stated sample count and turn limit.
- A Release command that is separate from normal tests.

The completion record states the hardware, command, elapsed result, allocated
memory if measured, and why the case is representative. It does not set a
universal speed promise until there is a justified baseline history.

## Requirement Coverage

| Requirement | Main fixture or test |
| --- | --- |
| OCE-001–002 | OCE-FIX-007 |
| OCE-003 | OCE-FIX-001, OCE-FIX-002, and Scryfall mapping tests |
| OCE-004–005 | OCE-FIX-003 |
| OCE-006 | OCE-FIX-004 |
| OCE-007–008 | OCE-FIX-001 and OCE-FIX-002 |
| OCE-009 | OCE-FIX-005 |
| OCE-010 | OCE-FIX-001, OCE-FIX-002, and OCE-FIX-006 |
| OCE-011–012 | OCE-FIX-006 and OCE-FIX-007 |
| OCE-013–014 | Project, architecture, Task, coverage, and end-to-end checks |
| OCE-015 | OCE-FIX-008 |
