# Local Deck Agent Safety Plan

## Current Work

- Status: In progress
- Owner: mtg-mcp (Opus parent session with Sonnet implementation children)
- Last updated: 2026-10-31, `main` at `3485497`
- Implementation authorized: Yes (owner's standing instruction, 2026-10-31: other
  sessions send mtg-mcp bugs; research each, fix it, and release it in the next
  version)
- Selected work and endpoint: phases 1 to 3 and the upgrade note, merged into
  `main` on this host, audited, released as `0.13.2`, and installed.
- State: phases 1 to 3 implemented and merged into `ldas/integ`; docs done; full audit next.
- Next action and blocker: full audit, then merge to `main`, release `0.13.2`, and install. No blocker.
- Resume check: `git worktree list` shows the checkouts below.

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| `mtg-mcp-wt/local-deck-agent-safety/integ` | `ldas/integ` | parent | integration |
| `mtg-mcp-wt/local-deck-agent-safety/p1-adds` | `ldas/p1-adds` | Sonnet child | phase 1 |
| `mtg-mcp-wt/local-deck-agent-safety/p2-duplicates` | `ldas/p2-duplicates` | Sonnet child | phase 2 |
| `mtg-mcp-wt/local-deck-agent-safety/p3-read` | `ldas/p3-read` | Sonnet child | phase 3 |

## Outcome And Requirements

A session cleaning up Archidekt deck 27968076 on `0.13.1` (2026-10-31) reported
three gaps that let an agent damage a deck or work blind:

1. `local_deck_apply_changes` adds a second entry for a card already in the deck
   with `issueCount: 0`. A retry loop turned 17 cards into 4 copies each.
2. `local_deck_duplicates_preview` returns copies that are identical in every
   field as `ambiguous` and plans no change, while marking one entry `kept`; the
   caller built 51 removals by hand. Its `next` hint (add a `keepPriority` group)
   cannot separate identical entries.
3. Checking whether a card is in a deck takes paging through `local_deck_get`
   (a 172-entry deck was cut at 79 entries).

The same session noted that a Claude Code session started before an upgrade
keeps running the old server until mtg-mcp restarts.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-001 | An `add-entry` whose card matches an entry that existed before the batch, or an earlier add in the same batch, is reported as an issue of kind `already-in-deck` with the existing entry's ID and card name: "'<name>' was already in the deck as entry <id> (zone <zone>, <qty> copies, categories: <list>). This add made another entry; to change copies use update-entry on <id>, or remove-entry on the new entry." Matching uses Oracle ID when both sides have one, else `CardNames.Match` on names; never printings. Basic lands (`BasicLandNames`) are exempt. The add still happens. | Tests with and without installed card data; basics give no issue; a duplicate inside one batch is reported; `issueCount` counts it. |
| REQ-002 | `local_deck_apply_changes` takes `whenCardPresent` (`add` default, or `skip`). With `skip`, an add whose card is already in the same zone is not made and is reported as `skipped`: "'<name>' is already in zone <zone> as entry <id>; not added." A later change in the batch that refers to a skipped add's entry ID refuses the batch with `invalid-deck-change` naming both changes. | `skip` twice is idempotent; a different zone is added; a reference to a skipped add is refused with no write. |
| REQ-003 | `local_deck_duplicates_preview`: when the entries tied at the cut are identical (printing key: printing ID, else set and collector number, else normalized name; language; finish; zone; category set, ignoring case and order), it keeps the first in deck order and plans the removals, marking the group `tieBrokenBy: "identical-entries"`. Groups that stay `ambiguous` mark no tied entry as kept, and `next` names the field that differs. | The reported case (17 cards x 4 identical sideboard copies) gives 17 groups and 51 `remove-entry` changes, keeping the first by deck order; ties that differ in finish, language, or categories stay ambiguous; a size cut keeps each group's changes together. |
| REQ-004 | `local_deck_get` takes `cardNames` (1 to 100 names, matched with `CardNames.Match`) returning only matching entries plus `cardNamesNotFound`, with deck-wide totals unchanged; and `responseFormat` (`concise`, `full`; default `full`) where concise entries are `entryId`, `cardName`, `quantity`, `zone`, `categoryNames`. Both are part of the cursor fingerprint. | Name hits and misses; a double-faced card matched by one face; a cursor refuses a changed filter; a 172-entry deck returns in one concise page under the size limit. |
| REQ-005 | Tool descriptions say to pass an `entryId` or use `whenCardPresent: "skip"` for retried adds, and to use `cardNames` to check membership; README "Start the server" and the CHANGELOG say an MCP client keeps the server process it started, so restart or reconnect mtg-mcp after an upgrade. | Description tests (4,096-character limit) and doc review. |

