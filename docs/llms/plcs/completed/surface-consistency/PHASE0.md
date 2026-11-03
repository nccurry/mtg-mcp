# Surface Consistency Phase 0: Facts

Read-only facts for the `surface-consistency` PLC (`README.md` in this folder). Every path is relative to the repo
root. Every line number was read at starting commit `b0d9732` (code identical to `4125d0a`). No code was edited, built,
or run. Decisions D-1 to D-6 are taken as recommended.

Layer words used below:

- **Site**: the code at that line already knows the tool's argument names, so it sets the path itself.
- **Lower**: a library project (Scryfall, Decks, Archidekt, Playgroup, Spellbook, Statistics, Core) that does not know
  the tool's argument names. It either names its own parameter (when that equals the tool argument) or returns a
  field key that the tool wrapper turns into a path.
- **Wrapper**: the App tool method or a shared wrapper (`ScryfallToolExecution.RunAsync`, the Archidekt
  `ExecuteAsync` in `ArchidektRemoteWriteTools.cs:216`, `ToolCallPolicy`) that maps a key to the tool's path.

## 0. Summary and differences from the PLC

Counts:

| Fact | Count |
| --- | --- |
| Paged tools (any paging parameter) | 27 (a: 6, b: 8, c: 10, d: 3) |
| Paging styles found | 6 (cursor, offset+fingerprint, bare offset, two offsets, limit+offset, page+limit), plus 2 special shapes (two cursors, nested `rows.pageSize`) |
| `responseFormat` tools today | 10 (4 default standard, 2 concise, 4 full); after D-2 all default standard |
| `new OperationInvalidInput(...)` sites in `src/` | 205 (111 name a path, 94 do not) |
| Of the 94 without a path | 11 are startup configuration (not tool-reachable), 83 are reachable from a tool |
| Reachable sites by project | Scryfall 45, App 19, Statistics 7, Decks 6, Archidekt 2, Playgroup 2, Core 1, Spellbook 1 |
| Funnel creators behind those sites | about 130 more call sites that reach `Invalid<T>(message)` helpers or `throw` provider exceptions (listed in section 4) |
| Archidekt tools whose input is a `request` object | 12 (11 remote writes plus `archidekt_pull_apply`), plus `playgroup_live_session_create` |
| Operations that destroy or replace data (Destructive annotation) | 17 tools; 7 are covered by D-5 as written (4 Archidekt, 2 Scryfall deletes, 1 activation change), 3 need an owner call (section 6) |
| Removed change codes still in `src/` | 12 literals at 19 sites, plus the page `preview-changed` at 1 site (section 8) |

Findings that contradict or extend the PLC (the owner or the phase 1 and 2 owners must settle these):

1. **Twelve Archidekt tools take `request`, not eleven.** `archidekt_pull_apply` also does
   (`src/MtgMcp.App/Archidekt/ArchidektLocalWriteTools.cs:46`). Also `playgroup_live_session_create` takes a
   `request` object of eight scalar fields (`src/MtgMcp.App/Playgroup/PlaygroupRemoteWriteTools.cs:54`,
   `src/MtgMcp.Playgroup/PlaygroupModels.cs:52`). It is not on the D-3 allowlist, which lists "Playgroup events" (that
   is `playgroup_game_events_batch_create.events`). Flatten it, or add it to the allowlist.
2. **`scryfall_card_data_sync` has no acknowledgement today.** The activation-change boolean
   (`acknowledgeActivationChange`) is on `scryfall_card_data_rollback`
   (`src/MtgMcp.App/Scryfall/ScryfallWriteTools.cs:75`). D-5 names the sync tool. Section 6 explains.
3. **The Spellbook tools already take flat arguments.** `limit`, `offset`, and `groupByCombo` are separate tool
   arguments (`src/MtgMcp.App/Spellbook/SpellbookReadTools.cs:43-45`, `:82-84`). The nested
   `SpellbookPageOptions` (`src/MtgMcp.Spellbook/SpellbookPageOptions.cs:6`) is an adapter type, and the result echoes
   `request.page` with `limit`, `offset`, `groupByCombo` (`src/MtgMcp.Spellbook/SpellbookRequestDetails.cs`,
   `SpellbookPageRequest.cs`). The Non-goals say adapter HTTP contracts stay; the adapter-side record and the echoed
   `request.page` still have to change.
4. **No Archidekt read has a page size today.** `archidekt_deck_get`, `archidekt_folders_get`,
   `archidekt_snapshots_get`, `archidekt_sync_diff`, and the three previews page only by the byte budget
   (`src/MtgMcp.App/Hosting/ResultPage.cs:152`). `pageSize` is a new argument there, not a rename.
5. **`local_deck_list` cursor binds nothing.** It is a bare decimal offset string
   (`src/MtgMcp.Decks/SqliteDeckStore.cs:104-106`, `:924-933`). D-1 needs a binding (section 1).
6. **Several tools have no result-size cut at all**: `local_deck_list`, `archidekt_deck_list`, every Spellbook result
   (up to 2 MiB, `src/MtgMcp.Spellbook/SpellbookTransport.cs:15`), `playgroup_playgroup_games_list`
   (`src/MtgMcp.Playgroup/PlaygroupTransport.cs:16`), `scryfall_request_snapshots_get` with `includeRaw`, and
   `scryfall_tags_search` with `includeRaw`. REQ-002 cannot hold by default page size alone there (section 2).
7. **`ConflictState` has no `previewFingerprint`** (`src/MtgMcp.Core/Results/OperationResult.cs:215-221`: only
   `revision` and `remoteFingerprint`). `preview-changed` and `cursor-stale` "carrying `current`" need a Core change
   in phase 1.
8. **A fourth revision name exists**: results use `deckRevision` (category and identity previews, export bundle,
   import-related models), not only `localRevision` (section 5).
9. **The card-list tools return a result field named `fingerprint`**
   (`src/MtgMcp.App/CardLists/CardListModels.cs:78`, `:110`). It fingerprints the request for cursors; it is not a
   preview fingerprint. The REQ-003 naming test needs a decision: rename it (`requestFingerprint`) or exempt it.
10. **A fifth deck identifier**: card-list sources use `archidektDeckId`
    (`src/MtgMcp.App/CardLists/CardListSourceInput.cs`, `ArchidektDeckSourceInput` and
    `ArchidektSnapshotSourceInput`). D-4 makes it `remoteDeckId`.
11. **`local_deck_backup_delete` has no guard at all** (only `backupId`,
    `src/MtgMcp.App/Decks/DeckWriteTools.cs:172-177`), not a revision guard. D-5 says backup deletes "keep their
    revision guard"; there is none, and deleting a backup is not undone by a backup.
12. **A pull and a push apply treat the remote fingerprint differently.** Pull returns `remote-deck-changed` for a
    changed remote after the preview check (`src/MtgMcp.App/Archidekt/ArchidektCoordinator.cs:419`); push folds the
    same cause into `push-preview-changed` (`:735`, called from `:773-780` and `:832`). The PLC end state sends "a
    fingerprint mismatch" to `preview-changed`; recommended: `previewFingerprint` mismatch is `preview-changed`, a
    changed `expectedRemoteFingerprint` is `remote-deck-changed` on both applies.
13. **Four `invalid-input` results that are really conflicts or limits**: `identity-preview-mismatch`
    (`DeckIdentityReconciliation.cs:346`) is `invalid-input` today and becomes a conflict;
    `request-limit-exceeded` (`ArchidektRequestPacer.cs:44`, `:61`, `:97`, and the push block at
    `ArchidektCoordinator.cs:839`) names no argument; `provider-request-rejected` and `source-request-rejected`
    (provider HTTP 400) name none either. Reclassify them or give them a path.
14. **A real defect found on the way**: `local_deck_category_rules_preview` on an empty deck with tag selectors returns
    `invalid-card-lookup` "Deck card lookups are required" (`src/MtgMcp.Scryfall/ScryfallCardDataStore.cs:696`, reached
    from `DeckCategorization.cs:175` with zero entries).

## 1. Paged tools

Binding words: **revision** = local deck revision; **preview** = `previewFingerprint`; **remote** = Archidekt
`remoteFingerprint` (or `treeFingerprint`); **artifact+checksum**; **provider** = provider offset or page.

### Family a: local deck, interchange, backups (6)

| Tool | Params today (file:line) | Next-page field (file:line) | Default / max | Cursor must bind |
| --- | --- | --- | --- | --- |
| `local_deck_list` | `cursor`, `pageSize` (`src/MtgMcp.App/Decks/DeckReadTools.cs:66-67`) | `nextCursor` (`src/MtgMcp.Core/Decks/DeckModels.cs:21-23`) | 50 / 100 (`SqliteDeckStore.cs:76-80`) | Not bound today (bare offset, `SqliteDeckStore.cs:104-106`, parsed `:924-933`). Bind a deck-list fingerprint: count plus the `deckId`, `revision` pairs of the page range, or the newest `updatedAtUtc`. Decks created or deleted between pages must give `cursor-stale`. |
| `local_deck_get` | `cursor`, `pageSize` (`int?`), `responseFormat` (`DeckReadTools.cs:111`, `:115`, `:139`) | `nextCursor` (`src/MtgMcp.App/Decks/DeckCardViewModels.cs:166-168`) | with `include`: 25; without `include`, `cardNames`, `zones`, `categoryNames`: whole deck when it fits (`DeckCardViewCoordinator.cs:177-210`); default and max `:32`, `:37`, range check `:402-411` | revision plus request fingerprint plus offset (`DeckCardViewCoordinator.cs:632`, `:737`; stale check `:473`) |
| `local_deck_import_preview` | `offset`, `expectedPreviewFingerprint` (`src/MtgMcp.App/Decks/DeckInterchangeReadTools.cs:274-275`) | `nextOffset` (`src/MtgMcp.App/Decks/DeckPreviewPaging.cs:131`) | no page size; byte-filled (`DeckPreviewPaging.cs:231-330`) | preview (the result `fingerprint`, `DeckPreviewPaging.cs:85`; null when nothing is creatable, then bind a hash of formatId plus content plus options). The caller re-sends `content`, `formatId`, `options` every call. |
| `local_deck_identity_reconcile_preview` | `offset`, `expectedPreviewFingerprint` (`src/MtgMcp.App/Decks/DeckIdentityReconciliation.cs:758-759`) | `nextOffset` (`DeckPreviewPaging.cs:37`) | none; byte-filled (`DeckPreviewPaging.cs:188-228`) | preview plus revision (`expectedRevision` is required and re-sent) |
| `local_deck_category_rules_preview` | `offset`, `expectedPreviewFingerprint` (`src/MtgMcp.App/Decks/DeckCategorization.cs:316-317`) | `nextOffset` (`DeckPreviewPaging.cs:164`) | none; byte-filled (`DeckPreviewPaging.cs:334-400`) | preview (covers deck revision and the Scryfall generation) |
| `local_deck_export_bundle` | `artifact`, `offset` in UTF-16 code units (`DeckInterchangeReadTools.cs:311`, `:315`) | `chunk.nextOffset` (`src/MtgMcp.App/Decks/DeckExportPaging.cs:40`; the field is inside `chunk`, not top level) | 30,000 UTF-8 bytes of JSON per chunk (`DeckExportPaging.cs:66`, shrink loop `:144-160`) | artifact file name plus the artifact `sha256` plus deck revision. Counts code units; never split a surrogate pair (`WholeCharacters`, `DeckExportPaging.cs:164`). |

