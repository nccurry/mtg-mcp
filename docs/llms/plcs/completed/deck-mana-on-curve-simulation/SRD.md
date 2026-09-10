# Deck Mana and On-Curve Estimate Requirements

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-10
- Related design: [SADD.md](SADD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Related fixtures: [FIXTURES.md](FIXTURES.md)
- Implementation authorized: Yes

## Purpose

The tool estimates the chance that one named card can be cast by a
named turn from one saved deck. It models only card draw, one land play per
turn, simple land mana, and the target card's printed simple mana cost.

The result gives the player and agent information. It does not decide whether a
card belongs in a deck, how to sequence a real game, or whether a deck is good.

## Scope

### Included

- One new read-only deck_on_curve_estimate MCP tool in the existing decks
  toolset.
- One local saved deck, one mainboard target entry, its exact revision, and at
  most 150 distinct mainboard entries.
- Direct Scryfall facts for every mainboard entry: exact printing ID, mana
  cost, type line, and the distinct missing, null, or listed-values state of
  produced mana.
- A caller-supplied rule for every eligible single-faced land with one or more
  listed produced-mana colors.
- A fixed opening hand, play-or-draw draw schedule, land-only mana model, and
  one named land-play rule.
- Repeatable sampled runs, a Wilson confidence interval, visible failure
  counts, up to two traces, and visible model limits.
- Offline unit, integration, end-to-end, coverage, and benchmark checks.

### Excluded

- A full rules engine or a general goldfish player.
- Automatic behavior guesses from Oracle text, Scryfall tags, an LLM, card
  names, or card types beyond identifying a land candidate.
- Mana creatures, mana rocks, treasure, rituals, fetch lands, tutors, filters,
  cost changes, or extra land plays.
- Mulligan choices, commander rules, opponents, combat, the stack, priority,
  triggers, replacement effects, and alternate casting permissions.
- Target costs with X, hybrid, Phyrexian, snow, variable, or other unsupported
  symbols.
- Multi-face cards as a target or modeled mana source. They remain visible as
  cards the version 1 model did not use.
- Scores, rankings, recommendations, deck edits, win rates, or real-game
  predictions.
- Any change to exact Statistics behavior or its provider-independent project
  boundary.

## Request And Report

### Request fields

| Field | Required | Meaning |
| --- | --- | --- |
| deckId | Yes | The local saved deck to read. |
| expectedRevision | Yes | The revision returned by deck_get or another deck read. The tool returns a conflict if the deck changed first. |
| targetEntryId | Yes | A mainboard entry in that deck. The tool resolves its exact printing. |
| turnLimit | Yes | The last modeled turn. Version 1 accepts 1 through 12. |
| sampleCount | No | Number of sampled hands. Default: 10,000. Version 1 accepts 100 through 100,000. |
| onThePlay | No | True means no normal draw on turn one. Default: true. |
| seed | No | Exactly 16 hexadecimal characters, case-insensitive on input and lower-case in output. If absent, the tool creates and reports one. |
| landRules | Yes | One rule for every land candidate in the saved deck. |

Each land rule names one deck entry and one value:

| Rule | Meaning |
| --- | --- |
| one-mana-same-turn | The land can be played and provide one mana of any color listed by its Scryfall produced-mana fact on that turn and later turns. |
| one-mana-next-turn | The land can be played this turn but provides one listed color only on later turns. |
| not-modeled | Keep the actual card in the deck, but never play it as a mana source in this estimate. |

A land candidate is a single-faced mainboard entry whose exact Scryfall type
line identifies it as a land and whose produced-mana fact has one or more known
listed colors. The request must include exactly one rule for every such entry.
The caller may use not-modeled when a card is conditional or otherwise outside
the simple model.

Every mainboard entry must name one exact Scryfall printing. For a single-faced
land, a missing or null produced-mana field returns unavailable. An explicit
empty list remains visible in the report as a land with no modeled mana; it is
not a candidate and cannot have a rule. Multi-face cards are not candidates and
remain visible as outside the version 1 model.

The tool must reject a duplicate, unknown, missing, or non-land rule. It must
not silently treat an unlisted land as an untapped basic land.

### Report fields

Every successful report must include:

| Field | Meaning |
| --- | --- |
| kind | The fixed value sampled-on-curve-estimate. |
| modelVersion, policyId, and randomVersion | The exact rules used for this result. |
| deckId and deckRevision | The saved deck read by the tool. |
| target | The selected deck entry, resolved printing, name, and printed mana cost. |
| sourceFacts | The Scryfall printing IDs, type lines, and distinct produced-mana states used by the run. |
| sourceEvidence | The installed Scryfall data generation and checksum used by the run. |
| landRuleCoverage | Every candidate land and caller rule, every known-empty land, and every multi-face card left out of version 1. |
| inputFingerprint | A stable identifier for the full canonical mainboard, deck and target IDs, quantities, resolved source facts, candidate lands, rules, turn setting, model, and policy. It excludes seed and sample count. |
| seed, sampleCount, turnLimit, and onThePlay | The details needed to repeat this run. |
| successCount, successRate, and interval | The completed trials, sampled rate, and named 95% Wilson interval. |
| failureCounts | Mutually exclusive reasons a sampled hand did not cast the target by the turn limit. |
| assumptions and warnings | What the estimate models and what it leaves out. |
| traces | At most one successful and one unsuccessful sampled hand, each with a fixed event limit. |

The report must call the rate a sampled estimate. It must never call it a deck
rating, win rate, card rating, recommendation, or a prediction of a real game.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- |
| OCE-001 | Must | The tool is one public, read-only deck_on_curve_estimate tool in the existing decks toolset. | Tool registration, descriptions, mode visibility, capability record, and end-to-end checks agree. The tool changes no deck data. |
| OCE-002 | Must | The tool reads one local saved deck with no more than 150 distinct mainboard entries and one exact mainboard target entry. | A missing deck or entry returns the existing not-found result. A sideboard target, missing mainboard printing identity, or too many entries returns invalid input. |
| OCE-003 | Must | The tool gets the target and land facts from direct locally stored Scryfall data. | The Scryfall model and cache expose mana cost, type line, printing identity, source snapshot details, and separate missing, null, and listed-value states for produced mana. The tool uses cache-only exact lookups and maps an unavailable local fact to OperationUnavailable without HTTP or a cache write. |
| OCE-004 | Must | The caller supplies one explicit rule for every candidate land. | Missing, duplicate, unknown, wrong-kind, known-empty, and multi-face rules fail before sampling. A not-modeled rule remains visible in the report. |
| OCE-005 | Must | Version 1 models only eligible single-faced lands and never derives behavior from Oracle text, tags, an LLM, or a card name. | Source code and tests contain no parser, tag lookup, behavior classifier, or automatic land rule. Mana creatures, mana rocks, known-empty lands, and multi-face cards remain normal deck cards but are not source candidates. |
| OCE-006 | Must | The target must have one printed simple mana cost. | Generic mana plus W, U, B, R, G, and C are accepted. Unsupported cost symbols return an existing unsupported result before sampling. |
| OCE-007 | Must | The model uses a seven-card hand, no mulligan, the stated play-or-draw schedule, one modeled land play per turn, and no other spell actions. | Ordered-library tests prove each rule. The report repeats all stated rules as assumptions. |
| OCE-008 | Must | The model uses the named target-first-v1 land-play rule and stable tie breaks. | Tests show the same starting library always picks the same land and reaches the same result. The report shows the policy ID. |
| OCE-009 | Must | A supplied 16-character hexadecimal seed repeats a run exactly. An absent seed is generated once and reported. | Leading-zero, largest-value, invalid-text, random-value, shuffle, replay, and concurrent-run tests pass. System.Random is not the replay contract. |
| OCE-010 | Must | The result includes count, rate, a two-sided 95% Wilson interval, and failure reasons. | Zero, full, mixed, and known ordered-library cases match independent reference values. Failure reasons are mutually exclusive and exhaustive for completed misses. |
| OCE-011 | Must | The result makes source facts, caller rules, unmodeled cards, limits, and uncertainty clear. | Response and end-to-end tests show the required fields. Descriptions do not make a quality, strategy, or real-game claim. |
| OCE-012 | Must | Work and output stay bounded, cancellation works, and no completed-looking partial report is returned. | Bounds reject invalid requests. A deterministic cancellation test throws OperationCanceledException. Reports have at most two traces and 64 events per trace. |
| OCE-013 | Must | The new calculation stays separate from Core, Statistics, Scryfall transport, and deck persistence. | MtgMcp.OnCurve references Core only. App owns deck/Scryfall coordination and MCP output. Statistics stays exact and provider-independent. Architecture tests enforce the dependency direction. |
| OCE-014 | Must | Normal tests remain offline, deterministic, and covered by the repository gates. | Task unit, integration, coverage, surface, smoke, and documentation checks include the new project and tool. Each production assembly remains at or above 90% line coverage. |
| OCE-015 | Should | A named benchmark records the cost of a bounded representative run after correctness is established. | A Release benchmark for a 99-card saved-deck fixture records environment, samples, turns, and result. It is a review baseline, not a claim about all decks. |

## Expected Outcomes

| Situation | Outcome |
| --- | --- |
| The deck, target, Scryfall facts, rules, and bounds are valid. | OperationSuccess with one sampled estimate report. |
| The deck or selected entry does not exist. | Existing OperationNotFound result. |
| A required Scryfall fact is missing, null, or not stored locally. | Existing OperationUnavailable result. The tool uses a cache-only exact lookup and does not send HTTP or write the local cache. |
| A rule is missing, repeated, unknown, not a land, or outside the request bounds. | Existing OperationInvalidInput result. |
| The target's printed mana cost has an unsupported symbol. | Existing OperationUnsupported result. |
| The caller cancels. | OperationCanceledException; no partial report. |

## Quality Checks

| Quality | Measure |
| --- | --- |
| Honest meaning | The output says sampled estimate and lists the model limits. |
| Repeatable result | The same canonical mainboard, source facts, rules, model, policy, seed, sample count, and play-or-draw setting return the same report. |
| Clear ownership | OnCurve does pure calculation; Scryfall owns card facts; Decks owns saved decks; App joins them and presents the tool. |
| No hidden behavior | Every modeled land has a caller rule. Every omitted candidate land is visible. |
| Safe work | Bounds, cancellation, no network request, and short traces protect the server. |
| Plain English | User-facing names and descriptions say what the calculation did and did not do. |

## Traceability

| Requirement | Design section | Fixture or test |
| --- | --- | --- |
| OCE-001–002 | [Project boundary](SADD.md#project-boundary) | OCE-FIX-001, tool registration, and end-to-end test |
| OCE-003–005 | [Source facts and caller rules](SADD.md#source-facts-and-caller-rules) | OCE-FIX-002 and Scryfall contract tests |
| OCE-006–008 | [Turn rules](SADD.md#turn-rules) | OCE-FIX-001 through OCE-FIX-004 |
| OCE-009–010 | [Sampling and result](SADD.md#sampling-and-result) | OCE-FIX-005 and OCE-FIX-006 |
| OCE-011–012 | [Report and limits](SADD.md#report-and-limits) | OCE-FIX-003, OCE-FIX-006, and tool tests |
| OCE-013–015 | [Projects, tests, and benchmark](SADD.md#projects-tests-and-benchmark) | Architecture, Task, coverage, and benchmark checks |
