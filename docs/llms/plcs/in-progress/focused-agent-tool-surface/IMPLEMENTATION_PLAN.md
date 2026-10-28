# Implementation Plan: Focused Agent Tool Surface

## Execution Boundary

This plan covers only the requirements in [SRD.md](SRD.md). Findings during
the work do not add requirements or phases on their own. New deliverables,
contract or architecture changes, or much larger work need owner approval
first. Routine repairs inside the approved scope may go ahead.

Playgroup is frozen. Do not rename, merge, or change Playgroup tools.

Move this packet to `in-progress/` before the first code edit.

## Phases

### Phase 1: Schemas, descriptions, conflict data, and baseline

No tool names change and no valid request changes meaning. Enum values on
the wire stay the same; values that the code already rejected are now also
rejected by the schema.

1. Spike: publish `scryfall_search.freshnessPolicy` as a schema enum, and try
   rejecting an unknown property on the same tool. Capture the generated
   schema and behavior and record both in the README evidence table. If
   either fails, use the `TransformSchemaNode` fallback in the SADD. Do not
   turn on unknown-property rejection yet; it is breaking and ships in
   phase 3. In the same spike, confirm that SDK 2.2.0 can set `isError` on a
   structured result, set tool `_meta`, send server instructions, and serve
   prompts. Also find where it lets the server set argument serializer
   options and catch argument binding errors (ERR-003, SCHEMA-007). Record
   each result; SAFE-002 and PROMPT-001 are deferred with the reason if the
   SDK cannot do them.
2. Set `isError: true` on every failed result through one shared App wrapper,
   and add the per-tool failing-call test and the E2E check (ERR-002).
   Accept `kind` in any position for every union input, and turn argument
   binding failures into `invalid-input` with the JSON path (SCHEMA-007,
   ERR-003, cases X01 to X03). Compare Archidekt secondary categories as a
   set in `EntryEqual` (ARCH-001, cases A01 to A03). Link pushed entries and
   categories to their new Archidekt IDs in the baseline transaction
   (ARCH-002, cases A04 to A07).
3. Publish the remaining choice parameters as schema enums with `[AllowedValues]`;
   a list used more than once gets one shared attribute subclass.
4. Add the SCHEMA-001 test (every choice parameter has `enum`).
5. Rewrite `stats_*` descriptions with fields and examples; add the example
   fixtures (SCHEMA-003).
6. Rewrite the `scryfall_search`, `scryfall_tag_search`, and
   `scryfall_cards_by_tag` descriptions for the tag path (FLOW-001).
7. Add the current revision or remote state to every conflict result
   (ERR-001).
8. Add the `tools/list` order smoke test (SCHEMA-005) and the description
   length test (SCHEMA-008). Measure the largest default result of every
   tool and record it in the README size table (SIZE-001).
9. Write the agent task set fixtures (EVAL-001), mark the held-out tasks
   (EVAL-003), then run the task set against `0.10.0` and record the
   baseline (EVAL-002). Tasks T18 to T20, T22 to T28, T30 to T35, and T37 are
   skipped until phase 4, and T16, T17, T21, T29, and T36 until phase 6.

Checks: `task build`, `task test`, `task surface:report`, `task smoke:mcp`.

Exit: SCHEMA-001 to SCHEMA-003, SCHEMA-005, SCHEMA-007, SCHEMA-008, EVAL-001, EVAL-003,
FLOW-001, ERR-001 to ERR-003, ARCH-001, and ARCH-002 pass; the SCHEMA-004 and SDK capability spike results
are recorded; the baseline is recorded.

### Phase 2: Card response formats and deck card view

1. Add the concise projection and `fields` narrowing in `MtgMcp.Scryfall`
   with unit tests.
2. Add `responseFormat` and `fields` to `scryfall_search`,
   `scryfall_card_get`, `scryfall_card_collection`, and
   `scryfall_cards_by_tag`; remove `includeRaw`.