### Family b: Archidekt (8)

| Tool | Params today (file:line) | Next-page field (file:line) | Default / max | Cursor must bind |
| --- | --- | --- | --- | --- |
| `archidekt_deck_list` | `cursor`, `pageSize` (`src/MtgMcp.App/Archidekt/ArchidektReadTools.cs:84-85`) | `nextCursor` (`src/MtgMcp.Archidekt/ArchidektModels.cs:144`) | 50 / 100 (`ArchidektDeckTransport.cs:399-406`) | offset plus list checksum (`ArchidektDeckTransport.cs:448`, stale check `:60-66`); already bound |
| `archidekt_deck_get` | `deckId`, `offset`, `zones`, `responseFormat` (`ArchidektReadTools.cs:110-116`) | `nextOffset` (`src/MtgMcp.App/Archidekt/ArchidektReadResults.cs:321`) | none; byte-filled (`ArchidektResultPaging.cs:20-105`) | remote fingerprint plus zones plus format. Unguarded today (`CheckPage(null, null)`, `ArchidektResultPaging.cs:49`). |
| `archidekt_sync_diff` | `localDeckId`, `differenceOffset`, `responseFormat` (`ArchidektReadTools.cs:143-147`) | `nextDifferenceOffset` (`ArchidektReadResults.cs:111`) | none; byte-filled (`ArchidektResultPaging.cs:243-285`) | remote fingerprint plus local revision plus baseline fingerprint (all in the result header, `ArchidektReadResults.cs:88-95`). There is no diff fingerprint yet. |
| `archidekt_pull_preview` | `remoteDeckId`, `localDeckId`, `discardLocalChanges`, `operationOffset`, `differenceOffset`, `expectedPreviewFingerprint`, `responseFormat` (`ArchidektReadTools.cs:189-203`) | `nextOperationOffset`, `nextDifferenceOffset` (`ArchidektReadResults.cs:404`, `:410`) | none; both lists byte-filled by `ResultPage.TakeTwo` (`ResultPage.cs:167`) | preview (`PullPreviewFingerprint`, `ArchidektCoordinator.cs:1207`; covers remote, local revision, discard flag, operations) plus both positions in one cursor |
| `archidekt_push_preview` | `localDeckId`, `operationOffset`, `differenceOffset`, `expectedPreviewFingerprint`, `confirmRemovals`, `responseFormat` (`ArchidektReadTools.cs:244-255`) | same two fields (`ArchidektReadResults.cs:404`, `:410`) | none; byte-filled | preview (`PushPreviewFingerprint`, `ArchidektCoordinator.cs:1230`) plus both positions |
| `archidekt_snapshot_restore_preview` | `deckId`, `snapshotId`, `operationOffset`, `differenceOffset`, `expectedPreviewFingerprint` (`ArchidektReadTools.cs:330-336`) | `nextOperationOffset`, `nextDifferenceOffset` (`ArchidektReadResults.cs:147`, `:153`) | none; byte-filled (`ArchidektResultPaging.cs:287-350`) | preview plus snapshot checksum and content fingerprint plus both positions |
| `archidekt_folders_get` | `kind`, `folderId`, `offset` (`ArchidektReadTools.cs:375`) | `nextOffset` (`ArchidektReadResults.cs:80`) | none; byte-filled (`ArchidektResultPaging.cs:218-241`) | `treeFingerprint` plus kind plus folderId |
| `archidekt_snapshots_get` (kind get only; kind list is unpaged) | `kind`, `deckId`, `snapshotId`, `offset`, `zones`, `responseFormat` (`ArchidektReadTools.cs:430-439`) | `snapshot.deck.nextOffset` (`ArchidektReadResults.cs:321`, via `ArchidektSnapshotView` `:30-33`) | none; byte-filled (`ArchidektResultPaging.cs:195-216`) | snapshot checksum plus content fingerprint plus zones plus format |

### Family c: Scryfall and card lists (10)

| Tool | Params today (file:line) | Next-page field (file:line) | Default / max | Cursor must bind |
| --- | --- | --- | --- | --- |
| `scryfall_search` | `cursor`, `pageSize`, `responseFormat` (`src/MtgMcp.App/Scryfall/ScryfallReadTools.cs:110-116`) | `page.nextCursor` (`src/MtgMcp.Scryfall/ScryfallModels.cs:240`) | 25 / 100 (25 with `full`) (`ScryfallService.cs:2004-2012`) | snapshot checksum plus request hash (immutable snapshot; `ScryfallCursor.cs:19-55`) |
| `scryfall_cards_get` | `cursor`, `pageSize`, `responseFormat` (`ScryfallReadTools.cs:157-162`) | `page.nextCursor` | 25 / 100 (25 with `full`) | request hash plus snapshot id and checksum plus corpus generation plus result checksum plus offset (`ScryfallCursor.cs:164-171`) |
| `scryfall_autocomplete` | `cursor`, `pageSize` (`ScryfallReadTools.cs:188-189`) | `page.nextCursor` | 25 / 100 | snapshot checksum plus scope |
| `scryfall_tags_search` | `cursor`, `pageSize` (`ScryfallReadTools.cs:237-238`) | `nextCursor` | 25 / 100 | corpus generation plus query (`ScryfallCardDataStore.cs:887`) |
| `scryfall_installed_cards_by_tag` | `cursor`, `pageSize`, `responseFormat` (`ScryfallReadTools.cs:270-277`) | `page.nextCursor` | 25 / 100 (25 with `full`) | corpus generation plus tag expression plus format (`ScryfallService.cs:1362`) |
| `scryfall_reference_get` (kinds sets, catalog) | `cursor`, `pageSize` (`int?`) (`src/MtgMcp.App/Scryfall/ScryfallReferenceTools.cs:147-149`) | `page.nextCursor` | 25 / 100; rejected for kind bulk (`ScryfallReferenceTools.cs:350-356`) | snapshot checksum plus kind plus identifier |
| `scryfall_request_snapshots_get` | `cursor`, `pageSize` (`ScryfallReferenceTools.cs:197-199`) | `nextCursor` (`ScryfallModels.cs:419`; list kind via `ScryfallPage`) | 25 / 100 (25 with `includeRaw`) | snapshot checksum (get) or filter hash (list) (`ScryfallSnapshotStore.cs:212`, `:273`) |
| `scryfall_card_details` | `printsCursor`, `rulingsCursor`, one `pageSize` (`ScryfallReferenceTools.cs:102-106`) | `prints.nextCursor` and `rulings.nextCursor` (one per section) | 25 / 100 (25 with `includeRaw`) | two positions in one cursor: prints snapshot checksum plus offset, rulings snapshot checksum plus offset (the "two-list" rule in the PLC Design Notes) |
| `card_lists_compare` | `cursor`, `pageSize`, `responseFormat` (`src/MtgMcp.App/CardLists/CardListTools.cs:158`, `:165`, `:169`) | `page.nextCursor` (`ScryfallModels.cs:240`) | 25 / 100 (25 with `full`) (`CardListCoordinator.cs:17-22`, `:98`) | request fingerprint plus one marker per source (deck revision, Archidekt remote fingerprint, backup id) plus offset (`CardListCursor.cs:16`, `:53-108`) |
| `card_list_query` | `cursor` (`CardListTools.cs:232`) and `rows.pageSize` inside `rows` (`CardListModels.cs:58-59`) | `rows.nextCursor` (`CardListModels.cs:96`) | 25 / 100 (`CardListCoordinator.cs:230-236`) | same as compare. End state needs a top-level `pageSize` and `nextCursor` (owner call: move `rows.pageSize`). |

### Family d: Spellbook, Playgroup (3)

| Tool | Params today (file:line) | Next-page field | Default / max | Cursor must bind |
| --- | --- | --- | --- | --- |
| `spellbook_variant_search` | `limit`, `offset` (`src/MtgMcp.App/Spellbook/SpellbookReadTools.cs:43-44`) | none (provider JSON carries `next`; the server adds nothing) | 20 / 25, offset 0 to 1000 (`src/MtgMcp.Spellbook/SpellbookContract.cs:59`, `:74`, `:79`, check `:135-143`) | provider offset plus `sourceQuery` plus `groupByCombo`. The cursor ends at offset 1000: no `nextCursor` once `offset + limit` would pass the cap. |
| `spellbook_deck_combos_find` | `limit`, `offset` (`SpellbookReadTools.cs:82-83`) | none | same | provider offset plus `sourceQuery` plus `groupByCombo` plus deck revision plus the sent entry selection (`expectedRevision` is re-sent) |
| `playgroup_playgroup_games_list` | `page`, `limit` (`src/MtgMcp.App/Playgroup/PlaygroupReadTools.cs:178-179`) | none | page 1; limit 10 / 100 (`PlaygroupService.cs:197`) | provider page plus `playgroupId` plus `includeEvents`. No total is reported, so `nextCursor` follows every full page; an exact multiple gives an empty last page (PLC special case). |

Not paged and not changed: `local_deck_duplicates_preview` (groups cut by `CutList`), `stats_*`,
`playgroup_games_stats_get` (`maxGames` is a read bound, not a page; `PlaygroupService.cs:243`),
`spellbook_deck_bracket_facts_get`, `archidekt_snapshots_get` kind list, `local_deck_backup_list`.

## 2. Default page size per paged tool (D-2)

### How this was estimated

- The limit is `ResultSizeLimit.MaximumBytes = 40,000` UTF-8 bytes of structured result
  (`src/MtgMcp.Core/Results/ResultSizeLimit.cs:14`) less `EnvelopeReserveBytes = 3,000` (`:20`). Rows plus the result
  header therefore get 37,000 bytes (`ResultPage.Budget`, `src/MtgMcp.App/Hosting/ResultPage.cs:209-212`).
  `ResultPage.Take` measures each row with the server serializer plus one byte, so every tool that uses it already
  cuts a page early; `pageSize` becomes the row maximum on top of that cut.
