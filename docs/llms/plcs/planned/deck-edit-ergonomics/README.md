# Deck Edit Ergonomics Plan

## Current Work

- Status: Planned
- Owner: mtg-mcp (owner session)
- Last updated: 2026-11-01, branch `docs/deck-edit-ergonomics-plc` from `main` at `c4fa0f1` (release `0.13.3`)
- Implementation authorized: No. The owner asked for this plan; implementation waits for the owner's go-ahead.
- Selected work and endpoint: phases 0 to 4, merged into `main`, audited, released in the next version, and
  installed.
- State: drafted and reviewed once (independent read-only review on 2026-11-01; its findings are folded in).
- Next action: the owner answers decisions D-2 and D-3, then the plan moves to `in-progress/` and phase 0 starts.
- Blocker: owner confirmation of D-2 (a new primary category replaces the old one) and D-3 (`remove-entry` takes a
  list of IDs).
- Resume check: this file exists on `main` or on `docs/deck-edit-ergonomics-plc`.

## Problem

A session editing a 347-entry Archidekt deck on `0.13.1` (2026-11-01) moved 13 maybeboard cards to the sideboard,
added 5 new sideboard cards, and pushed to Archidekt. It worked only because the session drove the server through a
local PowerShell JSON-RPC script that filtered results and generated the changes. Calling the tools directly would
have flooded the context or meant writing 49 changes by hand. This plan makes that script unnecessary.

The reported instances are symptoms of five general problems. The plan fixes each problem across every tool it
affects, not only the instance that was reported.

| Shape | Reported instance | Where else the shape shows up |
| --- | --- | --- |
| S1. Finding entries means paging through the whole deck. | 4 pages of 100 entries to find 13 named cards. | Any edit that targets a zone or category: "every maybeboard card", "everything in Ramp". `cardNames` (shipped in `0.13.2`) covers names only. |
| S2. Changing entries means restating each one, one fact per change. | A zone move took 3 changes: `update-entry` with all 11 stored fields, `assign-category`, and `unassign-category`. | Changing quantity, printing, or finish also copies all 11 fields; recategorizing takes one change per category per entry; removing N entries takes N changes. |
| S3. Creating something takes a second change to finish it. | `add-entry` cannot set categories, so a sideboard add needs a caller-chosen `entryId` and then `assign-category`. | Every add into a category, including the Archidekt zone categories Sideboard, Maybeboard, and Commander, and the same draft in `local_deck_create` and imports. |
| S4. Previews show whole objects, not what changes. | `archidekt_push_preview` for 18 small edits returned about 25 KB, giving the baseline, local, and remote JSON of every changed entry. | `archidekt_sync_diff` and `archidekt_pull_preview` use the same difference rows. |
| S5. A refused apply or preview does not say how to fix it. | `discardLocalChanges` must be set on both `archidekt_pull_preview` and `archidekt_pull_apply`; a mismatch gives a generic `pull-preview-changed`. | A zone that disagrees with its categories refuses the push and names only the card, not the entry or the change that fixes it. |

The session also could not load mtg-mcp tools through ToolSearch at first. That is a client harness problem, not
part of this plan.

## Requirements

