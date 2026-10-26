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

1. Spike: convert `scryfall_search.freshnessPolicy` to an enum, and try
   rejecting an unknown property on the same tool. Capture the generated
   schema and behavior and record both in the README evidence table. If
   either fails, use the `TransformSchemaNode` fallback in the SADD. Do not
   turn on unknown-property rejection yet; it is breaking and ships in
   phase 3.
2. Convert the remaining choice parameters.
3. Add the SCHEMA-001 test (every choice parameter has `enum`).
4. Rewrite `stats_*` descriptions with fields and examples; add the example
   fixtures (SCHEMA-003).
5. Rewrite the `scryfall_search`, `scryfall_tag_search`, and
   `scryfall_cards_by_tag` descriptions for the tag path (FLOW-001).
6. Add the current revision or remote state to every conflict result
   (ERR-001).
7. Add the `tools/list` order smoke test (SCHEMA-005).
8. Write the agent task set fixtures (EVAL-001), then run the task set
   against `0.10.0` and record the baseline (EVAL-002). Tasks T18 to T20 and T22 to T28 are skipped until phase 4,
   and T16, T17, T21, and T29 until phase 6.

Checks: `task build`, `task test`, `task surface:report`, `task smoke:mcp`.

Exit: SCHEMA-001 to SCHEMA-003, SCHEMA-005, EVAL-001, FLOW-001, and ERR-001
pass; the SCHEMA-004 spike result is recorded; the baseline is recorded.

### Phase 2: Card response formats and deck card view

1. Add the concise projection and `fields` narrowing in `MtgMcp.Scryfall`
   with unit tests.
2. Add `responseFormat` and `fields` to `scryfall_search`,
   `scryfall_card_get`, `scryfall_card_collection`, and
   `scryfall_cards_by_tag`; remove `includeRaw`.
3. Add the size budget test (CARD-003) and the raw equality test (CARD-004).
4. Map Scryfall's `game_changer` field, and add `tags`, `cheapestPriceUsd`,
   `gameChanger`, and `edhrecRank` to the concise projection (FACT-001).
5. Add `include: ["cards","tags"]` to `deck_get` using the same projection
   (FLOW-002).

Checks: `task test`, `task coverage:scryfall`, `task coverage:app`,
`task surface:report`.

Exit: CARD-001 to CARD-005, FACT-001, and FLOW-002 pass.

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
   (SCHEMA-004).
8. Add the merge rule test (SURF-003), the old-name map test (SURF-004), the
   naming tests, and the frozen Playgroup list check (SURF-008). Update the
   approved surface list and counts in
   `tests/MtgMcp.Architecture.Tests/FoundationArchitectureTests.cs`, which
   hard-codes today's names.

Checks: `task ci`, `task test:integration`.

Exit: SURF-001 to SURF-005, SURF-007, SURF-008, NAME-001, NAME-002,
SCHEMA-004, and FLOW-003 pass; counts are 30/43/43 with no credentials and 53/67/80 with
both.

### Phase 4: Card list tools and deck rule facts

Depends on phase 3, because the new tools use the final card format options
and the new toolset layout.

1. Record the owner's decision to add Commander rule facts in the rewrite
   umbrella's post-cutover registry, since it reverses the 0.9.0 "no legality
   behavior" rule.
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
5. Add `rules: "commander"` to `deck_validate` (RULE-001, RULE-002, cases R01
   to R09).
6. Add both tools to the `scryfall` manifest and the approved surface list;
   update counts.
7. Rerun the agent task set, including T11 and T18 to T20 and T22 to T28,
   and record results.

Checks: `task test`, `task coverage:app`, `task coverage:scryfall`,
`task coverage:decks`, `task surface:report`, `task test:integration`.

Exit: SET-001 to SET-008, QUERY-001 to QUERY-006, RULE-001, and RULE-002
pass; counts are 32/45/45 with no credentials and 55/69/82 with both.

### Phase 5: Documents and release 0.11.0

1. Update README, `docs/toolsets.md`, `docs/rewrite-guide.md` counts, and
   CHANGELOG with the old-to-new table from FIXTURES and the inline rule set
   that replaces `common-v1`. Add examples for "search results not in my
   deck" (`card_lists_compare`), "mana curve and pips" (`card_list_query`),
   and Commander rule facts (`deck_validate`).
2. Follow the repository release steps for `0.11.0`.

Checks: `task ci`, `task pack VERSION=0.11.0`,
`task release:tool-smoke VERSION=0.11.0`.

Exit: DOC-001 passes and the release is published.

### Phase 6: Caller-ruled simulation and release 0.12.0

Approved by the owner on 2026-10-25 (README, Owner Decisions, item 3).

1. Record the owner's caller-ruled sampling decision in the rewrite
   umbrella's post-cutover registry, and mark
   `configurable-decision-models`, `conservative-goldfish-v2`,
   `simulation-profile-evidence`, and `stats-lab-interaction-readiness` as
   superseded in their packets and in the planned index.
2. Optionally rename `MtgMcp.OnCurve` to `MtgMcp.Simulation` with no behavior
   change; run all tests.
3. Capture replay fixtures from today's `deck_on_curve_estimate` before
   changing it.
4. Add rule and condition records, the engine rules in the SADD, and the
   evaluator, including the `draw-cards` rule kind (SIM-007), the free first
   mulligan (SIM-008), and `sampleHands` (SIM-009).
5. Add `stats_simulate` in App with deck and group resolution, including the
   overlapping-group check.
6. Add the `on-curve` preset; remove `deck_on_curve_estimate`; prove SIM-004.
7. Add the work limit (SIM-005), each engine-rule test (SIM-006), and the
   required-field tests (SIM-001), covering cases S01 to S19.
8. Update docs, rerun the task set including T16, T17, T21, and T29, and
   release `0.12.0`.

Checks: `task ci`, `task coverage:oncurve` (or its renamed task),
`task benchmark:oncurve`, CI on Windows and Linux for SIM-002.

Exit: SIM-001 to SIM-009 and SURF-006 pass.

## Release Plan

- `0.11.0`: phases 1 to 5, including `card_lists_compare`,
  `card_list_query`, card facts, and Commander rule facts. Breaking changes:
  removed and merged tool names,
  renamed tools (`local_deck_*` and others), `remote` as the default mode,
  provider toolsets turned off without credentials, `includeRaw`
  replaced by `responseFormat: "full"`, rejection of unknown request fields,
  and removal of the `common-v1` preset.
- `0.12.0`: phase 6. Breaking change: `deck_on_curve_estimate` removed.

## Reviews

After phases 4 and 6, run focused read-only reviews on the same revision, at
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