3. Add the size budget test (CARD-003) and the raw equality test (CARD-004).
4. Map Scryfall's `game_changer` field, and add `tags`, `cheapestPriceUsd`,
   `gameChanger`, and `edhrecRank` to the concise projection (FACT-001).
5. Map every-face oracle text, all legalities, `games`, `reserved`,
   `layout`, and all Scryfall price columns; expose them through `fields`
   and `standard` (FACT-002).
6. Add `include: ["cards","tags"]` to `deck_get` using the same projection,
   with entry paging (FLOW-002).
7. Add the result size limit, paging or `truncated` and `narrow` for each
   card-returning tool, and the size tests (SIZE-001).

Checks: `task test`, `task coverage:scryfall`, `task coverage:app`,
`task surface:report`.

Exit: CARD-001 to CARD-005, FACT-001, FACT-002, FLOW-002, and SIZE-001 pass
for the phase 2 tools.

### Phase 3: Default selection, names, merges, and preset removal

Depends on phase 2, because the merged `scryfall_cards_get` carries
`responseFormat`. Does not start until the phase 1 baseline is recorded.

1. Change toolset selection: with no `--toolsets`, select every toolset, and
   turn `archidekt` and `playgroup` off when their credentials are absent,
   even when named. Report `credentials-missing` in the capabilities resource
   and log the setting to add on stderr (SURF-001, SURF-002).
2. Make `remote` the default mode (SURF-007).
3. Remove the nine single-step deck tools; extend `deck_apply_changes` tests to
   cover each change kind (SURF-005).
4. Fold `deck_category_rules_validate` into the preview, and remove the
   `common-v1` preset (FLOW-003).
5. Merge Scryfall and Archidekt tools per the SADD.
6. Apply the final tool names from FIXTURES and start every description with
   its data source (NAME-001, NAME-002).
7. Turn on unknown-property rejection for every tool and add its test
   (SCHEMA-004). Apply it in every place that builds its own schema options:
   `ToolRegistration`, `DeckBatchWriteTools`, and `StatisticsToolsetManifest`,
   not only the default `WithTools` path.
8. Add server instructions built from the active selection (SURF-009).
9. Add untrusted-text notes to the listed provider tools, review result types
   for credential fields (SAFE-001), and add `_meta`
   `anthropic/requiresUserInteraction` to destructive Archidekt remote writes
   if the phase 1 spike allows it (SAFE-002).
10. Add description examples to `deck_apply_changes` and
    `deck_category_rules_preview` (SCHEMA-006).
11. Add the merge rule test (SURF-003), the old-name map test (SURF-004), the
    naming tests, and the frozen Playgroup list check (SURF-008). Update the
    approved surface list and counts in
    `tests/MtgMcp.Architecture.Tests/FoundationArchitectureTests.cs`, which
    hard-codes today's names.

Checks: `task ci`, `task test:integration`.

Exit: SURF-001 to SURF-005, SURF-007 to SURF-009, NAME-001, NAME-002,
SCHEMA-004, SAFE-001, SAFE-002 (or its recorded deferral), and FLOW-003
pass; counts are 30/43/43 with no credentials and 53/67/80 with both.

### Phase 4: Card list tools and deck rule facts

Depends on phase 3, because the new tools use the final card format options
and the new toolset layout.

1. Record two owner decisions in the rewrite umbrella's post-cutover
   registry: Commander rule facts, which reverse the 0.9.0 "no legality
   behavior" rule, and the factual use of Spellbook `/estimate-bracket`,
   which the Commander Spellbook packet excluded.
2. Add the shared list source type and resolver for `scryfall-snapshot`,
   `deck`, `deck-backup` (read-only backup open), `archidekt-deck`,
   `archidekt-snapshot`, `cards`, and nested `compare`, with the owning
   toolset rule and the backup schema check (SET-002, SET-006, SET-008,
   cases C04, C05, C09 to C11, C14 to C16).
