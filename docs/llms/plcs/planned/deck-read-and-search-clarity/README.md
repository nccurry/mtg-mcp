# Deck Read And Search Clarity Plan

## Current Work

- Status: Planned
- Owner: mtg-mcp (owner approval needed before implementation)
- Last updated: 2026-10-31, `main` at `d8b845c`
- Selected work and endpoint: phases 1 to 3 below, merged into `main` and
  released as the next minor version after owner approval. Decisions D-1 to
  D-4 are open.
- State: designed; not approved; implementation not started.
- Next action and blocker: owner answers D-1 to D-4 and approves; then
  implementation starts with phase 1.
- Resume check: this file is under `docs/llms/plcs/planned/` and no
  `deck-read-and-search-clarity` worktree exists.

## Outcome And Requirements

An agent using `0.13.0` (2026-10-31) reported four problems while building a
Commander deck from Archidekt deck 22959335 (417 entries: 101 main, 2
commander, 21 sideboard, 347 maybeboard):

1. `archidekt_deck_get` holds about 75 entries per page, sorted by name across
   every zone, so the real deck takes about 6 pages, mostly maybeboard. Each
   entry carries fields that rarely matter for deckbuilding.
2. The deck reads as 103 main plus commander cards with no warning.
3. `scryfall_search` with `pageSize: 100` returned 87 cards with
   `truncated: true` and gave no numbers to plan with.
4. There is no commander popularity rank, so "commanders not in the top 100"
   cannot be answered.