| ID | Shape | Required behavior | Acceptance criteria |
| --- | --- | --- | --- |
| REQ-001 | S1 | `local_deck_get` accepts `zones` (local zone codes; an unknown code is refused as invalid input naming the allowed codes) and `categoryNames` (trimmed and case-insensitive, matching primary and non-primary assignments) beside `cardNames`. Values within one filter combine with OR; filters combine with AND. Only matching entries are returned, with deck-wide totals unchanged. No match is a normal result with no entries, not an error. Each filter value that matches nothing is listed in `zonesNotFound` or `categoryNamesNotFound`, as `cardNamesNotFound` already is. The new filters join the cursor fingerprint, which already covers `cardNames` and `responseFormat`. `include` (cards, tags) applies to the filtered entries only. | Tests: zone filter; category filter on primary and non-primary assignments; both combined with `cardNames`; an unknown zone is refused; a category with no match is listed; a cursor refuses a changed filter; `include: ["cards"]` with a filter returns card data only for matches. On a 347-entry deck fixture, a concise call with `categoryNames: ["Maybeboard"]` returns its 13 entries in one page. |
| REQ-002 | S2 | `local_deck_apply_changes` gets a `patch-entries` change kind. It takes `entryIds` (1 to 100, no duplicates) plus only the fields that change: `quantity`, `zone`, `sortOrder`, `finish`, `language`, a `printing` group (`printingId`, or `setCode` and `collectorNumber`, resolved against the entry's card as `add-entry` resolves them), and category edits `primaryCategoryId`, `addCategoryIds`, and `removeCategoryIds`. A field left out stays unchanged. The same patch applies to every listed entry, including entries added earlier in the same batch. A category already assigned as non-primary becomes primary when named in `primaryCategoryId`. A new primary category removes the previous primary's assignment unless that category is in `addCategoryIds` (D-2). These are refused with `invalid-deck-change` and no write: a patch with no fields; the same category ID in two of the category fields; a duplicate or unknown entry ID; an unknown category ID. Card identity (`cardName`, `oracleId`) cannot be patched. | Tests: one change moves 13 entries from Maybeboard to Sideboard (zone, plus primary category Sideboard replacing Maybeboard); a quantity-only patch; a printing patch is resolved and reports identity issues as `add-entry` does, also for an entry added earlier in the batch; each refusal above, with no write; `addCategoryIds` keeps the old primary as non-primary. |
| REQ-003 | S2 | `remove-entry` accepts `entryIds` (1 to 100) in place of `entryId`; exactly one of the two must be given (D-3). | Tests: one change removes 13 entries; both or neither field is refused; an unknown ID refuses the batch with no write. |
| REQ-004 | S3 | `DeckEntryDraft` accepts `categoryIds` and `primaryCategoryId`. An `add-entry` may name a category added earlier in the same batch by its caller-chosen `categoryId`. `whenCardPresent: "skip"` skips the categories together with the entry. Wherever the draft is accepted (`add-entry`, `local_deck_create`, imports), the fields are honored or refused with a message; none silently drops them. The Archidekt pull mapper leaves them unset. | Tests: an add into Sideboard is one change; a category added in the same batch is assigned; an unknown category refuses the batch; a skipped add assigns nothing; `DeckContractValidator.Normalize` keeps the fields; `local_deck_create` with drafted categories. |
| REQ-005 | S4 | `archidekt_sync_diff`, `archidekt_pull_preview`, and `archidekt_push_preview` accept `responseFormat` (`full` by default, or `concise`). A concise difference row is `{ path, state, fields: [{ name, baseline, local, remote }] }` and lists only the fields whose values differ. For an added or removed entry, it lists the fields of the side that exists. Fingerprints, guards, summaries, operations, and apply availability are the same in both formats. Paging measures the size of the format returned, so concise pages hold more rows; `nextDifferenceOffset` keeps its meaning. `archidekt_snapshot_restore_preview` is excluded: its single difference row holds two content fingerprints, not entry JSON. | Tests on an 18-edit push built with fake HTTP (no recorded push fixture exists; phase 3 builds one): the concise result is at most one third the size of the full result, with the same `previewFingerprint` and `applyAvailable`. Field-level tests cover an added entry, a removed entry, a zone and category change, and a conflict. |
| REQ-006 | S5 | When `archidekt_pull_apply` gets a preview fingerprint that does not match, and the preview with the other `discardLocalChanges` value does match, the refusal says which value the preview used and to pass the same value. Each preview result's apply notice names the options its apply call must repeat. `confirmRemovals` already has a named refusal (`push-removals-unconfirmed`); it gets a regression test. | Tests: a pull apply without the `discardLocalChanges` its preview used is refused with a message naming the option and value; a pull and a push preview notice each name their options; the existing `confirmRemovals` refusal keeps naming the option. |
| REQ-007 | S5 | When push preview refuses entries whose zone disagrees with their categories, the notice names each local entry ID and both fixes, with the server choosing neither: a `patch-entries` change setting the zone, or one setting `primaryCategoryId`, preceded by an `add-category` when that zone category does not exist locally. `ArchidektZoneContradiction` gains the local entry ID. | A test with a zone-only edit shows both fixes; applying either makes the push available, including when the deck has no Sideboard category yet. |
| REQ-008 | all | Tool descriptions point to the new options: `update-entry`, `assign-category`, and `unassign-category` point to `patch-entries`, and `local_deck_get` names its filters. The reference (`docs/reference.md`, or the README if the redesign has not merged), `CHANGELOG.md`, and `docs/output-control.md` describe the changes. Each description stays under the 4,096-character limit. | Description and architecture tests; doc review. |
| REQ-009 | all | Once a deck is pulled, the reported workflow takes at most 4 tool calls and no hand-copied entry fields: find 13 maybeboard cards, move them to the sideboard, add 5 sideboard cards, then preview and apply the push. | An E2E test on the deck fixture, against fake HTTP, runs four calls: `local_deck_get` (filtered, concise); one `local_deck_apply_changes` with one `patch-entries` and 5 `add-entry` changes; `archidekt_push_preview` (concise); and `archidekt_push_apply`. |

Non-goals:

- **Writes that target a filter**, such as "move every Maybeboard entry". Writes stay keyed by explicit entry IDs;
  the REQ-001 read gives the IDs in one call. A filter-targeted write would change entries the caller never saw.
- **Removing `update-entry`, `assign-category`, or `unassign-category`.** They keep working; their descriptions
  point to `patch-entries`. Removal would follow the deprecation rules in `docs/versioning.md` and is not planned.
- **Partial updates for deck metadata and categories.** Each has 3 fields, and leaving out `description` cannot be
  told apart from clearing it.
- **Setting the Archidekt zone category automatically** when only the zone changes. The local store keeps zones and
  categories separate, and choosing the direction is the caller's decision (REQ-007).
- **Filters on other deck read tools.** `stats_local_deck_summary`, `local_deck_validate`, and
  `local_deck_duplicates_preview` report on the whole deck by design.
- **New tools.** The tool count stays the same.
- **ToolSearch loading** in client harnesses.

## Current System

Inspected at `c4fa0f1` (2026-11-01, read-only):

- Change kinds are a closed polymorphic input set (`DeckChangeInput.cs:10-22`, discriminators listed for schema
  enrichment in `DeckBatchWriteTools.cs:23-36`). They map one-to-one onto the Core union `DeckChange`
  (`DeckChanges.cs:71`). `update-entry` takes a whole `DeckEntry`. `assign-category` keeps a previous primary as
  non-primary. A batch has no change-count limit; written IDs share a 100-ID result budget.
- `DeckEntryDraft` (`DeckModels.cs:48-59`) has no categories. It is shared by `add-entry`, `local_deck_create`
  (`DeckContractValidator.cs:13`, `:42`), imports (`DeckInterchangeService.cs:348`), and the Archidekt pull
  mapper (`ArchidektLocalMapper.cs:40`, `:174`). `DeckCategoryDraft` already takes an optional caller-chosen
  `CategoryId` (`DeckModels.cs:73-77`). `whenCardPresent` handling lives in `DeckAddPresence.cs`.
- `local_deck_get` has `cardNames` and `responseFormat` (`DeckReadTools.cs:84-130`). `DeckCardViewCoordinator`
  owns filtering and the cursor fingerprint (`:356-375`), which already covers both.
- Archidekt has no zones. Pull gives the zone from categories (`ArchidektContractMapper.cs:480-504`), and push
  sends categories. Push preview refuses entries whose zone disagrees with their categories
  (`ArchidektSyncPlanner.cs:703-725`; notice in `ArchidektSyncNotices.cs:101`), and the record carries only the
  card name and the two zones (`ArchidektSyncPlanner.cs:1067`).
- `ArchidektDifference` (`ArchidektModels.cs:247-252`) holds `path`, `state`, and three whole JSON values. It is
  returned by `ArchidektSyncDiffView` (`ArchidektReadResults.cs:87`), `ArchidektSyncPreviewView` (`:361`; pull and
  push), and `ArchidektSnapshotRestorePreviewView` (`:118`). The snapshot row (`ArchidektSnapshotOperations.cs:357`)
  holds two content fingerprints.
- Pull apply re-runs the preview with its `discardLocalChanges`, which the preview fingerprint includes
  (`ArchidektCoordinator.cs:265`, `:300`). A mismatch gives a generic `pull-preview-changed`. Push apply refuses a
  missing `confirmRemovals` by name (`:732-735`).

## Design And Decisions

- **D-1, S2 as a Core change.** Add `PatchDeckEntriesChange` to the Core `DeckChange` union, applied by the store
  against each entry's current state inside the batch transaction. That lets a patch see changes made earlier in
  the same batch and refuse with a single issue. The rejected alternative: expanding a patch in App into existing
  primitive changes. That needs the current deck state before the transaction and turns one refusal into several.
  App resolves the `printing` group with `DeckEntryResolver` before the store runs, using the stored card name, or
  the drafted card name for an entry added earlier in the batch. The store writes the resolved values.
- **D-2, primary replacement (owner to confirm).** A new `primaryCategoryId` removes the previous primary's
  assignment, because a zone move needs Maybeboard gone. `addCategoryIds` keeps it as non-primary. The rejected
  alternative: keeping the old primary as `assign-category` does. That would bring back the third change this plan
  removes.
- **D-3, removal lists (owner to confirm).** `remove-entry` takes `entryIds` in place of `entryId`. The rejected
  alternatives: a `remove-entries` kind, which adds a second change kind for one job, and putting removal inside
  `patch-entries`, which mixes deleting entries with editing them.
- **D-4, no second categorization path.** `local_deck_category_rules_apply` assigns categories from rules over card
  data. `patch-entries` assigns categories the caller chose to entries the caller named. They answer different
  questions, so neither replaces the other. `replacePrimary` on `assign-category`, proposed before this plan, is
  dropped: `patch-entries` covers it, and the tool would otherwise have two ways to do the same job.
- **S1:** extend the existing `cardNames` path in `DeckCardViewCoordinator` into one entry filter covering names,
  zones, and category names.
- **S3:** apply drafted categories after the entry is inserted, in the same transaction, through the code
  `assign-category` uses. Update `DeckContractValidator.Normalize` and `DeckAddPresence` so the fields survive
  normalization and are skipped together with a skipped add.
- **S4:** the field-level form is computed in the Archidekt adapter (`MtgMcp.Archidekt`), which owns the entry JSON
  shape, from the same rows the full form uses. App's `ArchidektResultPaging` picks the format and measures it.
  Fingerprints are computed before formatting.
- **S5:** on a pull fingerprint mismatch, recompute the preview fingerprint with the other `discardLocalChanges`
  value from data already fetched, with no second provider read. Name the option only when that fingerprint
  matches. Add the local entry ID to `ArchidektZoneContradiction`; target rows are built in local entry order in
  `ArchidektLocalMapper.ToRemoteTarget`.

### MCP Surface

- No tools are added or removed. Tool count, annotations (`local_deck_apply_changes` writes; the Archidekt diff and
  previews stay read-only), and operation-mode visibility do not change. `patch-entries` is available exactly where
  `local_deck_apply_changes` is.
- Input schema changes: `local_deck_get` gains `zones` and `categoryNames`; the change discriminators gain
  `patch-entries` (`DeckChangeInput` `JsonDerivedType` list and `DeckBatchWriteTools.ChangeKinds`); `remove-entry`
  gains `entryIds`; `DeckEntryDraft` gains two fields; three Archidekt tools gain `responseFormat`.
- Output schema changes: the `notFound` lists on `local_deck_get`, and the concise difference row type.
- Surface tests: tool schema and output-schema snapshots, description length, and the architecture tests that
  check tool registration and mode visibility.

### Fixtures

Deck tests use in-memory stores built in the tests. The Archidekt tests use fake HTTP: a 347-entry deck with 13
Maybeboard entries, and an 18-edit push built from it. No live provider calls, no real Archidekt mutation.

## Steps And Verification

0. **Phase 0, facts (read-only):** record the full push preview size on the 18-edit fixture once it exists, and
   the preview and apply pairs with the options each must repeat (including the confirmation phrase of
   `archidekt_snapshot_restore_apply`).
1. **Phase 1 (REQ-001):** `DeckReadTools`, `DeckCardViewCoordinator`, view models, tests.
2. **Phase 2 (REQ-002 to REQ-004):** Core `DeckChanges` and `DeckModels`; the `MtgMcp.Decks` store;
   `DeckChangeInput`, `DeckBatchWriteTools`, `DeckAddPresence`, and `DeckContractValidator`; tests.
3. **Phase 3 (REQ-005 to REQ-007):** the Archidekt difference form, the three views and their paging, the pull
   option message, the zone notice with entry IDs, fake-HTTP fixtures and tests.
4. **Phase 4 (REQ-008, REQ-009):** descriptions, docs, the E2E workflow test, full audit, merge to `main`, release,
   install.

Each phase runs focused tests, then `task ci`, `task test:integration`, and `task test:e2e` from PowerShell.
Phases 1 and 3 touch separate files from phase 2 and can run in parallel worktrees. Phase 3's REQ-007 fix text
names `patch-entries`, so its tests apply the patch only after phase 2 merges. Phase 4 needs all three.

Audit: at the end, run the full audit with every specialist over the whole change. Fix every supported finding in
scope, including P3. Run at most 3 passes. Re-audit only after a serious finding or a substantial change.

Execution boundary: findings during implementation do not add requirements or phases automatically. Work beyond
these requirements needs the owner's approval. Routine repairs within scope can go ahead.

Versioning: the changes add parameters, a change kind, and fields, and the default formats do not change. Per
`docs/versioning.md` they are additive; the CHANGELOG names `patch-entries`, `remove-entry` `entryIds`, and the
concise difference row.

## Risks And Open Questions

- D-2 and D-3 need the owner's confirmation before implementation.
- **Batch size.** A 100-entry patch plus adds can exceed what one result reports. The existing 100-ID budget and
  exact `changeCounts` apply.
- **Category names in filters.** Archidekt decks can have two categories whose names differ only in case (repaired
  on pull in `0.13.3`). The filter matches all of them.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 | planned | |
| REQ-002 | planned | |
| REQ-003 | planned | |
| REQ-004 | planned | |
| REQ-005 | planned | |
| REQ-006 | planned | |
| REQ-007 | planned | |
| REQ-008 | planned | |
| REQ-009 | planned | |

The plan is complete when every requirement is verified on the merge commit and on the release commit, the audit
is closed, and the release is installed.
