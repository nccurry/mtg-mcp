# Deck Mana and On-Curve Estimate Design

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-10
- Related requirements: [SRD.md](SRD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Related fixtures: [FIXTURES.md](FIXTURES.md)
- Implementation authorized: Yes

## Design Summary

This is a narrow sampled calculation, not a Magic game engine.

The App reads an actual saved deck and exact Scryfall card facts. It checks the
caller's declared land rules and builds a small provider-free request. The new
OnCurve project shuffles the deck, follows one visible turn order, and returns
counts and traces. The App then adds deck revision and Scryfall source details
to the MCP result.

The calculation has no hidden card interpreter. It never reads Oracle text or
tags to decide what a card does. A Scryfall produced-mana fact says which colors
a card can produce. The caller says whether the simple model may use that land
now, later, or not at all.

## Evidence Boundary

The report says:

- This is a sampled estimate for the named model, not an exact probability.
- The deck revision, target printing, source facts, source rules, seed, and
  model version used.
- Whether the target was cast by the requested turn in the sampled hands.
- Why a sampled hand missed under the model.
- What was deliberately left out.

The report does not say:

- That a card is good, bad, strong, weak, or belongs in the deck.
- That a player should keep a hand, play a land, or make a deck change.
- That the deck wins games or has a real-game win percentage.
- That a tag, Oracle text, or card name proves a game behavior.

## Project Boundary

| Project | Responsibility | Allowed references |
| --- | --- | --- |
| MtgMcp.OnCurve | Pure request checks, random sequence, shuffle, turn calculation, mana payment, interval, failure labels, and traces. It is not packable. | MtgMcp.Core only |
| MtgMcp.OnCurve.Tests | Offline unit, replay, reference, cancellation, and bound tests. | MtgMcp.OnCurve |
| MtgMcp.Scryfall | Scryfall card facts and cache records, including the produced-mana field copied directly from Scryfall for this tool. | Existing Scryfall dependencies only |
| MtgMcp.App | Reads the saved deck, resolves facts, validates land rules, calls OnCurve, registers the MCP tool, and presents the result. | Existing projects plus MtgMcp.OnCurve |
| MtgMcp.Statistics | Exact caller-supplied mathematics. | MtgMcp.Core only |

The direction is:

| From | To | Reason |
| --- | --- | --- |
| App | Decks and Scryfall | Read the actual deck and direct facts. |
| App | OnCurve | Give the pure calculator a plain resolved request. |
| OnCurve | Core | Reuse result and shared value types only. |
| Statistics | Core | Keep exact math independent of providers and sampled work. |

OnCurve must not reference App, Decks, Scryfall, Statistics, HTTP, SQLite, or
MCP types. Scryfall must not reference OnCurve. Core must not grow a sampled
calculation type just for this feature.

Keep OnCurve types internal where practical. Phase 1 gives only its test
project friend-assembly access. Add App access in Phase 3, when it composes the
tool, so this does not become a broad reusable library API by accident.

## Data Flow

1. The read-only MCP tool accepts deckId, expected revision, targetEntryId,
   turn limit, sample count, play-or-draw setting, optional seed, and land
   rules.
2. App reads the local saved deck, requires the expected revision to match,
   and checks that the target is one mainboard entry.
3. App rejects a mainboard with more than 150 distinct entries or an entry
   without an exact printing ID. It resolves every remaining mainboard entry
   by one cache-only exact Scryfall lookup. It does not fuzzy-match names.
4. Scryfall returns direct facts from local storage only. A missing local card
   row, a missing produced-mana field on a single-faced land, or a null
   produced-mana field on a single-faced land maps to unavailable. This tool
   sends no HTTP request and writes no cache data in any operation mode.
5. App finds every land candidate: a single-faced card whose pre-dash type
   words include Land and whose produced-mana fact has known nonempty listed
   values. It checks that the caller supplied exactly one rule for each
   candidate. A known-empty value and every multi-face card remain visible but
   are outside the version 1 source model.
6. App builds a small OnCurve request from every mainboard entry and its direct
   produced-mana state, the resolved target cost, candidate entry IDs, caller
   rules, and run settings. It passes no Scryfall transport object to OnCurve.
7. OnCurve runs the fixed calculation and returns a sampled result.
8. App adds deck revision and source snapshot details, then returns the
   read-only MCP result.

## Source Facts And Caller Rules

### Scryfall facts

The Scryfall card model must add a direct produced-mana fact from the source
payload. It has three distinct cases:

| Case | Meaning | Version 1 treatment |
| --- | --- | --- |
| missing | The source object has no produced-mana field. | A single-faced land returns unavailable. |
| null | The source object explicitly sets produced-mana to null. | A single-faced land returns unavailable. |
| values | The source object supplies an ordered list, including an empty list. | A nonempty list makes an eligible single-faced land a candidate. An empty list remains visible but supplies no modeled mana. |

Use a closed `ScryfallProducedMana` union with missing, null, and values cases.
The values case carries the direct list without reclassifying it. The fact is
not a tag and it is not an instruction to model a card automatically.

The result records the resolved printing ID, mana cost, type line,
produced-mana state and values, source snapshot identity, and source retrieval
details. The saved deck keeps its own card identity and revision data.

### Caller rules

The caller gives each candidate land one of these values:

| Rule | Modeled behavior |
| --- | --- |
| one-mana-same-turn | The model may play the land once per turn. It supplies one of its listed colors immediately and on later turns. |
| one-mana-next-turn | The model may play the land once per turn. It supplies no mana that turn and one listed color from the next turn onward. |
| not-modeled | The card stays in the actual deck, but the model never plays it as a source. |

The calculation uses only colors that Scryfall listed. If a land has
conditional, restricted, or unusual behavior, the caller must either state the
simple rule they want analyzed or use not-modeled. Missing rules are invalid
input, not a silent assumption.

Mana creatures, artifacts, enchantments, spells, known-empty lands, and
multi-face cards are not candidate lands in version 1. They remain in the
library and can prevent a draw from finding a modeled land, but the calculation
does not cast or play them.

## Turn Rules

### Supported target cost

The target must have one nonempty printed mana cost built from a generic
nonnegative number plus W, U, B, R, G, and C symbols. The model pays required
colored or colorless symbols first, then pays generic mana with unused active
sources.

All other cost symbols return an unsupported result before a sampled run
begins. The calculation does not inspect card text for an alternative cost,
cost change, or special casting permission.

### Opening hand and draws

Each trial:

1. Makes a private copy of the actual mainboard card copies and shuffles it.
2. Draws seven cards.
3. Starts at turn one.
4. Draws one card at the start of each turn after turn one. On the draw, it
   also draws on turn one. On the play, it does not.
5. Plays at most one eligible modeled land each turn.
6. Tests whether active lands can pay the target cost. If so, it records the
   target cast and ends the trial.
7. Does not cast, activate, search for, discard, mill, or otherwise use any
   other card.
8. Ends unsuccessfully when the turn limit passes.

There is no mulligan in version 1. No step above claims to be the best way to
play Magic.

### Land-play rule

The only policy is target-first-v1. It sees the selected target's printed mana
cost, not Oracle text, tags, or other cards.

For every eligible modeled land in hand, it tests the board after that land
would be played. It chooses the land that, on the current turn:

1. Lets the board pay the greatest number of required colored or colorless
   target symbols.
2. Then lets the board pay the greatest total number of target cost symbols.
3. Then is usable on the same turn rather than a later turn.
4. Then has the lowest stable deck entry ID.

The policy is deliberately simple. It may not be the best real-game land play.
Its name, steps, and tie breaks are part of the report and replay contract.

### Mana payment

Each active modeled land pays at most one mana. The calculation first finds a
deterministic assignment of sources to the target's required W, U, B, R, G, or
C symbols. Remaining active sources may pay generic mana. This is a small
matching calculation, not card-text interpretation.

If more than one assignment works, source identity order chooses one. The
selected source assignment appears only in a captured trace, never as advice.

### Miss labels

Every unsuccessful trial has exactly one stable label. The runner evaluates the
final modeled state in this order:

| Priority | Label | Exact condition |
| --- | --- | --- |
| 1 | target-not-drawn | No copy of the target entry is in hand by the final modeled turn. |
| 2 | no-modeled-land-played | The target is in hand and no land with a same-turn or next-turn rule was played. |
| 3 | source-not-ready | The target is in hand, at least one modeled land was played, and no played land is active by the final modeled turn. |
| 4 | missing-required-color | The target is in hand, at least one source is active, and no deterministic source assignment can pay all required W, U, B, R, G, or C symbols. |
| 5 | not-enough-mana | The target is in hand, the required colored or colorless symbols can be paid, but too few unused active sources remain for its generic symbols. |

The labels describe this small model only; they do not diagnose why a real game
failed.

## Sampling And Result

### Random sequence and shuffle

Use a named SplitMix64 sequence and Fisher-Yates shuffle owned by OnCurve.
The version identifier is splitmix64-v1. Do not use System.Random as the
repeatability promise because its algorithm is not a stable framework contract.

The caller supplies a 16-character hexadecimal seed string. The tool accepts
upper- or lower-case input and returns lower-case text, including leading zeros.
If it is absent, App creates a 64-bit value with a system random-number source,
formats it as that string, reports it, and OnCurve uses the parsed value for
every trial. A supplied seed drives the same order, traces, counts, and interval
every time the resolved input is unchanged.

One request owns one random sequence. Trials take values from that sequence in
order. Each trial owns its shuffled library, hand, sources, turn number, and
optional trace. No mutable static state is allowed.

### Fingerprints

The report contains two clear identifiers:

- A modeled-input fingerprint based on a canonically sorted full mainboard:
  deck ID, deck revision, target entry ID, every entry ID and quantity, every
  resolved printing ID and produced-mana state, candidate-land selection and
  rule, turn limit, play-or-draw setting, model version, policy, and
  random-sequence version.
- Run details: seed and sample count.

Use SHA-256 with length-marked fields in a documented fixed order. The modeled
input fingerprint excludes seed and sample count because those describe one
run, not the modeled deck and rules. Changing a filler-card quantity changes
the fingerprint. Reordering equivalent entries does not. The report includes
both identifiers so a caller can repeat a result exactly.

### Interval

For completed samples, report:

- Integer successful trials.
- Integer completed trials.
- Success rate.
- Two-sided 95% Wilson confidence interval.

Use the standard normal value 1.959963984540054 and document the rounding
rule. Tests must independently check zero successes, all successes, and a
mixed result. The interval shows sampling variation for this declared model.
It says nothing about variation in real games or opponents.

### Traces

Keep no more than:

- The first successful trial.
- The first unsuccessful trial.

Each trace has no more than 64 events plus an omitted-event count. Events
include draw, modeled land play, skipped not-modeled candidate, target cast,
and terminal result. Capturing a trace must not change any later random result.

## Report And Limits

| Input or output | Version 1 limit |
| --- | --- |
| Turn limit | 1 through 12 |
| Sample count | 100 through 100,000 |
| Target entries | Exactly one |
| Total deck copies | 7 through 500 |
| Distinct mainboard entries | 1 through 150 |
| Candidate land rules | Exactly one per candidate |
| Captured traces | At most two |
| Events per trace | At most 64 |

The runner checks cancellation before each trial and at deterministic points
during a captured trace. Cancellation throws OperationCanceledException. It
does not return a short report that could look complete.

Invalid request fields return invalid input. Unsupported target cost syntax
returns unsupported. Missing saved deck data returns not found. Missing
required Scryfall facts return unavailable.

## Projects, Tests, And Benchmark

Add MtgMcp.OnCurve and MtgMcp.OnCurve.Tests to the solution, project inventory,
Task unit test path, coverage report, coverage gate, and architecture tests.
The App architecture test must update its expected project references and the
expected tool count after the public tool exists.

Add focused tests for:

- Exact deck and printing resolution for every mainboard entry.
- Produced-mana source fact mapping and cache persistence for missing, null,
  empty, and nonempty values.
- Missing, known-empty, multi-face, and not-modeled land rules.
- Simple target cost acceptance and unsupported symbols.
- Ordered-library turn cases, mana payment, and land-play tie breaks.
- Random values, shuffle, hexadecimal seed parsing and reporting, same-seed
  replay, generated-seed reporting, and concurrent runs.
- Wilson interval references, failure-label totals, trace limits, and
  cancellation.
- Tool descriptions, read-only mode behavior, capability metadata, result
  shape, cache-only exact reads, and no HTTP or cache write.

Add a named Release benchmark only after behavior tests pass. It uses a
sanitized 99-card deck fixture with a stated target, turn limit, sample count,
and seed. Record the machine, command, and result as a review baseline. Do not
turn the first number into a broad performance promise.

## Alternatives Not Chosen

| Alternative | Why it is not chosen |
| --- | --- |
| A private toy-card prototype | It would not test an actual saved-deck workflow. |
| A complete Magic rules engine | The rules scope is too broad for an honest first estimate. |
| Automatic Oracle-text, tag, or LLM behavior mapping | It would hide guesses as game facts. |
| A source rule inferred from land type or name | It would silently assume behavior the source data does not prove. |
| Modeling mana rocks and mana creatures now | It requires casting, activation, and timing rules outside this small model. |
| A new Statistics tool | Statistics remains exact; this result is sampled. |
| Reusing the old simulator design | Its broader profiles and behavior rules do not fit this evidence boundary. |