- Row bytes come from two sources. Scryfall `standard` rows are measured from the golden files
  `tests/MtgMcp.Scryfall.Tests/Fixtures/golden/standard-search-page.golden.json` (6 cards: 3,214 / 5,064 / 3,746 /
  3,595 / 5,237 / 3,377 bytes, mean 4,039, max 5,237) and `standard-cards.golden.json` (4,295 / 6,280 / 4,962 / 4,676 /
  6,318 / 4,593, mean 5,187, max 6,318). Every other row was built by hand in a scratch script from the model records
  (camelCase, nulls omitted as `McpJsonUtilities.DefaultOptions` does, UTF-8) with realistic values (36-byte UUIDs,
  16-character names, 1.6 categories). The estimate is within about 20 percent; phase 2 must replace each with a
  measured test (REQ-002 requires one).
- "Largest fixture": the generated decks `CreateRealisticDeck` (2 commander, 98 main, 21 sideboard, 347 maybeboard
  = 468 entries, `tests/MtgMcp.App.Tests/ArchidektDeckReadTests.cs:414-451`), `CreateLargeDeck(347)` and `(300)`
  (`tests/MtgMcp.App.Tests/ArchidektWorkflowTests.cs:2995-3015`), and the 347-entry local deck
  (`tests/MtgMcp.App.Tests/DeckCardViewTests.cs:978`). The recorded Archidekt deck
  `tests/MtgMcp.Archidekt.Tests/Fixtures/recorded/decks-id/22959335.json` holds only 5 cards (21 KB) and cannot size a
  page.
- The proposal rule: default = the largest whole number of the largest realistic row that fits 34,000 bytes (the 37,000
  budget less 3,000 for the header), rounded down to a convenient value; for unbounded text rows also require a
  byte cut. Where no cut exists today the table says so.

### Estimates and proposed defaults

