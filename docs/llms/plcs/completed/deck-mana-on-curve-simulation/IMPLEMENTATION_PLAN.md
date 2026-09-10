# Deck Mana and On-Curve Estimate Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-10
- Related requirements: [SRD.md](SRD.md)
- Related design: [SADD.md](SADD.md)
- Related fixtures: [FIXTURES.md](FIXTURES.md)
- Implementation authorized: Yes

## Delivery Strategy

Build one small path from source facts to a working tool:

1. Make the direct Scryfall fact available.
2. Build and test a pure on-curve calculation from resolved facts.
3. Join that calculation to a saved deck in App.
4. Register one read-only tool and prove its complete MCP behavior.
5. Measure one bounded representative run only after correctness is proven.

Do not build a generic simulation framework, a rules engine, a card-text
parser, a tag system, a player policy chooser, or a recommendation layer along
the way.

Keep unrelated changes out of this work. Another agent may be changing Mise and
Task setup. Preserve those changes and add only the narrow project, test, and
coverage entries this child needs after checking the current files.

## Phase 0: Independent Review And Owner Authorization

- Problems solved: The old review covered a rejected toy-card plan. This public
  actual-deck design needs a fresh check before code starts.
- Expected work:
  - Give the complete packet to an independent reviewer.
  - Check source fact use, scope limits, result wording, project direction,
    test cases, random replay, and public-tool contract.
  - Fix high- and medium-impact findings. Fix low-impact findings when small;
    otherwise record why the choice remains safe.
  - Reread the revised packet after fixes.
  - Change the packet to Implementation authorized: Yes only after the owner
    approves it.
- Validation:
  - Check all local links and headings.
  - Run git diff --check.
- Exit criteria:
  - The review record names all findings and their result.
  - The owner has explicitly authorized implementation.
- Stop condition:
  - If a reviewer finds a missing product choice that materially changes the
    tool, stop and ask the owner instead of making that choice in code.

### Phase 0 completion record

- Review date: 2026-09-10.
- Verdict: Minor revisions needed; all findings were fixed before Phase 1.
- Record: [REVIEW.md](REVIEW.md).
- Owner authorization: The owner asked to fix the review findings and implement
  the next phase.

## Phase 1: Add Direct Scryfall Facts And The Pure Project

- Problems solved: The calculation needs source-backed mana colors without
  reading card text or exposing provider transport types.
- Expected edits:
  - Copy Scryfall's produced-mana field into the Scryfall card model and its
    local source mapping as a closed missing/null/values fact. The existing raw
    source record continues to preserve the original source object.
  - Add src/MtgMcp.OnCurve and tests/MtgMcp.OnCurve.Tests. Make the production
    project non-packable and reference MtgMcp.Core only.
  - Add small internal value types for resolved target cost, every mainboard
    entry's direct produced-mana state, candidate entry IDs, caller land rules,
    and a request that contains no deck, provider, HTTP, SQLite, or MCP type.
    Keep the full resolved mainboard and source states so later validation and
    fingerprints cannot lose facts.
  - Give the test project friend-assembly access. Add App access only when
    Phase 3 needs to compose the public tool.
  - Add the projects to the solution, project inventory, architecture test,
    Task unit test path, coverage report, and coverage gate.
- Tests added:
  - Produced-mana card fact maps and persists as direct missing, null, empty,
    and nonempty source states.
  - OnCurve cannot reference App, Decks, Scryfall, Statistics, HTTP, or SQLite.
  - Request checks reject invalid bounds, duplicate land rules, and missing
    candidate-land rules before any random work.
- Validation:
  - Run the narrow Scryfall and OnCurve tests first.
  - Run task lint and task test after project wiring changes.
  - Run the affected coverage gate and the architecture test.
- Exit criteria:
  - A resolved request can represent only stated Scryfall facts and caller
    rules.
  - No new tool exists yet.
- Rollback:
  - Remove the new field and project together if source facts cannot be
    represented cleanly. Do not replace the field with Oracle-text parsing.

### Phase 1 completion record

- Completed: 2026-09-10.
- Delivered:
  - Direct Scryfall missing, null, empty-list, and nonempty produced-mana
    states that survive a local card-data sync and cache-only read.
  - A small Core-only OnCurve project. Its request carries every mainboard
    entry's exact printing ID, quantity, and direct produced-mana state.
  - Request checks for complete, bounded, exact entries and caller land rules.
    No random calculation or MCP tool exists yet.
  - Solution, Task, coverage, and architecture wiring for the new project.