Non-goals: refusing duplicate adds outright (blocks deliberate extra entries);
format legality on writes (singleton and "any number" rules stay in
`local_deck_validate`); a `zones` filter on `local_deck_get`.

## Current System And Design

Inspected at `3485497` (research 2026-10-31, read-only):

- Add-time issues come only from identity lookup (`DeckBatchWriteTools.cs:129-143`;
  kinds in `DeckWriteResult.cs:21-31`); the resolver never reads the existing
  deck (`DeckEntryResolver.cs:67-102`). Existing retry guards: a stale
  `expectedRevision` is refused (`DeckBatchWriteTools.cs:126-128`), and a reused
  caller `entryId` fails on the primary key.
- Duplicates rank only by `keepPriority` zone and category (`DeckDuplicates.cs:303-322`);
  `IsTiedAtTheCut` (`:278-297`) makes any split tie ambiguous while `Kept` is
  still set (`:251`). Deck order is already deterministic (`DeckSql.cs:635-636`).
- `local_deck_get` returns the whole deck only within the size limit
  (`DeckCardViewCoordinator.cs:152-177`); each entry carries its category
  assignments (about 500 bytes). `archidekt_deck_get` already has a concise
  format to copy (`ArchidektReadTools.cs:84-107`).

Decisions (parent, under the standing instruction): warn by default and skip
only on request, because an outright refusal would block deliberate extra
entries; same-zone matching for `skip` so a sideboard copy of a main-deck card
stays possible; `responseFormat` defaults to `full` so existing callers see no
change. Version `0.13.2`: all changes are additive or fix a broken preview
contract; the new `already-in-deck` issue kind is a new status value and is
named in the CHANGELOG.

## Steps And Verification

1. Phase 1 (REQ-001, REQ-002): `DeckBatchWriteTools`, `DeckWriteResult`, the
   tool description, tests.
2. Phase 2 (REQ-003): `DeckDuplicates`, `DeckDuplicatesCoordinator`, tests.
3. Phase 3 (REQ-004): `DeckReadTools`, `DeckCardViewCoordinator`, view models,
   tests, the budget test fixture.
4. Docs (REQ-005), full audit, merge to `main`, release `0.13.2`, install.

Each phase: focused tests, then `task ci`, `task test:integration`, and
`task test:e2e` from PowerShell.

## Risks And Open Questions

- An agent may still ignore a warning; `whenCardPresent: "skip"` and the
  description are the stronger guard for retries.
- Name matching without card data can flag two different cards that share a
  face name only through `CardNames.Match`'s face rule; acceptable for a warning.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 | implemented; checks on the merged commit pending | `5e573a6`: `ApplyChanges_CardAlreadyInDeck_NoCardData_AddsAndReportsAlreadyInDeck`, `ApplyChanges_CardAlreadyInDeck_WithCardData_MatchesByOracleId`, `ApplyChanges_BasicLandOrOtherCard_GivesNoAlreadyInDeckIssue`, `ApplyChanges_CardAddedTwiceInOneBatch_ReportsTheSecondAdd`. |
| REQ-002 | implemented; checks on the merged commit pending | `5e573a6`: `ApplyChanges_SkipTwice_IsIdempotent`, `ApplyChanges_SkipWithCardInAnotherZone_AddsAndReportsAlreadyInDeck`, `ApplyChanges_ChangeReferringToASkippedAdd_IsRefusedWithoutAWrite`, `ApplyChanges_UnknownWhenCardPresent_IsRefused`. |
| REQ-003 | implemented; checks on the merged commit pending | `747a77d`: `Preview_IdenticalCopies_KeepTheFirstInDeckOrder`, `Preview_SeventeenCardsWithFourIdenticalCopies_PlansFiftyOneRemovals`, `Preview_TwoCopiesThatDiffer_AreAmbiguousWithNoKeptEntry`, `Preview_TwoIdenticalCopiesAndOneDifferent_StayAmbiguous`, `Preview_ManyIdenticalCopyGroups_AreCutWithEachGroupsRemovalsTogether`, `Preview_GroupSettledByTheKeepOrder_OmitsTieBrokenBy`. |
| REQ-004 | implemented; checks on the merged commit pending | `bc70bec`: `Get_CardNames_ReturnsMatchesAndTheNamesNotFound`, `Get_CardNames_AllMissOrInvalid_AreHandled`, `Get_Cursor_RefusesAChangedFilterOrFormat`, `Get_Concise_ReturnsOnlyTheShortEntryFields`, `Get_Concise_LargeSideboardDeckFitsInOneResult`. |
| REQ-005 | implemented; checks on the merged commit pending | Descriptions in `5e573a6` and `bc70bec` (checked by the 4,096-character architecture test); README, CHANGELOG, and `docs/output-control.md` in the docs commit. |