3. Add the pure set function (operations and `matchBy`) with unit tests
   (SET-001, cases C01 and C02), then `card_lists_compare` in App with paging,
   unmatched entries, and the small fingerprint-bound cursor (SET-003 to
   SET-005, SET-007, cases C03, C06 to C08, C12, C13).
4. Add the pure filter, group, and metric function in `MtgMcp.Scryfall`, with
   pips and sources (QUERY-001 to QUERY-006, cases Q01 to Q08), then
   `card_list_query` in App with the work limit (cases Q09 and Q10).
5. Add the QUERY-007 fields to the query function (cases Q11 to Q14).
6. Capture Scryfall card data for one card of each pairing row, and check
   the current Comprehensive Rules 903.3 eligibility text; adjust the SADD
   tables to what they show. Then add `rules: "commander"` to
   `deck_validate` with eligibility, singleton limits, pairing, and companion
   facts (RULE-001 to RULE-004, cases R01 to R21).
7. Add `deck_duplicates_preview` (DEDUP-001 to DEDUP-003, cases D01 to D08).
   It depends on ARCH-001 from phase 1 for a clean push preview.
8. Add the exact conditional castability function in `MtgMcp.Statistics`
   with brute-force tests, then `stats_castability` over `deck` and
   `deck-backup` sources, using the existing mana availability engine for
   the unconditional value (CAST-001, CAST-002, cases K01 to K07).
9. Capture one real `/estimate-bracket` response as a sanitized fixture and
   confirm its field names. Then add the Spellbook request, mapping without
   `bracketTag`, and `spellbook_deck_bracket_facts_get` (BRACKET-001, cases
   B01 to B04).
10. Add a typed Playgroup game record, the pure aggregation, and
    `playgroup_games_stats_get` (PG-001, PG-002, cases P01 to P05).
11. Add description examples to `card_lists_compare`, `card_list_query`, and
    `stats_castability` (SCHEMA-006), and extend the size tests to the new
    tools (SIZE-001).
12. Add the new tools to their manifests (`decks`, `scryfall`, `stats`, `spellbook`,
    `playgroup`) and the approved surface list; update counts.
13. Rerun the agent task set, including T11, T18 to T20, T22 to T28, T30
    to T35, and T37, and record results.

Checks: `task test`, `task coverage:app`, `task coverage:scryfall`,
`task coverage:decks`, `task coverage:statistics`, `task coverage:spellbook`,
`task coverage:playgroup`, `task surface:report`, `task test:integration`.

Exit: SET-001 to SET-008, QUERY-001 to QUERY-007, RULE-001 to RULE-004,
DEDUP-001 to DEDUP-003, CAST-001, CAST-002, BRACKET-001, PG-001, and PG-002 pass; SCHEMA-006 and
SIZE-001 pass for the phase 4 tools; counts are 35/48/48 with no credentials
and 59/73/86 with both.

### Phase 5: Documents and release 0.11.0

1. Update README, `docs/toolsets.md`, `docs/rewrite-guide.md` counts, and
   CHANGELOG with the old-to-new table from FIXTURES and the inline rule set
   that replaces `common-v1`. Add examples for "search results not in my
   deck" (`card_lists_compare`), "mana curve and pips" (`card_list_query`),
   and Commander rule facts (`deck_validate`).
2. Add the `audit-deck`, `build-from-commander`, and `compare-decks` prompts
   and their tests, if the phase 1 spike allows it (PROMPT-001). Document
   them in README.
3. Follow the repository release steps for `0.11.0`.

Checks: `task ci`, `task pack VERSION=0.11.0`,
`task release:tool-smoke VERSION=0.11.0`.

Exit: DOC-001 and PROMPT-001 (or its recorded deferral) pass and the release
is published.

### Phase 6: Caller-ruled simulation and release 0.12.0

Approved by the owner on 2026-10-25 (README, Owner Decisions, item 3).