- Review fixes:
  - Preserve source states for every mainboard entry, not only candidate lands.
  - Reject a quantity before it can overflow the total-card bound.
  - Reject malformed stored produced-mana JSON with a JSON error.
  - Keep App out of the new project until Phase 3 actually composes the tool.
- Validation:
  - `task lint` passed.
  - `task test` passed: 674 offline tests.
  - `task coverage:oncurve` passed: 98.80% line, 98.91% branch, and 100.00%
    method coverage.
  - A clean no-incremental strict solution build passed.

## Phase 2: Build The Repeatable On-Curve Calculation

- Problems solved: The server needs a small, testable calculation that can say
  what happened under declared land rules.
- Expected edits:
  - Add the simple mana-cost reader, deterministic source-to-cost matching,
    land-play rule, trial runner, report builder, failure labels, traces, and
    Wilson interval to MtgMcp.OnCurve.
  - Implement SplitMix64 version 1 and Fisher-Yates shuffle as the repeatable
    random contract. Parse and report fixed-width hexadecimal seeds. Use a
    system random source only to create a missing seed.
  - Make cancellation flow through every loop and avoid mutable static state.
  - Produce the modeled-input fingerprint from the canonical full mainboard
    and clear run details.
- Tests added:
  - Ordered-library cases for play, draw, same-turn, next-turn, and
    not-modeled lands.
  - Source choice and mana-payment tie breaks.
  - Unsupported target costs.
  - Known random values, shuffle order, hexadecimal seed boundaries, same-seed
    replay, and concurrent runs.
  - Zero, full, and mixed Wilson interval references.
  - Each defined miss-label condition and their complete priority order.
  - Cancellation after a fixed trial returns no partial report.
  - Trace cap and omitted-event behavior.
- Validation:
  - Run the OnCurve test project after each small behavior change.
  - Run task lint, task test, and task coverage when the calculation is
    complete.
- Exit criteria:
  - The fixtures show every stated model rule.
  - A supplied seed gives the same report for the same resolved request.
  - The output never treats a sampled result as exact or real-game evidence.
- Rollback:
  - Keep the pure project only if it remains small and clear. If the next
    feature requires a card interpreter, stop and ask the owner rather than
    adding one.

### Phase 2 completion record

- Completed: 2026-09-10.
- Delivered: A pure, repeatable land-only calculation with a simple printed
  mana-cost reader, stable target-first-v1 land choice, SplitMix64 version 1,
  Fisher-Yates shuffle, bounded traces, five ordered miss reasons, cancellation,
  and unrounded two-sided 95% Wilson intervals.
- Replay: The fingerprint now has named, counted sections for the full
  mainboard, candidate-land selection, and caller rules. It excludes only the
  seed and sample count. A regression test proves candidate selection changes
  the fingerprint.
- Validation: Focused OnCurve behavior, replay, random, and interval tests
  passed before App composition. The final full validation is recorded below.

## Phase 3: Join Saved Decks, Scryfall Facts, And The MCP Tool

- Problems solved: The pure calculation needs an honest public path from a
  real saved deck without leaking adapter details into it.
- Expected edits:
  - Add a small App coordinator that reads one expected saved-deck revision,
    requires exact printing IDs for every mainboard entry, resolves them in one
    cache-only exact lookup, checks rules, and builds the pure request.
  - Return not-found, unavailable, invalid-input, and unsupported outcomes
    using the existing result cases.
  - Add the read-only deck_on_curve_estimate tool, its clear description, and
    the output fields from the requirements.
  - Add it only to the existing decks toolset. Keep it visible in every
    read-only operation mode. Do not add a new toolset or configuration switch.
  - Update the capability record, static tool inventory, App architecture
    expectations, and end-to-end expected tool count.
  - Add source snapshot details, caller land-rule coverage, assumptions, and
    warnings to the displayed report.
- Tests added:
  - Local saved-deck revision, target, and land resolution.
  - Exact source identity handling and no fuzzy name fallback.
  - Missing, null, or not-locally-stored Scryfall facts return unavailable with
    no HTTP request or local-cache write.
  - Cache-only read-only tool behavior in every operation mode.
  - Request and result descriptions use sampled estimate language and contain
    no advice.
  - End-to-end MCP registration, tool count, toolset membership, and result
    shape.
- Validation:
  - Run focused App, Decks, Scryfall, and OnCurve tests.
  - Run task lint, task test, task coverage, task surface:report, and task
    smoke:mcp.
- Exit criteria:
  - An MCP client can call one read-only tool against a local saved deck and
    receive enough facts to understand the estimate and repeat it.
  - Normal tests make no network call and no local deck mutation.