Research (2026-10-31) found that problem 2 is mostly a zone-mapping gap, not
duplicates. Archidekt counts an entry toward the deck only when its first
category has `includedInDeck`. `MapZone`
(`src/MtgMcp.Archidekt/ArchidektContractMapper.cs:449-472`) ignores that flag.
In deck 22959335, three main entries have an excluded first category
(Bloodthirsty Blade under Multiplayer, Revival // Revenge and Osteomancer
Adept under Reanimate). Counted Archidekt's way, the deck has exactly 100
cards. One Bloodthirsty Blade is also a real second entry, under Removal.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-001 | Each `archidekt_deck_get` entry reports `inDeck`: whether its first category counts toward the deck on Archidekt (true when it has no category or the flag is unknown). | Fake Archidekt test copying deck 22959335: the Multiplayer Bloodthirsty Blade has `inDeck: false`; others true. |
| REQ-002 | The `archidekt_deck_get` summary reports, for the whole deck whatever the filter: `inDeckCardCount` (Archidekt's count); main and commander entries that are not in the deck (count and up to 20 names); duplicate groups across commander and main by Oracle ID, falling back to name, skipping basic lands, capped with an omitted count; and for Commander decks a size fact `{format, expected: 100, mainAndCommander, inDeck}`. Facts only, no verdict. | Same fixture: `inDeckCardCount` 100, `mainAndCommander` 103 (101 + 2), three not-in-deck entries, one duplicate group, basics not grouped, cap sets the omitted count. No card data needed. |
| REQ-003 | `archidekt_deck_get` takes an optional `zones` filter (`commander`, `main`, `sideboard`, `maybeboard`, the names `card_list_query` already uses) and returns `shownEntryCount`. Offsets count the filtered list. | A 417-entry fixture: `zones: ["commander","main"]` returns only those; a bad offset is refused with `invalid-offset` naming the filtered count; an unknown zone is refused by the schema enum. |
| REQ-004 | `archidekt_deck_get` takes `responseFormat` (`concise`, `standard`, `full`, the shared attribute). `concise` entries hold `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`, `collectorNumber`, `oracleId`, `inDeck`. `standard` keeps the `0.13.0` entry; `full` equals `standard` (provider extras already come back as `extensions`). The default follows D-2. | Main plus commander of deck 22959335 (103 entries) fits one page in `concise`; `standard` matches `0.13.0` field for field. |
| REQ-005 | Entries are ordered by zone (commander, main, sideboard, maybeboard), then name, then relation ID, in every format. | Ordering is stable across the fake's per-read reordering; paging reaches every entry once. |
| REQ-006 | `archidekt_snapshots_get` (kind `get`) takes the same `zones` and `responseFormat`, since it shares the deck pager. | Snapshot read test with both options. |
| REQ-007 | `scryfall_search`, `scryfall_cards_get`, and `scryfall_installed_cards_by_tag` report `returnedCount` and `requestedCount` on every page, and `cutReason: "result-size-limit"` when cut. `narrow` gives the numbers: kept and requested counts, average bytes per card, where `nextCursor` continues, and that leaving `oracleText` out of `fields` fits more. `card_lists_compare` uses the same hint builder. | Recorded-fixture tests: a cut concise page reports 87 of 100 for the reporter's field set; an uncut page has no `cutReason`; cursors visit every card once; concise without `oracleText` fills 100. |
| REQ-008 | Descriptions say `pageSize` is a maximum, that `oracleText` is usually the largest field, and that `edhrecRank` counts deck inclusion in any slot and is not a rank of how often a card is played as commander (same for `order: "edhrec"`). | Description tests and the 4,096-character limit pass. |

Non-goals: a commander popularity rank or an EDHREC adapter (see Decisions);
changing how pulls map zones (D-1); an `oracleTextMaxLength` option (D-3).

## Current System And Design

Inspected at `d8b845c`.

- `archidekt_deck_get` takes `deckId` and `offset`
  (`src/MtgMcp.App/Archidekt/ArchidektReadTools.cs:73-83`). `PageDeck`
  (`ArchidektResultPaging.cs:23-96`) sorts by name and pages with
  `ResultPage.Take`; it is shared with `archidekt_snapshots_get` (`:161-172`).
  Entries are the 14-field `RemoteDeckEntry` (`ArchidektModels.cs:55-69`).
- `zones` already exists on card-list sources (`CardListSourceInput.cs:47-83`).
  Every `responseFormat` parameter must use the shared attribute
  (`ToolAllowedValuesTests.cs:35`), and choice parameters publish an enum.
- `RemoteDeckEntry.OracleId` comes from Archidekt's `oracleCard.uid`, so
  duplicate grouping needs no Scryfall data. The Commander size fact matches
  `CommanderRuleFacts` (`CommanderRuleFacts.cs:145-167`).
- Scryfall pages are cut to 40,000 bytes by `ScryfallCardFormatter.CountAsync`
  (`ScryfallCardFormatter.cs:138-166`); the next cursor resumes exactly after
  the last kept card, from a stored snapshot, without fetching again. The
  `narrow` text is fixed (`ScryfallCardResponses.cs:16-18`). Measured on the
  reporter's query: 436 bytes per card with oracle text (87 fit), 208 without
  (100 fit). Leaving `oracleText` out of `fields` already works.

Decisions made by research (2026-10-31), for owner confirmation:

- **No commander rank.** Scryfall's `edhrec_rank` counts inclusion in any
  slot: EDHREC's top commander, Y'shtola, Night's Blessed, has rank 2,495, so
  sorting `is:commander` by it answers "top 100 commanders" wrongly. EDHREC has
  the right data, but its terms forbid automated requests, and
  `completed/evidence-first-deckbuilding-evolution/SOURCE_FEASIBILITY.md:44-56`
  already rejected its undocumented JSON routes. REQ-008 corrects the wording
  so agents say the server cannot answer instead of answering wrongly.
- **Report counts before shortening text.** Shortened oracle text hands the
  agent partial rules text, and a 200-character cap fits only 92 of 100.

## Steps And Verification

1. Phase 1, deck facts (REQ-001, REQ-002): `inDeck` on entries, the summary
   facts, a shared basic-land list. Archidekt adapter and App paging only.
2. Phase 2, deck reading (REQ-003 to REQ-006): `zones`, `responseFormat`,
   zone ordering, the concise entry record, snapshot pass-through, `narrow`
   naming the new options.
3. Phase 3, Scryfall pages and wording (REQ-007, REQ-008): counts, cut reason,
   numeric `narrow`, description fixes.

Each phase: focused tests with the offline fake Archidekt and recorded
Scryfall fixtures, then `task ci`, `task test:integration`, and `task test:e2e`
from PowerShell; `ToolResultBudgetTests` stays under the ceiling; schema and
surface tests updated; README, CHANGELOG, `docs/output-control.md` updated.
Phases are independent and may run in parallel worktrees; phase 2 touches the
same pager as phase 1, so merge phase 1 first.

## Risks And Open Questions

- **D-1 (owner, blocks nothing here):** fix `MapZone` so entries whose first
  category is excluded do not map to `main`. This is the root cause, but it
  changes pull results, content fingerprints, and local zones, and a "not in
  deck" zone has no local equivalent. Proposed: a separate plan. This plan
  only reports the fact.
- **D-2 (owner, blocks REQ-004's default):** make `concise` the default for
  `archidekt_deck_get`. Changing a default result is breaking under
  `docs/versioning.md:72-75`. Proposed: opt-in now, with a `Deprecated:` note
  that the default becomes `concise` in the following minor.
- **D-3 (owner, non-blocking):** add `oracleTextMaxLength` later only if agents
  still struggle once REQ-007 ships.
- **D-4 (owner, non-blocking):** the size fact covers Commander only
  (proposed); Oathbreaker and Brawl only if wanted.
- After Scryfall's 24-hour snapshot lifetime, an old cursor fails as
  `invalid-cursor` (inferred from `ScryfallService.cs:1588`); REQ-007's
  `narrow` should not promise more.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 to REQ-008 | planned | Research reports of 2026-10-31: one read-only GET of Archidekt deck 22959335, one read-only Scryfall search, four Scryfall rank checks, and EDHREC terms. |