1. Record the owner's caller-ruled sampling decision in the rewrite
   umbrella's post-cutover registry. (`configurable-decision-models`,
   `conservative-goldfish-v2`, `simulation-profile-evidence`, and
   `stats-lab-interaction-readiness` were already closed as superseded and
   moved to `completed/` on 2026-10-27.)
2. Optionally rename `MtgMcp.OnCurve` to `MtgMcp.Simulation` with no behavior
   change; run all tests.
3. Capture replay fixtures from today's `deck_on_curve_estimate` before
   changing it.
4. Add rule and condition records, the engine rules in the SADD, and the
   evaluator, including the `draw-cards` rule kind (SIM-007), the free first
   mulligan (SIM-008), `sampleHands` (SIM-009), and group `produces` with the
   `can-cast` condition on the existing payment matcher (SIM-010).
5. Add `stats_simulate` in App with deck and group resolution, including the
   overlapping-group check.
6. Add the `on-curve` preset; remove `deck_on_curve_estimate`; prove SIM-004.
7. Add the work limit (SIM-005), each engine-rule test (SIM-006), and the
   required-field tests (SIM-001), covering cases S01 to S26.
8. Add description examples to `stats_simulate` (SCHEMA-006).
9. Update docs, rerun the task set including T16, T17, T21, T29, and T36,
   and release `0.12.0`.
10. Fix the defects found after the phase 6 audit, each in its own commit,
    with CHANGELOG entries under 0.12.0 Fixed: FIX-001 (initialize handshake),
    FIX-002 (case-distinct categories, schema 2), FIX-003 (push mismatch record,
    zone refusal, `discardLocalChanges`, entry order ignored), FIX-004 (row-order
    independent local fingerprint), FIX-005 (converged edits), FIX-006 (per-deck
    local IDs), and FIX-007 (whole numbers as doubles).

Checks: `task ci`, `task coverage:oncurve` (or its renamed task),
`task benchmark:oncurve`, CI on Windows and Linux for SIM-002.

Exit: SIM-001 to SIM-010, FIX-001 to FIX-007, SCHEMA-006 for `stats_simulate`, and SURF-006
pass.

## Release Plan

- `0.11.0`: phases 1 to 5, including `card_lists_compare`,
  `card_list_query`, card facts, Commander rule facts with pairing and
  companion, `stats_castability`, `spellbook_deck_bracket_facts_get`,
  `playgroup_games_stats_get`, `deck_duplicates_preview`, server
  instructions, and prompts. Bug fixes: `kind` in any position (SCHEMA-007,
  ERR-003), order-insensitive Archidekt category diffs (ARCH-001), and
  provider IDs saved after push (ARCH-002); by
  owner decision on 2026-10-27 they ship with the full plan, not as a
  separate patch. Behavior change: failed results set `isError: true`.
  Breaking changes:
  removed and merged tool names,
  renamed tools (`local_deck_*` and others), `remote` as the default mode,
  provider toolsets turned off without credentials, `includeRaw`
  replaced by `responseFormat: "full"`, rejection of unknown request fields,
  and removal of the `common-v1` preset.
- `0.12.0`: phase 6. Breaking change: `deck_on_curve_estimate` removed.

## Reviews

At the end of every phase, run a full code audit (the repository's combined
audit) of that phase's changes before the phase is reported done, and fix
supported findings in scope; this is an owner instruction from 2026-10-27.
After phases 4 and 6, also run focused read-only reviews on the same revision, at
most three reviewers at a time: correctness, test quality, and plain language.
Fix every supported finding in scope, including low-severity ones. Allow at
most three review passes in total. Re-review only after a confirmed serious
finding or a large change, and only with the affected reviewers. If findings
remain at the limit, report them instead of claiming a pass.

## Finish

The packet is complete when every requirement in the SRD passes its check,
both releases are published, the evidence table is filled in, and the packet
moves to `completed/`. Optional follow-ups recorded as deferred do not reopen
it.