- Rollback:
  - If App orchestration grows into a large mixed owner, split the deck reader,
    source resolver, and result presenter into small App-only pieces. Do not
    move this work into Core or a provider project.

### Phase 3 completion record

- Completed: 2026-09-10.
- Delivered: `deck_on_curve_estimate` in the existing `decks` toolset. It takes
  direct request fields, requires `expectedRevision`, reads every mainboard
  printing by cache-only exact Scryfall lookup, and returns source facts,
  caller-rule coverage, run details, uncertainty, limits, and up to two traces.
- Safety: The tool has no HTTP or local-cache-write fallback. Missing installed
  facts return unavailable. It does not change the saved deck.
- Tests: Focused App tests cover changed revisions, missing facts, missing
  source data, known-empty lands, multi-face targets, and missing land rules.
  Process tests cover a successful result and the same seeded local data in
  read-only, local, and remote modes.

## Phase 4: Finish, Check, And Record The First Baseline

- Problems solved: A public sampled result needs stronger evidence than unit
  tests alone and needs current documentation.
- Expected edits:
  - Update user documentation, tool inventory, capability details, PLC status,
    and deferred-work notes to match the implemented limits.
  - Add a named Release benchmark for one sanitized 99-card fixture. Make the
    Task command easy to run without changing normal tests.
  - Keep benchmark results out of generated source files unless the repository
    already stores that kind of baseline.
- Tests and checks:
  - Run all normal offline checks.
  - Run the separate benchmark and record its machine, command, inputs, and
    result in the completion record.
  - Have an independent reviewer check the changed code and public wording.
- Validation:
  - Run task lint.
  - Run task test.
  - Run task coverage.
  - Run task surface:report.
  - Run task smoke:mcp.
  - Run the named benchmark.
  - Run git diff --check and inspect documentation links.
- Exit criteria:
  - All normal checks pass.
  - Every production assembly remains at or above 90% line coverage.
  - The documented limits and the public result match the actual code.
  - Review findings are fixed or recorded with a reason.
- Rollback:
  - Keep the tool disabled from release if its meaning, replay, or source
    evidence is unclear. Do not hide a problem behind weaker descriptions.

### Phase 4 completion record

- Completed: 2026-09-10.
- Documentation: Updated public tool counts, toolset information, architecture,
  product direction, change notes, and deferred full-game simulation wording.
- Benchmark: `task benchmark:oncurve` ran the fixed 99-card Release fixture:
  one `{2}{G}` target, 38 single-color sources, 60 inert cards, 10,000 samples,
  turn 4, on the play, seed `0123456789abcdef`. It completed in 302.1 ms on a
  Lenovo 21RRS0DW00 with 24 logical processors, 127.4 GiB RAM, Windows 11 Pro
  10.0.26200 x64, and .NET SDK 11.0.100-preview.7.26381.103. This is a review
  baseline, not a speed promise.
- Validation: `task lint`, `task test`, `task coverage`,
  `task surface:report`, `task smoke:mcp`, and `task benchmark:oncurve` passed.
  OnCurve line coverage was 96.82% and branch coverage was 94.13%.
- Final review: The review fixed the fingerprint candidate selection, missing
  land-rule detail, all-mode process coverage, and stale product wording. No
  high- or medium-impact issue remains.

## Required Review Gate

Before closing any implementation phase:

1. Review the changed code, tests, and public wording.
2. Fix every high- and medium-impact finding.
3. Fix low-impact findings when practical; otherwise record why the remaining
   choice is safe.
4. Rerun the focused checks after fixes.
5. Run the full checks once before the phase is marked complete.

## Completion Record

This packet completed on 2026-09-10.

- Final tool and toolset: `deck_on_curve_estimate` in `decks`.
- Versions: `on-curve-v1`, `target-first-v1`, and `splitmix64-v1`.
- Supported model: A seven-card hand with no mulligan, normal play-or-draw
  draws, at most one caller-modeled single-faced land per turn, and target costs
  made only of generic, W, U, B, R, G, and C symbols.
- Refused or excluded behavior: Missing/null land mana facts, unsupported cost
  symbols, multi-face targets, multi-face mana sources, empty-mana lands as
  sources, nonland mana sources, card text, tags, game rules, and deck advice.
- Validation and benchmark: Recorded in the Phase 4 completion record above.
- Review: Independent design review and final code/design/test review both
  passed after their findings were fixed. See [REVIEW.md](REVIEW.md).
- Later work: Broad multiplayer or full-game simulation remains deferred to a
  separate approved packet. It must not extend this land-only model by default.