| Tool | Row type in `standard` | Row bytes (basis) | Rows that fit 34,000 B | Proposed default `pageSize` | Maximum | Note |
| --- | --- | --- | --- | --- | --- | --- |
| `local_deck_list` | deck summary | 341 (est.) | 99 | 25 | 100 | Description is unbounded and rows are not byte cut (`SqliteDeckStore.cs:99-107` returns the page whole). Add `ResultPage.Take` and `ResultText.Description` (8,000 B cap). |
| `local_deck_get` | entry plus about 1.5 assignment rows | 480 (299 + 1.5 x 121) | 70 | 25 | 100 | With `include` cards and tags a row is about 1,060 B (881 + 181), 32 rows; mean-text cards fit 25 (26.5 KB). Both paths already cut with `ResultPage.Take`. One default for both modes; the "whole deck if it fits" shortcut (`DeckCardViewCoordinator.cs:177-190`) goes. A 347-entry deck then needs 14 pages (5 today). |
| `local_deck_import_preview` | proposed entry plus assignments | 480 (est.) | first page about 25 after up to 21 KB of header (`ImportHeaderListBytes` 12,000 at `DeckPreviewPaging.cs:177`, name 1,000, description 8,000); later pages 70 | 25 | 100 | Worst-case first page: 21 KB + 25 x 480 = 33 KB. |
| `local_deck_identity_reconcile_preview` | row with before, after, snapshot | 775 (est.) | 43 | 25 | 100 | 150-entry cap means 6 pages. |
| `local_deck_category_rules_preview` | decision | 230 (est.) | 148 | 50 | 200 | Header carries `expandedRules` and `source` (unmeasured, could reach 10 to 20 KB); 50 x 230 = 11.5 KB stays under the budget either way. |
| `local_deck_export_bundle` | UTF-16 code units | n/a | n/a | 30,000 code units (cut by the existing 30,000-byte encoded fit) | 30,000 | The cursor counts code units; `pageSize` here means code units, an owner call. |
| `archidekt_deck_list` | remote summary | 464 (est.) | 73 | 25 | 100 | No byte cut today and `description` is unbounded; add `Take` and a description cut. |
| `archidekt_deck_get` | entry, every field (today's `full`) | 387 (est.) vs 197 concise | 87 (47 after a worst first-page header of 19 KB) | 40 | 100 | Default grows rows by 2x versus today's concise default. The 468-entry realistic deck: 181 KB, 6 pages by bytes, 12 at 40. |
| `archidekt_snapshots_get` (get) | same as deck_get | 387 | 87 | 40 | 100 | Kind list is not paged; 317 B per snapshot summary (est.). |
| `archidekt_folders_get` | folder record | 163 (est.) | 208 | 50 | 200 | `extensions` per folder is unbounded provider data. |
| `archidekt_sync_diff` | difference, standard (today's full) | 908 for a change, 478 for an add, about 1,400 for a three-way conflict; 109 concise | 24 at 1,400 | 20 | 100 | Values are serialized entry JSON strings, escaped. |
| `archidekt_pull_preview` | operations plus differences share one page | same rows; operation 129 | 24 at 1,400 | 20 (rows of both lists together) | 100 | Header (summary, `applyOptions`, `applyNotice`) about 1.5 to 3 KB. 300 edits: 15 pages (9 today). |
| `archidekt_push_preview` | same | same | 24 | 20 | 100 | Same. |
| `archidekt_snapshot_restore_preview` | same | same | 24 | 20 | 100 | No `responseFormat` today. |
| `scryfall_search` | `ScryfallCardView`, standard | 4,039 mean, 5,237 max (golden) | 6 at max, 8 at mean | 6 | 100 (25 `full`) | Today's default 25 is cut by bytes to about 8 per page. 6 x 5,237 = 31.4 KB fits even with the largest golden card. |
| `scryfall_cards_get` | collection row, standard | 5,187 mean, 6,318 max (golden) | 5 at max | 5 | 100 (25 `full`) | 5 x 6,318 = 31.6 KB. |
| `scryfall_installed_cards_by_tag` | card view plus tag evidence | about 4,300 mean, about 5,500 max (golden plus 250 B, est.) | 6 | 6 | 100 (25 `full`) | |
| `card_lists_compare` | `ScryfallCardView`, standard | 4,039 mean, 5,237 max | 6 | 6 | 100 (25 `full`) | Header adds counts, sources, and a cut `unmatched` list (`CardListCoordinator.cs:195`). |
| `card_list_query` | row with the caller's `fields` (default name, oracleId) | about 120 (est.) | 280 | 25 | 100 | `groups` sit in the header; the existing cut keeps the page small. Unchanged. |
| `scryfall_autocomplete` | name string | 32 | 1,000 | 25 | 100 | Unchanged. |
| `scryfall_tags_search` | tag | 227 (est.) | 149 | 25 | 100 | With `includeRaw` a raw tag may hold large assignment arrays and nothing cuts it; cap `includeRaw` pages like `full` (25) and measure. |
| `scryfall_reference_get` (sets, catalog) | set 359 (est.), catalog name about 20 | | 94 / 1,700 | 25 | 100 | Unchanged. Raw sets about 1 KB each. |
| `scryfall_request_snapshots_get` | list 400 (est.); get member 100 without `raw` | | 85 / 340 | 25 | 100 (25 raw) | With `includeRaw` a member is a raw provider object (cards about 3 to 6 KB): 25 x 6 KB = 150 KB and no cut. Needs a cut or a smaller raw cap (5). |
| `scryfall_card_details` | print 361 (est.), ruling 236 (est.) | | 94 / 144 | 25 (per section) | 100 (25 raw) | Raw prints have the same problem as raw snapshot members. |
| `spellbook_variant_search` | variant, raw provider JSON | about 5,000 for a three-card combo (est., no fixture holds one; the only fixture is `estimate-bracket-response.json`, 19 KB) | 6 | 5 | 25 | No byte cut: today's default 20 would be about 100 KB. Phase 2 must measure a real response, then either cut or lower the default. |
| `spellbook_deck_combos_find` | same | same | 6 | 5 | 25 | `find-my-combos` may return several lists per response (unverified). |
| `playgroup_playgroup_games_list` | game with about 4 participations | about 930 (est. from the OpenAPI schema `Game` and `Participation`, `src/MtgMcp.Playgroup/Fixtures/OpenApi/public-v1-1.0.0.yaml:1107-1180`) | 36 | 10 (as today) | 35 (today 100) | With `includeEvents` each event adds about 190 B and a game may hold 50 to 300 events: lower the cap to 5 or cut when events are included. |

## 3. `responseFormat` per tool under D-2

Every tool defaults to `standard`. An unknown value stays `invalid-response-format` with path `responseFormat`
(`ArchidektDeckReading.cs:60`, `DeckCardViewCoordinator.cs:394`, `ScryfallCardFormat.cs:69`).

| Tool | Today (default; values accepted; where parsed) | Values to offer | `concise` returns (only where a short form exists) |
| --- | --- | --- | --- |
| `scryfall_search` | standard; concise, standard, full (`ScryfallResponseFormat.cs:36-60`) | concise, standard, full (no change) | the `fields` list, or the default set; `scryfallId`, `oracleId`, `name` always (`ScryfallConciseFields.cs:11-18`) |
| `scryfall_cards_get` | same | concise, standard, full | same |
| `scryfall_installed_cards_by_tag` | same | concise, standard, full | same |
| `card_lists_compare` | same (`CardListTools.cs:158`; parsed `CardListCoordinator.cs:92`) | concise, standard, full | same |
| `archidekt_deck_get` | **concise**; concise, standard, full with standard equal to full (`ArchidektDeckReading.cs:50-63`) | concise, standard (no `full`: Archidekt has no raw provider objects) | `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`, `collectorNumber`, `oracleId`, `inDeck` (`ArchidektReadResults.cs:208-221`). `standard` adds `providerRelationId`, `providerCardId`, `printingId`, `language`, `finish`, `primaryCategoryName`, `sortOrder` (`ArchidektReadResults.cs:241-257`). Default changes concise to standard. |
| `archidekt_snapshots_get` | **concise** (kind get only) | concise, standard | same as deck_get; default changes to standard |
| `local_deck_get` | **full**; concise, standard, full with standard equal to full (`DeckCardViewCoordinator.cs:213`, `:389-398`) | concise, standard (`full` removed) | `entryId`, `cardName`, `quantity`, `zone`, `categoryNames` and no assignment rows; with `include` it still adds `cardData`, `card`, `tags` (description at `DeckReadTools.cs:133-137`). `standard` is today's `full`. |
| `archidekt_sync_diff` | **full**; concise, standard, full with standard equal to full | concise, standard (`full` removed) | per difference `path`, `state`, and `fields` holding only the differing fields with `baseline`, `local`, `remote` (`ArchidektReadTools.cs:30-34`); fingerprints, guards, summary, operations, `applyAvailable` unchanged. `standard` is today's `full`. |
| `archidekt_pull_preview` | **full**; same | concise, standard | same as diff |
| `archidekt_push_preview` | **full**; same | concise, standard | same as diff |

No other tool gains `responseFormat`: `archidekt_snapshot_restore_preview`, the local previews, `card_list_query`,
`scryfall_card_details`, `scryfall_reference_get`, and `scryfall_request_snapshots_get` have no short form today
(`includeRaw` stays their raw switch). `AllowedValuesAttribute` today is one shared enum
(`src/MtgMcp.App/Hosting/AllowedToolValues.cs:28-31`: concise, standard, full); phase 1 needs a second attribute for the
two-value tools, and the published schema enum per tool must match (`ToolAllowedValuesTests`).

## 4. Error paths (REQ-007)

205 `new OperationInvalidInput(` sites exist in `src/`. 111 pass a path. The 94 that do not are listed here; a scan
script counted constructor arguments, then each site was read. Eleven are startup configuration and out of scope:
`CapabilityToolsetSelection.cs:195`, `DataRootResolver.cs:63`, `:80`, `FoundationConfigurationLoader.cs:83`, `:236`,
`:281`, `:304`, `:326`, `:343`, `:355`, `OperationMode.cs:83`. The other 83 follow. After D-1, D-3 and D-4, "path" uses
the new argument names and flat Archidekt arguments (no `request.` prefix).

### 4a. App layer: set the path at the site (19 sites)

| Site | Reason code | Tool(s) | Path to carry | Who |
| --- | --- | --- | --- | --- |
| `src/MtgMcp.App/Archidekt/ArchidektCoordinator.cs:624` | `entry-zone-contradicts-category` | `archidekt_push_preview`, `archidekt_push_apply` | `localDeckId` | site |
| `ArchidektCoordinator.cs:839` | `block.ReasonCode` (`request-limit-exceeded`, `printing-unresolved`; `ArchidektDeckOperations.cs:377-415`) | `archidekt_push_apply` | `localDeckId`; reclassify `request-limit-exceeded` (no argument is at fault) | site |
| `src/MtgMcp.App/Decks/DeckBatchWriteTools.cs:132` | `invalid-when-card-present` | `local_deck_apply_changes` | `whenCardPresent` | site |
| `DeckBatchWriteTools.cs:143` | `invalid-deck-change` (mapper failure) | `local_deck_apply_changes` | `changes[i]` (index is in the message; `DeckChangeInputMapper.TryMap` must return it) | site, mapper returns the index |
| `DeckBatchWriteTools.cs:162` | `invalid-deck-change` (printing cannot apply) | same | `changes[i]` (`callerIndexes[...]`) | site |
| `DeckBatchWriteTools.cs:175` | `invalid-deck-change` (presence refusal) | same | `changes[i]` | site |
| `src/MtgMcp.App/Decks/DeckCategorization.cs:88` | `invalid-category-apply-token` | `local_deck_category_rules_apply` | `applyToken` | site |
| `DeckCategorization.cs:110` | `category-preview-incomplete` | same | `previewFingerprint` (state problem; alternatively a conflict) | site |
| `src/MtgMcp.App/Decks/DeckCategoryRuleResolver.cs:44` | `invalid-category-rule-source` | category preview, category apply | `source` | site |
| `DeckCategoryRuleResolver.cs:49` | `invalid-freshness-policy` | same | `freshnessPolicy` | site |
| `DeckCategoryRuleResolver.cs:113` | `invalid-category-rule-category` | same | `source.ruleSet.rules[i].categoryId` | site (needs the rule index) |
| `src/MtgMcp.App/Decks/DeckIdentityReconciliation.cs:293` | `invalid-identity-apply-token` | `local_deck_identity_reconcile_apply` | `applyToken` | site |
| `DeckIdentityReconciliation.cs:338` | `invalid-identity-apply-token` | same | `applyToken` | site |
| `DeckIdentityReconciliation.cs:346` | `identity-preview-mismatch` | same | none: becomes the `preview-changed` conflict | site |
| `DeckIdentityReconciliation.cs:353` | `partial-identity-reconciliation-not-allowed` | same | `allowPartial` | site |
| `DeckIdentityReconciliation.cs:440` | `invalid-identity-entry-selection` | identity preview (`entryIds`), identity apply (selection read from the token) | `entryIds` (preview) and `applyToken` (apply): two creators or a parameter | site |
| `src/MtgMcp.App/Hosting/ToolCallPolicy.cs:191` | `invalid-arguments` (blank argument name) | every tool | `arguments` (the offending name is blank) | site |
| `src/MtgMcp.App/Spellbook/SpellbookDeckInputResolver.cs:194` (creator; callers `:59`, `:123`, `:129`, `:135`, `:172`, `:184`) | `invalid-spellbook-deck-selection` | `spellbook_deck_combos_find`, `spellbook_deck_bracket_facts_get` | `:59` splits into `deckId` and `expectedRevision`; the rest describe the deck's content and carry `deckId` | site |
| `src/MtgMcp.App/Statistics/StatisticsDeckResolver.cs:391` (creator; callers `:42`, `:100`, `:132`, `:137`, `:161`, `:176`, `:182`) | `invalid-statistics-selection` | the seven `stats_*` population tools, `stats_local_deck_summary` | `:42` `request.population.kind`; `:100` splits into `request.population.deckId` and `.expectedRevision`; `:132`, `:137` `request.population.populationSelectors`; `:161` `request.population.groups`; `:176` `request.population.groups[i]`; `:182` `request.population.groups[i].selectors`. `stats_local_deck_summary` has no `population` wrapper (`request.populationSelectors`). | wrapper passes the base path to the resolver |

### 4b. Decks, Core, Statistics libraries (14 sites plus funnels)

| Site | Reason code | Tool(s) | Path | Who |
| --- | --- | --- | --- | --- |
| `src/MtgMcp.Decks/SqliteDeckStore.cs:1015` (creator, `DeckReasonCodes.InvalidDeckInput`); callers `:79` | page size | `local_deck_list` | `pageSize` | lower |
| `SqliteDeckStore.cs:84` | bad cursor (reason must become `invalid-cursor`) | `local_deck_list` | `cursor` | lower |
| `SqliteDeckStore.cs:124`, `:309` | invalid deck ID | `local_deck_get` and others, `local_deck_export_bundle` | `deckId` (Archidekt callers: wrapper renames to `localDeckId`) | lower |
| `SqliteDeckStore.cs:219`, `:269` | no matching binding, bad binding IDs (internal) | `archidekt_pull_*`, `archidekt_push_*`, `archidekt_sync_diff` | `localDeckId` | lower, wrapper renames |
| `SqliteDeckStore.cs:412` | ID or revision invalid (one reason, two arguments: split) | `local_deck_delete` | `deckId` / `expectedRevision` | lower |
| `SqliteDeckStore.cs:474`, `:479` | same pair; empty change list | `local_deck_apply_changes` (and category/identity apply through the store) | `deckId` / `expectedRevision`; `changes` | lower |
| `SqliteDeckStore.cs:956`, `:966` | forwarded `DeckInputException` and SQLite constraint | create, apply | `request.<field>` or `changes[i]`: carry the field in the exception | lower |
| `src/MtgMcp.Decks/DeckInterchangeService.cs:422` (creator); callers `:112`, `:286` | format not supported for the direction; content over 5 MiB | `local_deck_import_preview`, `local_deck_export_bundle` | `formatId`; `content` | lower |
| `DeckInterchangeService.cs:164` | `invalid-import-preview` | `local_deck_import_create` | `content` | lower |
| `DeckInterchangeService.cs:179` | `partial-import-not-allowed` | `local_deck_import_create` | `options.allowPartial` | lower |
| `src/MtgMcp.Decks/DeckBackupStore.cs:572` (creator); callers `:144`, `:237`, `:275` | blank backup ID or fingerprint (split); backup ID; backup plus deck IDs | `local_deck_backup_restore`; `local_deck_backup_delete`; card-list `deck-backup` source | `backupId`, `expectedDatabaseFingerprint`; `backupId`; `source.backupId` / `source.deckId` (and `left.`/`right.`) | lower plus wrapper prefix |
| `src/MtgMcp.Core/Decks/CategoryRuleSetValidator.cs:77` (creator); callers `:16`, `:31` | `invalid-category-rules` | category preview and apply | `source.ruleSet.assignmentMode` / `source.ruleSet.rules`; `source.ruleSet.rules[i]` | lower returns relative path, wrapper prefixes `source.ruleSet` |
| `src/MtgMcp.Statistics/ExactStatisticsCalculator.cs:264` (creator, 42 callers across `Castability`, `DeckSummary`, `Exact`, `Mulligan`, `Multivariate`, `TurnAndMana` calculators), `InverseCountCalculator.cs:342`, `:346`, `:368`, `:387`, `:405`, `StatisticsPopulation.cs:301` (creator, 8 callers) | `invalid-statistics-request`, `invalid-statistics-population` | `stats_hypergeometric`, `stats_multivariate`, `stats_turn_table`, `stats_mana_availability`, `stats_package_assembly`, `stats_mulligan`, `stats_minimum_count`, `stats_local_deck_summary`, `stats_castability` | each message already starts with the field (`request.drawCount`, `request.event.turn`, ...): pass it as the path | lower (the message prefix is the path; add a `path` parameter to the three creators) |

### 4c. Archidekt, Playgroup, Spellbook libraries (5 mapping sites plus the throw sites behind them)

| Site | Reason code | Tool(s) | Path | Who |
| --- | --- | --- | --- | --- |
| `src/MtgMcp.Archidekt/ArchidektOperationResults.cs:37` (kind `InvalidInput`), feeding 13 throw sites | see rows below | Archidekt tools | add `Path` to `ArchidektProviderException` | lower |
| `ArchidektContract.cs:59` | `invalid-visibility` | `archidekt_deck_create` | `visibility` | lower |
| `ArchidektDeckTransport.cs:385` | `unsupported-deck-format` | `archidekt_deck_create` | `format` | lower |
| `ArchidektDeckTransport.cs:72`, `:428` | `invalid-deck-list-cursor` (becomes `invalid-cursor`) | `archidekt_deck_list` | `cursor` | lower |
| `ArchidektDeckTransport.cs:401` | `invalid-page-size` | `archidekt_deck_list` | `pageSize` | lower |
| `ArchidektFolderOperations.cs:403` | `folder-not-found` (as `invalid-input`) | `archidekt_folder_create`, `archidekt_folder_update` | `parentFolderId` (move: `destinationFolderId`); consider `not-found` | lower |
| `ArchidektFolderOperations.cs:477`, `:489`, `:501` | `move-items-required`, `invalid-folder-item-kind`, `conflicting-folder-items` | `archidekt_folder_move_items` | `items`; `items[i].kind`; `items` | lower |
| `ArchidektFolderOperations.cs:546` | `invalid-folder-visibility` | folder create and update | `visibility` | lower |
| `ArchidektOperationResults.cs:82` (`RequireConfirmation`) | `confirmation-required` | `archidekt_deck_delete`, `archidekt_folder_delete`, `archidekt_snapshot_delete`, `archidekt_snapshot_restore_apply` | `confirmation` | lower |
| `ArchidektOperationResults.cs:62` | `invalid-archidekt-input` (an `ArgumentException`; the catch drops `ParamName`) | all Archidekt tools; 23 throw sites use `ArchidektContract.Required(value, nameof(value))` | `ArgumentException.ParamName` is `value` today: pass the real argument name at each of the 23 sites and use it as the path | lower |
| `ArchidektRequestPacer.cs:44`, `:61`, `:97` | `request-limit-exceeded` | `archidekt_push_apply`, previews, restore | no argument: reclassify | lower |
| `src/MtgMcp.Playgroup/PlaygroupService.cs:528` (maps exceptions), feeding `PlaygroupContract.cs:42` (`invalid-provider-id`, message names the parameter) and `:59` (`invalid-provider-input`) | per row | Playgroup tools | add `Path` to `PlaygroupProviderException`; use the parameter name (`playgroupId`, `playgroupDeckId`, `userId`, `commanderId`, `gameId`, `name`, `events[i].name`) | lower |
| `PlaygroupService.cs:108` | `invalid-elo-scope` | `playgroup_deck_elo_history_get` | `leagueId` | lower |
| `PlaygroupService.cs:199` | `invalid-page-boundary` (page and limit in one reason: split) | `playgroup_playgroup_games_list` | `cursor`, `pageSize` | lower |
| `PlaygroupService.cs:243` | `invalid-max-games` | `playgroup_games_stats_get` | `maxGames` | wrapper (`PlaygroupStatsTools.cs:142`) or lower |
| `PlaygroupService.cs:304`, `:341`, `:353`, `:588` | `invalid-event-batch`, `invalid-live-session`, `invalid-live-session-scope`, `invalid-player-id` | `playgroup_game_events_batch_create`, `playgroup_live_session_create` | `events`; `request` (or flat); `leagueId`; `events[i]` field | lower |
| `PlaygroupTransport.cs:218` | `provider-request-rejected` (HTTP 400 or 422) | Playgroup tools | no argument: reclassify as unavailable | lower |
| `src/MtgMcp.Spellbook/SpellbookService.cs:372` (maps exceptions), feeding `SpellbookContract.cs:99`, `:122`, `:140`, `:155`, `:165`, `:206`, `:215`, `:237`, `SpellbookService.cs:140`, `:197` | `invalid-source-query`, `invalid-variant-id`, `invalid-page-options` (limit and offset in one reason: split), deck-request reasons | `spellbook_variant_search`, `spellbook_variant_get`, `spellbook_deck_combos_find` | `sourceQuery`; `variantId`; `pageSize` and `cursor`; deck-request reasons are built from the local deck: `deckId` | lower |
| `SpellbookTransport.cs:165` | `source-request-rejected` (HTTP 400) | Spellbook tools | no argument: reclassify | lower |

### 4d. Scryfall library (45 sites; the PLC said about 35)

Cursor sites carry `cursor` (single argument after D-1; `printsCursor`/`rulingsCursor` collapse into `cursor`).

| Site(s) | Reason code | Tool(s) | Path | Who |
| --- | --- | --- | --- | --- |
| `src/MtgMcp.Scryfall/ScryfallService.cs:144` | `invalid-scryfall-query` | `scryfall_search` | `query` | lower |
| `ScryfallService.cs:157` | `invalid-scryfall-search-options` (one reason covers `unique`, `order`, `direction`: split) | `scryfall_search` | `unique` / `order` / `direction` | lower |
| `ScryfallService.cs:1517` | provider `invalid-scryfall-query` (Scryfall rejects the syntax) | `scryfall_search` | `query` | lower |
| `ScryfallService.cs:282`, `:309` | `invalid-scryfall-collection`, `fuzzy-collection-lookup-unsupported` | `scryfall_cards_get` | `lookups` | lower |
| `ScryfallService.cs:1908`, `:1924` | `invalid-card-lookup` | `scryfall_cards_get`, `scryfall_card_details` | `lookups[i]` (cards_get; needs the index), `lookup` (card_details) | lower returns index, wrapper prefixes |
| `ScryfallService.cs:1895`, `ScryfallDeckTagOperations.cs:73` | `invalid-freshness-policy` | every Scryfall read, category preview/apply, identity preview | `freshnessPolicy` | lower |
| `ScryfallFacade.cs:522` | `invalid-freshness-policy` ("Corpus sync policy must be default or refresh") | `scryfall_card_data_sync` | `metadataPolicy` today, `freshnessPolicy` after section 7 | lower |
| `ScryfallService.cs:748`, `:773`, `:790`, `:924` | `invalid-cursor` | `scryfall_cards_get` | `cursor` | lower |
| `ScryfallService.cs:1592` (search, prints), `:1635` (prints from installed data), `:1671`, `:1705` (rulings), `:1739` (sets), `:1772` (catalog, autocomplete), `:1362` (cards by tag) | `invalid-cursor` | `scryfall_search`, `scryfall_card_details`, `scryfall_reference_get`, `scryfall_autocomplete`, `scryfall_installed_cards_by_tag` | `cursor` | lower |
| `ScryfallCardDataStore.cs:887` | `invalid-cursor` | `scryfall_tags_search` | `cursor` | lower |
| `ScryfallSnapshotStore.cs:212`, `:273` | `invalid-cursor` | `scryfall_request_snapshots_get` | `cursor` | lower |
| `ScryfallService.cs:1120` | `invalid-scryfall-set` | `scryfall_reference_get` | `codeOrId` | lower |
| `ScryfallService.cs:1168` | `invalid-scryfall-catalog` | `scryfall_reference_get` | `catalogName` | lower |
| `ScryfallService.cs:1215` | `invalid-scryfall-autocomplete` | `scryfall_autocomplete` | `query` | lower |
| `ScryfallService.cs:1309` | `invalid-scryfall-tag-search` (query and `tagType` in one reason: split) | `scryfall_tags_search` | `query` / `tagType` | lower |
| `ScryfallService.cs:1336` | `invalid-scryfall-tag-selector` (`tagIdentity`, `tagType`, `minimumWeight`: split) | `scryfall_installed_cards_by_tag` | `tagIdentity` / `tagType` / `minimumWeight` | lower |
| `ScryfallCardDataStore.cs:1790`, `:1796` | `invalid-scryfall-tag-identity` (type; id or slug) | by-tag tool | `tagType`; `tagIdentity` | lower |
| `ScryfallCardDataStore.cs:631` | `invalid-scryfall-tag-identity` (no identity) | category preview and apply | `source.ruleSet.rules[i].selectorGroups` | lower plus wrapper prefix |
| `ScryfallCardDataStore.cs:696` | `invalid-card-lookup` ("Deck card lookups are required"): empty deck, a defect | category preview and apply | `deckId`; better: treat zero entries as an empty, successful evidence set | lower |
| `ScryfallFacade.cs:763` | `invalid-snapshot-time-range` | `scryfall_request_snapshots_get` | `retrievedAfterUtc` | lower |
| `ScryfallFacade.cs:806` | `invalid-snapshot-checksum` | `scryfall_request_snapshot_delete` | `expectedChecksum` | lower |
| `ScryfallCardDataStore.cs:1237`, `:1271`, `ScryfallSnapshotStore.cs:319` | `activation-change-not-acknowledged`, `data-loss-not-acknowledged`, `evidence-loss-not-acknowledged` (become `confirmation-required`, section 6) | rollback, card-data delete, request-snapshot delete | `confirmation` | lower |
| `ScryfallListQuery.cs:149` | `list-too-large` | `card_list_query`, `card_lists_compare`, `stats_castability`, `stats_simulate` | `source` (or `left` / `right`, `deck`): lower names the source index, wrapper maps it | wrapper |
| `ScryfallService.cs:492`, `:516` | `invalid-scryfall-evidence-binding` | `local_deck_identity_reconcile_apply` | `applyToken` | lower |
| `ScryfallService.cs:550`, `:559`, `:578` | `invalid-scryfall-collection`, `invalid-scryfall-lookup` (lookups the server builds from deck entries; internal invariants) | identity preview, deck entry resolution | none reachable from an argument: reclassify as unavailable, or `entryIds` | lower |
| `ScryfallService.cs:1074` | `scryfall-card-id-required` (internal) | `scryfall_card_details` | none: reclassify | lower |

### 4e. Test hook (REQ-007)

`ToolCallPolicy.Filter` (`src/MtgMcp.App/Hosting/ToolCallPolicy.cs:45`) is the one place every tool result passes;
the null-path hook sits there. `ToolCallPolicy.MarkOperationFailure` already inspects every non-success result.

## 5. Renames per tool (REQ-003, REQ-004, REQ-005, REQ-008, D-4)

Parameters (P) and result fields (R). Paging parameters from section 1 are not repeated except where they carry a
renamed fingerprint.

### Preview fingerprint (REQ-003)

| Tool | Old | New | File:line |
| --- | --- | --- | --- |
| `local_deck_import_preview` | P `expectedPreviewFingerprint` | removed (the cursor binds it) | `DeckInterchangeReadTools.cs:275` |
| `local_deck_import_preview` | R `fingerprint` | `previewFingerprint` | `DeckPreviewPaging.cs:85`, `src/MtgMcp.Core/Decks/DeckInterchangeModels.cs:114` |
| `local_deck_import_create` | P `expectedFingerprint` | `previewFingerprint` | `DeckInterchangeWriteTools.cs:54` |
| `local_deck_identity_reconcile_preview` | P `expectedPreviewFingerprint` | removed | `DeckIdentityReconciliation.cs:759` |
| `local_deck_category_rules_preview` | P `expectedPreviewFingerprint` | removed | `DeckCategorization.cs:317` |
| `archidekt_pull_preview`, `archidekt_push_preview`, `archidekt_snapshot_restore_preview` | P `expectedPreviewFingerprint` | removed | `ArchidektReadTools.cs:201`, `:249`, `:336` |
| `card_lists_compare`, `card_list_query` | R `fingerprint` (request fingerprint, not a preview) | owner call: `requestFingerprint` or exempt | `CardListModels.cs:78`, `:110` |
| apply tools | `previewFingerprint` already | no change | `ArchidektRequests.cs:42`, `:55`, `:156`; `DeckCategorization.cs:370`; `DeckIdentityReconciliation.cs:820` |

### Revision (REQ-005)

| Tool | Old | New | File:line |
| --- | --- | --- | --- |
| `archidekt_deck_create` | P `expectedLocalRevision` | `expectedRevision` | `ArchidektRemoteWriteTools.cs:45` |
| `archidekt_pull_apply` | request field `expectedLocalRevision` | `expectedRevision` | `ArchidektRequests.cs:40` |
| `archidekt_push_apply` | request field `expectedLocalRevision` | `expectedRevision` | `ArchidektRequests.cs:53` |
| `archidekt_pull_preview`, `archidekt_push_preview` | R `localRevision` | `revision` | `ArchidektReadResults.cs:367` |
| `archidekt_sync_diff` | R `localRevision` | `revision` | `ArchidektReadResults.cs:89` |
| Archidekt apply and sync results | R `localRevision` | `revision` | `src/MtgMcp.Archidekt/ArchidektModels.cs:415`, `:435`, `:520` |
| `local_deck_category_rules_preview` | R `deckRevision` | `revision` (a fourth name the PLC does not list) | `DeckCategorization.cs:29`, `DeckPreviewPaging.cs:140` |
| `local_deck_identity_reconcile_preview` | R `deckRevision` | `revision` | `DeckIdentityReconciliation.cs:46`, `DeckPreviewPaging.cs:14` |
| `local_deck_export_bundle` | R `deckRevision` | `revision` | `DeckExportPaging.cs:51`, `src/MtgMcp.Core/Decks/DeckInterchangeModels.cs:174` |
| import proposal | R `sourceRevision` | keep: it is provenance of the source deck, not the current revision | `DeckPreviewPaging.cs:48` |

### Deck and snapshot identifiers (REQ-008, D-4)

Local tools already use `deckId` for a local deck; stats population inputs use `deckId` and `expectedRevision`
(`StatisticsPopulationInputs.cs:29-31`, `:84-86`).

| Tool | Old | New | File:line |
| --- | --- | --- | --- |
| `archidekt_deck_get` | P `deckId` (Archidekt deck) | `remoteDeckId` | `ArchidektReadTools.cs:110` |
| `archidekt_snapshot_restore_preview` | P `deckId` | `remoteDeckId` | `ArchidektReadTools.cs:330` |
| `archidekt_snapshots_get` | P `deckId` | `remoteDeckId` | `ArchidektReadTools.cs:430` |
| `archidekt_deck_delete`, `archidekt_snapshot_create`, `archidekt_snapshot_update`, `archidekt_snapshot_delete`, `archidekt_snapshot_restore_apply` | request field `deckId` | `remoteDeckId` | `ArchidektRequests.cs:26`, `:121`, `:131`, `:140`, `:150` |
| `archidekt_snapshots_get`, restore preview | R `deckId` | `remoteDeckId` (proposed: results should match; the PLC names parameters only) | `ArchidektReadResults.cs:119`, `ArchidektModels.cs:206`, `:594` |
| `archidekt_deck_get`, `archidekt_deck_list` | R `remoteId` | `remoteDeckId` (proposed) | `ArchidektReadResults.cs:292`, `ArchidektModels.cs:29`, `:93` |
| card-list sources `archidekt-deck`, `archidekt-snapshot` | `archidektDeckId` | `remoteDeckId` | `src/MtgMcp.App/CardLists/CardListSourceInput.cs` (`ArchidektDeckSourceInput`, `ArchidektSnapshotSourceInput`) |
| `playgroup_deck_get` | P `deckId` | `playgroupDeckId` | `PlaygroupReadTools.cs:84` |
| `playgroup_deck_elo_history_get` | P `deckId` | `playgroupDeckId` | `PlaygroupReadTools.cs:98` |
| `scryfall_request_snapshots_get` | P `snapshotId` | `requestSnapshotId` | `ScryfallReferenceTools.cs:185` |
| `scryfall_request_snapshot_delete` | P `snapshotId` | `requestSnapshotId` | `ScryfallWriteTools.cs:119` |
| card-list source `scryfall-snapshot` | `snapshotId` | `requestSnapshotId` | `CardListSourceInput.cs:26` |
| Scryfall results | R `snapshotId` (reference, summary, delete result, list cards) | `requestSnapshotId` | `src/MtgMcp.Scryfall/ScryfallModels.cs:228`, `:397`, `:425`, `ScryfallListCards.cs:30`, `src/MtgMcp.Core/Evidence/EvidenceDescriptor.cs:45`, `:99` |
| `archidekt_snapshots_get`, `archidekt_snapshot_update`, `archidekt_snapshot_delete`, `archidekt_snapshot_restore_*` | `snapshotId` (Archidekt) | keep | `ArchidektReadTools.cs:332`, `:432`; `ArchidektRequests.cs:132`, `:141`, `:151` |
| `card_lists_*` source info | R `snapshotId` (a source label for an Archidekt snapshot) | keep (Archidekt); confirm at `CardListResolver.cs:79-81` | |

### Flat Archidekt arguments (REQ-004), field by field

After the change each tool takes these arguments directly (new names; `*` marks a rename above).

| Tool | Flat arguments (required first) | Source of the fields |
| --- | --- | --- |
| `archidekt_deck_create` | `name`, `format`, `description?`, `visibility?` (private), `parentFolderId?`, `localDeckId?`, `expectedRevision?`* | `ArchidektRequests.cs:9-20` plus tool parameters `ArchidektRemoteWriteTools.cs:44-45` |
| `archidekt_deck_delete` | `remoteDeckId`*, `expectedRemoteFingerprint`, `confirmation` (`delete deck <remoteDeckId>`) | `ArchidektRequests.cs:25-29` |
| `archidekt_pull_apply` | `remoteDeckId`, `localDeckId?`, `expectedRevision?`*, `expectedRemoteFingerprint`, `previewFingerprint`, `discardLocalChanges?` | `ArchidektRequests.cs:35-44` |
| `archidekt_push_apply` | `localDeckId`, `expectedRevision`*, `expectedRemoteFingerprint`, `previewFingerprint`, `confirmRemovals?` | `ArchidektRequests.cs:51-57` |
| `archidekt_folder_create` | `name`, `visibility`, `parentFolderId?` | `ArchidektRequests.cs:62-65` |
| `archidekt_folder_update` | `folderId`, `expectedTreeFingerprint`, `name?`, `visibility?`, `parentFolderId?`, `updateParent?` | `ArchidektRequests.cs:70-79` |
| `archidekt_folder_move_items` | `expectedTreeFingerprint`, `items` (kept nested: on the D-3 allowlist; each `{kind, id, expectedParentFolderId}`), `destinationFolderId` | `ArchidektRequests.cs:84-105` |
| `archidekt_folder_delete` | `folderId`, `expectedName`, `expectedTreeFingerprint`, `confirmation` (`delete folder <folderId>`) | `ArchidektRequests.cs:110-115` |
| `archidekt_snapshot_create` | `remoteDeckId`*, `expectedRemoteFingerprint`, `name`, `description?` | `ArchidektRequests.cs:120-125` |
| `archidekt_snapshot_update` | `remoteDeckId`*, `snapshotId`, `expectedChecksum`, `name` | `ArchidektRequests.cs:130-134` |
| `archidekt_snapshot_delete` | `remoteDeckId`*, `snapshotId`, `expectedChecksum`, `confirmation` (`delete snapshot <snapshotId>`) | `ArchidektRequests.cs:139-144` |
| `archidekt_snapshot_restore_apply` | `remoteDeckId`*, `snapshotId`, `expectedSnapshotChecksum`, `expectedSnapshotContentFingerprint`, `expectedRemoteFingerprint`, `previewFingerprint`, `confirmation` (`restore snapshot <snapshotId>`) | `ArchidektRequests.cs:149-158` |
| `playgroup_live_session_create` (not Archidekt; owner call) | `playerAmount?` (4), `lifeAmount?` (40), `bracket?`, `playgroupId?`, `leagueId?`, `discoverable?`, `languageIds?`, `clientIdentifier?` | `PlaygroupModels.cs:52-68` |

No two flat names collide inside a tool. The request records and their `[Description]` text move onto the tool
parameters (`ToolInputDescriptionTests` requires a description on every field). The coordinator facade methods that take
the request records (`ArchidektCoordinatorFacade.cs:65`, `:82`, `:92`, `:129`, `:169`, `:194`; `ArchidektCoordinator.cs:333`,
`:358`, `:785`, `:1041`) change with them.

### Spellbook paging inputs (REQ-001, REQ-008)

| Tool | Old | New | File:line |
| --- | --- | --- | --- |
| `spellbook_variant_search`, `spellbook_deck_combos_find` | P `limit` | `pageSize` | `SpellbookReadTools.cs:43`, `:82` |
| same | P `offset` | `cursor` (opaque; ends at provider offset 1,000) | `SpellbookReadTools.cs:44`, `:83` |
| same | R `request.page.limit`, `request.page.offset` (echo) | `request.pageSize`, `request.cursor`, or drop the echo; add top-level `nextCursor` | `SpellbookPageRequest.cs`, `SpellbookRequestDetails.cs` |
| adapter | record `SpellbookPageOptions(Limit, Offset, GroupByCombo)` | replace with page size plus offset plus `GroupByCombo` fields on the request records | `SpellbookPageOptions.cs:6`, `SpellbookVariantSearchRequest.cs`, `SpellbookDeckComboRequest.cs:9` |

### Other paging renames

| Tool | Old | New |
| --- | --- | --- |
| `local_deck_import_preview`, identity preview, category preview, `local_deck_export_bundle`, `archidekt_deck_get`, `archidekt_folders_get`, `archidekt_snapshots_get` | `offset`; `nextOffset` | `cursor`; `nextCursor` |
| Archidekt sync diff and previews | `operationOffset`, `differenceOffset`; `nextOperationOffset`, `nextDifferenceOffset` | `cursor`; `nextCursor` |
| `scryfall_card_details` | `printsCursor`, `rulingsCursor`; `prints.nextCursor`, `rulings.nextCursor` | `cursor`; top-level `nextCursor` |
| `card_list_query` | `rows.pageSize`; `rows.nextCursor` | `pageSize`; `nextCursor` (owner call) |
| `playgroup_playgroup_games_list` | `page`, `limit` | `cursor`, `pageSize` |
| `local_deck_export_bundle` | `chunk.nextOffset` | `nextCursor` (top level or `chunk.nextCursor`: decide in phase 1) |

## 6. Destructive operations (D-5)

All 17 tools carry `Destructive = true`. D-5 covers an operation that "destroys or replaces data with no local undo".

| Tool | What it destroys or replaces | Local undo | Guard today | New guard |
| --- | --- | --- | --- | --- |
| `archidekt_deck_delete` | one remote deck | none (remote) | `confirmation` `delete <id>` plus `expectedRemoteFingerprint` (`ArchidektDeckOperations.cs:225`) | `confirmation` = `delete deck <remoteDeckId>` |
| `archidekt_folder_delete` | one empty remote folder | none | `confirmation` `delete folder <folderId>` plus tree fingerprint (`ArchidektFolderOperations.cs:264-266`) | unchanged: `delete folder <folderId>` |
| `archidekt_snapshot_delete` | one remote named snapshot | none | `confirmation` `delete snapshot <snapshotId>` plus checksum (`ArchidektSnapshotOperations.cs:163-165`) | unchanged: `delete snapshot <snapshotId>` |
| `archidekt_snapshot_restore_apply` | replaces the remote deck's contents | none | `confirmation` `restore snapshot <snapshotId>` plus four fingerprints (`ArchidektSnapshotOperations.cs:253-255`) | unchanged: `restore snapshot <snapshotId>` |
| `scryfall_card_data_delete` | all installed card data, active and previous generation (`ScryfallCardDataStore.cs:1262-1307`) | none (a new sync reinstalls) | `acknowledgeDataLoss: bool` plus `expectedActiveGeneration` | `confirmation` = `delete card-data <expectedActiveGeneration>` |
| `scryfall_request_snapshot_delete` | one saved Scryfall request snapshot | none | `acknowledgeDataLoss: bool` plus `expectedChecksum` (`ScryfallSnapshotStore.cs:314-319`) | `confirmation` = `delete request-snapshot <requestSnapshotId>` |
| `scryfall_card_data_rollback` | swaps active and previous generation | yes: roll back again swaps them back | `acknowledgeActivationChange: bool` plus both generation guards | the PLC lists the Scryfall `acknowledge*` booleans becoming phrases: `confirmation` = `rollback card-data <expectedPreviousGeneration>` (the generation that becomes active) |
| `scryfall_card_data_sync` | activates a new generation and deletes the older previous generation (`ScryfallCardDataStore.cs:1157-1160`) | partial (rollback reaches the one kept previous) | none (`expectedActiveGeneration?` only) | **PLC mismatch**: D-5 says "an activation change on sync" but sync takes no acknowledgement. Recommended: no phrase on sync (it is the install path and has an empty-state first run with no id to bind). The activation-change boolean is the rollback tool's. Owner to confirm. |
| `local_deck_backup_delete` | one backup | none: the backup is the undo | **none** (only `backupId`; `DeckBackupStore.cs:231-244`) | D-5 text says no phrase; recommended: owner decides, since this is the one local delete with no guard and no undo. A phrase would be `delete backup <backupId>`. |
| `local_deck_backup_restore` | replaces `decks.db` | yes: a rollback backup is made first and returned (`DeckBackupStore.cs:192-194`, `DeckRestoreResult`) | `expectedDatabaseFingerprint` | no phrase (as D-5) |
| `local_deck_delete` | one deck and its rows | only if a backup exists | `expectedRevision` (`DeckWriteTools.cs:107`) | no phrase (as D-5) |
| `archidekt_pull_apply` | replaces a local deck; with `discardLocalChanges: true` discards local edits | none for the discarded edits | `discardLocalChanges: bool` must equal the preview's, plus fingerprints (`pull-discard-option-mismatch`, `ArchidektCoordinator.cs:346`) | not listed in D-5. A boolean option bound to the preview, not an acknowledgement; owner to confirm it stays. |
| `archidekt_push_apply` | rewrites the remote deck | none | `confirmRemovals` count above 10 entries or 10 percent | stays a count (as D-5) |
| `archidekt_folder_move_items` | moves decks and folders | yes: move back | tree fingerprints | none |
| `local_deck_apply_changes`, `local_deck_category_rules_apply`, `local_deck_identity_reconcile_apply` | edit one deck | yes: apply the inverse, or restore a backup | `expectedRevision` (plus preview guards) | none |
| `playgroup_game_events_batch_create` | remote write, exactly once | none | tag `requiresUserInteraction` only | not a delete or replace; none |

Phrases bind a target: a caller cannot confirm another id. Exact new phrases: `delete deck <remoteDeckId>`,
`delete folder <folderId>`, `delete snapshot <snapshotId>`, `restore snapshot <snapshotId>`,
`delete card-data <expectedActiveGeneration>`, `delete request-snapshot <requestSnapshotId>`,
`rollback card-data <expectedPreviousGeneration>`. New reason code for every wrong or missing phrase:
`confirmation-required`, path `confirmation`, message showing the exact phrase (existing at
`ArchidektOperationResults.cs:77-85`; move it to Core). `confirmRemovals` stays an `int?` count
(`ArchidektRemovalGuard.cs:65-80`).

## 7. `metadataPolicy` versus `freshnessPolicy`, and the two generation guards

### `metadataPolicy` and `freshnessPolicy`

Same meaning. Verdict: rename `metadataPolicy` to `freshnessPolicy`.

- `metadataPolicy` is on `scryfall_card_data_sync` only (`src/MtgMcp.App/Scryfall/ScryfallWriteTools.cs:50-51`;
  values `default`, `refresh`, the description says "Metadata cache policy").
- `SyncUnderLeaseAsync` passes it straight to `cards.GetBulkMetadataAsync(metadataPolicy == "refresh" ? "refresh" :
  "default", ...)` (`src/MtgMcp.Scryfall/ScryfallFacade.cs:606-609`). `GetBulkMetadataAsync` is the same method
  `scryfall_reference_get` kind bulk calls with its `freshnessPolicy` (`ScryfallService.cs:1256`). So it is the
  `freshnessPolicy` (default, cache-only, refresh) of the bulk-metadata request.
- The rejection already uses the reason code `invalid-freshness-policy` (`ScryfallFacade.cs:520-524`), but with a
  second validation (`default` or `refresh` only).
- Difference: `cache-only` is not offered on sync, correctly: a sync with no network cannot install anything.
  After D-2 ("a tool publishes only the values it has"), sync publishes `default`, `refresh` and the shared
  `FreshnessPolicyValues` attribute (`AllowedToolValues.cs:9-10`) needs a second, two-value attribute.

### `expectedCorpusGeneration` and `expectedActiveGeneration`

Same meaning. Verdict: rename `expectedCorpusGeneration` to `expectedActiveGeneration`.

- `expectedActiveGeneration` is the Scryfall `active_generation_id` (`ScryfallCardDataStore.cs:1240-1248`, `:1273-1282`,
  `ScryfallFacade.cs:600-604`) and its mismatch code is `stale-scryfall-generation`.
- `expectedCorpusGeneration` on category apply (`DeckCategorization.cs:77`, `:103`, tool at `:364`) is compared with
  `preview.Data.CorpusGenerationId`, which `ResolveTagIdentitiesAsync` fills from `active_generation_id`
  (`ScryfallCardDataStore.cs:642-683`, returns `ScryfallTagResolution(generationId, ...)`). It is the active
  generation at preview time, so the same Guid.
- Two differences, not of meaning: `expectedCorpusGeneration` is nullable (null when no tag selector needed card data),
  and a mismatch returns `category-corpus-conflict` (`DeckCategorization.cs:105`), not `stale-scryfall-generation`;
  section 8 aligns the code.
- Result fields named `corpusGenerationId` (`DeckCategorization.cs:36`, `DeckIdentityReconciliation.cs:38`,
  `DeckPreviewPaging.cs:149`, and the Scryfall models at `ScryfallModels.cs:264`, `:283`, `:289`, `:306`, `:314`,
  `ScryfallCardResponses.cs:92`, `:111`) name the generation that served the evidence; they are not guards. The
  PLC does not list them; the new parameter takes their value. Keep.

## 8. Change codes (REQ-005, REQ-006)

Every site of every code in the PLC table, with tool and end state. `current` below means `ConflictState`
(`revision`, `remoteFingerprint`); it needs a `previewFingerprint` field for the new codes (finding 7).

| Code | Site (file:line) | Tool(s) | End state |
| --- | --- | --- | --- |
| `stale-deck-revision` | `src/MtgMcp.Decks/SqliteDeckStore.cs:913` | `local_deck_apply_changes`, `local_deck_delete`, the writes inside category apply, identity apply, `archidekt_pull_apply` (replace) | kept: the one revision code, `current.revision` |
| `deck-revision-conflict` | `src/MtgMcp.App/CardLists/CardListResolver.cs:653` | `card_list_query`, `card_lists_compare`, `stats_castability`, `stats_simulate` (deck sources) | `stale-deck-revision` |
| | `src/MtgMcp.App/Decks/DeckCategorization.cs:149` | `local_deck_category_rules_preview` | `stale-deck-revision` |
| | `src/MtgMcp.App/Decks/DeckDuplicatesCoordinator.cs:87` | `local_deck_duplicates_preview` | `stale-deck-revision` |
| | `src/MtgMcp.App/Decks/DeckIdentityReconciliation.cs:242` | `local_deck_identity_reconcile_preview` | `stale-deck-revision` |
| | `DeckIdentityReconciliation.cs:309` | `local_deck_identity_reconcile_apply` | `stale-deck-revision` |
| | `src/MtgMcp.App/Spellbook/SpellbookDeckInputResolver.cs:69` | `spellbook_deck_combos_find`, `spellbook_deck_bracket_facts_get` | `stale-deck-revision` |
| | `src/MtgMcp.App/Statistics/StatisticsDeckResolver.cs:110` | the seven `stats_*` population tools, `stats_local_deck_summary` | `stale-deck-revision` |
| `local-deck-changed` | `src/MtgMcp.App/Archidekt/ArchidektCoordinator.cs:1058` | `archidekt_deck_create` | `stale-deck-revision` |
| `pull-preview-changed` | `ArchidektCoordinator.cs:399` (one check covers remote fingerprint, preview fingerprint, and `expectedLocalRevision`) | `archidekt_pull_apply` | split: revision to `stale-deck-revision` (checked first), `previewFingerprint` to `preview-changed`, remote fingerprint to `remote-deck-changed` (finding 12) |
| `push-preview-changed` | `ArchidektCoordinator.cs:735` (called from `:773-780` revision or remote fingerprint, and `:832` preview fingerprint) | `archidekt_push_apply` | split: revision to `stale-deck-revision`, `previewFingerprint` to `preview-changed`, remote fingerprint to `remote-deck-changed` |
| | `src/MtgMcp.Archidekt/ArchidektDeckOperations.cs:464` (plan fingerprint) | `archidekt_push_apply` | `preview-changed` |
| `restore-preview-changed` | `src/MtgMcp.Archidekt/ArchidektSnapshotOperations.cs:279` | `archidekt_snapshot_restore_apply` | `preview-changed` |
| `import-preview-changed` | `src/MtgMcp.Decks/DeckInterchangeService.cs:173` | `local_deck_import_create` | `preview-changed` |
| `category-preview-mismatch` | `DeckCategorization.cs:100` | `local_deck_category_rules_apply` | `preview-changed` |
| `identity-preview-mismatch` | `DeckIdentityReconciliation.cs:346` (an `invalid-input` today) | `local_deck_identity_reconcile_apply` | `preview-changed` (a conflict) |
| `preview-changed` (page) | `src/MtgMcp.App/Hosting/ResultPage.cs:140`, reached from `DeckPreviewPaging.cs:198`, `:242`, `:344` and `ArchidektResultPaging.cs` (`PagePreview`, `PageRestorePreview`) | import, identity, category previews; pull, push, restore previews | `cursor-stale` |
| `source-changed-between-pages` | `src/MtgMcp.App/CardLists/CardListCursor.cs:102` | `card_list_query`, `card_lists_compare` | `cursor-stale` |
| `deck-changed-between-pages` | `src/MtgMcp.App/Decks/DeckCardViewCoordinator.cs:473` | `local_deck_get` | `cursor-stale` |
| `deck-list-changed` | `src/MtgMcp.Archidekt/ArchidektDeckTransport.cs:65` | `archidekt_deck_list` | `cursor-stale` |
| `category-corpus-conflict` | `DeckCategorization.cs:105` | `local_deck_category_rules_apply` | `stale-scryfall-generation` (existing at `ScryfallCardDataStore.cs:1140`, `:1247`, `:1281`, `ScryfallFacade.cs:603`) |
| `remote-deck-changed` | `ArchidektCoordinator.cs:419`; `ArchidektDeckOperations.cs:234`, `:458`; `ArchidektSnapshotOperations.cs:89`, `:272` | pull apply; deck delete; push apply; snapshot create; restore apply | kept |
| `snapshot-changed` | `ArchidektSnapshotOperations.cs:130`, `:174`, `:264` | snapshot update, snapshot delete, restore apply | kept |
| `snapshot-content-changed` | `ArchidektSnapshotOperations.cs:268` | restore apply | kept |
| `folder-tree-changed` | `src/MtgMcp.Archidekt/ArchidektFolderOperations.cs:121`, `:187`, `:275` | folder update, move items, delete | kept |
| `pull-discard-option-mismatch` | `ArchidektCoordinator.cs:346` (an `invalid-input`, path `discardLocalChanges`) | `archidekt_pull_apply` | kept |

Codes the PLC table does not mention but that touch the same causes: `stale-database-fingerprint`
(`DeckBackupStore.cs:164`, backup restore, a database fingerprint), `pull-conflict` and `push-conflict`
(`ArchidektCoordinator.cs:386`, `:802`, a conflict with the sync baseline), `binding-remote-mismatch`
(`ArchidektCoordinator.cs:241`). They are not in REQ-006's removal list; leave them.

The REQ-006 source test must reject these literals in `src/`: `deck-revision-conflict`, `local-deck-changed`,
`pull-preview-changed`, `push-preview-changed`, `restore-preview-changed`, `import-preview-changed`,
`category-preview-mismatch`, `identity-preview-mismatch`, `source-changed-between-pages`,
`deck-changed-between-pages`, `deck-list-changed`, `category-corpus-conflict` (12 literals at 19 sites; the page
`preview-changed` at `ResultPage.cs:140` is a 20th site whose code becomes `cursor-stale`, and `preview-changed` stays as
the apply code).

## 9. Tests and docs that need updating, per family

Counts of files come from a token search of `tests/` for the old names; the lists are the files to open first, not a
promise that each changes.

### Cross-cutting (phase 1; every family depends on them)

- `tests/MtgMcp.App.Tests/ToolNamingRuleTests.cs`, `ToolSurfaceRulesTests.cs`, `ToolSchemaSurfaceTests.cs`,
  `ToolInputDescriptionTests.cs`, `ToolOutputSchemaTests.cs`, `ToolAllowedValuesTests.cs`, `ToolResultBudgetTests.cs`,
  `ToolPagingTests.cs`, `ToolCallPolicyTests.cs`, `ToolBudgetScript.cs`, `ToolBudgetWorld.cs`, `RegisteredToolSet.cs`.
- `tests/MtgMcp.Architecture.Tests/ToolSurfaceNamingTests.cs`, `FoundationArchitectureTests.cs`.
- `tests/MtgMcp.E2E.Tests/FoundationMcpTests.cs` (the E2E schema test), `ToolInputBindingMcpTests.cs`,
  `ToolProtocolMcpTests.cs`, `LiveMethodAcceptanceTests.cs` (101 matches), `LiveAcceptanceSupport.cs`.
- New: the paging surface test, the removed-codes source test, the null-path hook, the D-3 allowlist test, and a
  default-page-fits test per tool (section 2).
- Docs: `README.md` (26 matching lines), `CHANGELOG.md` (25), `docs/output-control.md` (18),
  `docs/architecture/mcp-surface.md` (21), `docs/architecture/deck-model.md` (1; the PLC says `docs/deck-model.md`),
  `docs/architecture/README.md` (1), `docs/adapters.md` (3), `docs/toolsets.md` (1), `docs/versioning.md` (new
  0.15.0 exception beside `:30` and `:43`), `llms.txt`, `.codex/` playbooks, `src/MtgMcp.App/Hosting/ServerInstructions.cs`,
  `src/MtgMcp.App/Prompts/WorkflowPrompts.cs`, `src/MtgMcp.App/Decks/DeckToolExamples.cs`,
  `src/MtgMcp.App/CardLists/CardListTools.cs` examples (9 matching lines),
  `src/MtgMcp.App/Statistics/StatisticsToolExamples.cs` (8). Tests that pin them: `ReadmeExampleTests.cs`,
  `ToolFactsDescriptionTests.cs`, `ServerInstructionsTests.cs`, `WorkflowPromptTests.cs`, `DeckToolExampleTests.cs`,
  `StatisticsToolExampleTests.cs`, `CardListExampleSupport.cs`.

### Family a: local deck, interchange, backups

- App tests: `DeckCardViewTests.cs`, `DeckImportPreviewPagingTests.cs`, `DeckImportHeaderTests.cs`,
  `DeckExportPagingTests.cs`, `DeckInterchangeToolsTests.cs`, `DeckIdentityReconciliationTests.cs`,
  `DeckCategorizationTests.cs`, `DeckCategoryRuleSourceTests.cs`, `DeckCategorizationSourceFixture.cs`,
  `DeckDuplicatesTests.cs`, `DeckBatchWriteToolsTests.cs`, `DeckWriteToolsTests.cs`, `DeckWriteSummaryTests.cs`,
  `DeckApplySummaryToolTests.cs`, `DeckPatchEntriesToolTests.cs`, `DeckEditWorkflowScenarioTests.cs`,
  `AddByNameScenarioTests.cs`, `ChangeListArgumentErrorTests.cs`.
- Library tests: `tests/MtgMcp.Decks.Tests/SqliteDeckStoreTests.cs`, `DeckInterchangeServiceTests.cs`,
  `DeckBackupStoreTests.cs`, `DeckBackupReadTests.cs`; `tests/MtgMcp.Core.Tests/DeckCategorizationTests.cs`,
  `DeckInterchangeContractTests.cs`.
- E2E: `DeckMcpTests.cs`, `DeckInterchangeMcpTests.cs`, `ManualInterchangeAcceptanceTests.cs`,
  `CardListAndRuleToolsMcpTests.cs`.
- Scenario test (REQ-010): import, category, and identity workflows with only `cursor`, `pageSize`,
  `previewFingerprint`, `expectedRevision`.

### Family b: Archidekt

- App tests: `ArchidektWorkflowTests.cs` (105 matches), `ArchidektWorkflowTests.PreviewOptions.cs` (19),
  `ArchidektWorkflowTests.PartialPush.cs` (16), `ArchidektCoordinatorTests.cs` (45), `ArchidektPagingToolTests.cs`,
  `ArchidektTwoListPagingTests.cs`, `ArchidektDeckReadTests.cs`, `ArchidektKindToolTests.cs`,
  `ArchidektFakeContractTests.cs`, `ArchidektSyncNoticesTests.cs`, `ArchidektSyncLiveTests.cs`, `FakeArchidekt.cs`.
- Library tests: `tests/MtgMcp.Archidekt.Tests/ArchidektServiceTests.cs` (25), `ArchidektLiveTests.cs` (19),
  `ArchidektRecordedTests.cs`, `ArchidektNamedOwnerTests.cs`, `ArchidektConciseDifferenceTests.cs`,
  `ArchidektAllowedValuesTests.cs`, `ArchidektChangedOutsidePushTests.cs`.
- Wrapper to add or move: Archidekt request records in `src/MtgMcp.Archidekt/ArchidektRequests.cs` (delete after
  flattening); `ArchidektCoordinatorFacade.cs`.

### Family c: Scryfall and card lists

- App tests: `ScryfallToolTests.cs`, `ScryfallResponseFormatToolTests.cs`, `ScryfallReferenceToolTests.cs`,
  `CardListCompareTests.cs`, `CardListQueryTests.cs`, `CardListWorld.cs`, `DeckRuleToolTests.cs`.
- Library tests: `tests/MtgMcp.Scryfall.Tests/ScryfallServiceTests.cs` (20), `ScryfallStoreTests.cs` (8),
  `ScryfallCoordinationTests.cs` (7), `ScryfallCardFormatTests.cs` (golden files; row sizes feed section 2),
  `ScryfallDeckTagOperationsTests.cs`, `ScryfallRecordedTests.cs`, `ScryfallLiveTests.cs`.
- E2E: `ScryfallMcpTests.cs` (13), `CardListAndRuleToolsMcpTests.cs`, `RedWhiteWeeniesLiveMcpTests.cs`.

### Family d: Spellbook, Playgroup, statistics

- App tests: `SpellbookToolTests.cs`, `SpellbookBracketToolTests.cs`, `PlaygroupToolTests.cs`,
  `PlaygroupStatsToolTests.cs`, `StatisticsDeckResolverTests.cs`, `StatisticsCastabilityTests.cs`,
  `StatisticsSimulateTests.cs`, `StatisticsSimulatePresetTests.cs` (14), `StatisticsSimulateBindingTests.cs`.
- Library tests: `tests/MtgMcp.Spellbook.Tests/SpellbookServiceTests.cs`, `SpellbookContractTests.cs`,
  `SpellbookLiveTests.cs`; `tests/MtgMcp.Playgroup.Tests/PlaygroupServiceTests.cs`, `PlaygroupContractTests.cs`,
  `PlaygroupLiveTests.cs`; `tests/MtgMcp.Statistics.Tests/` (calculator tests that assert `invalid-statistics-request`
  messages).
- E2E: `SpellbookMcpTests.cs`, `StatisticsMcpTests.cs`, `StatisticsSimulateMcpTests.cs`.
- New tests the PLC names: Spellbook cap at offset 1,000, a Playgroup total that is an exact multiple of the page size.
