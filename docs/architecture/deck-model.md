# Deck Model

This page describes how a local deck is stored, how it relates to Archidekt, and how a change batch works. It
describes the code as it is now. Core holds the types (`src/MtgMcp.Core/Decks/`). `MtgMcp.Decks` holds the
SQLite store (`decks.db`). App holds the tools (`src/MtgMcp.App/Decks/`).

## Deck, entries, and categories

- A deck (`DeckDocument`) has an ID, a name, a description, a format label, a revision, timestamps, entries,
  categories, category assignments, and provider bindings. The format is a label stored lower-cased. The server does not
  enforce format legality.
- An entry (`DeckEntry`) is one row: `entryId`, `quantity`, `cardName`, optional `oracleId` and `printingId`,
  optional `setCode` and `collectorNumber`, `language`, `finish`, `zone`, and `sortOrder`. Quantity must be
  positive. Two entries can hold the same card; they are separate rows.
- An entry draft (`DeckEntryDraft`) is an entry without a required ID. `add-entry`, `local_deck_create`, imports, and
  the Archidekt pull all use it. It can carry `categoryIds`, `primaryCategoryId`, and `zoneCategories`
  (`src/MtgMcp.Core/Decks/DeckModels.cs`). The store assigns them in the same transaction right after it inserts the
  entry (`src/MtgMcp.Decks/DeckDraftCategories.cs`). The IDs name categories the deck already has or an earlier change
  of the same batch added (a `categoryDraft` with its own `categoryId`, or the `categories` of `local_deck_create`); an
  unknown one refuses the batch with `invalid-deck-change`, the same code a patch uses. Draft and patch zone-category refusals share that code, end "Nothing was written.", and name the caller's change number. The primary category is assigned as primary even when
  `categoryIds` does not list it. `DeckContractValidator.Normalize` keeps these fields. Imports build proposals, not
  drafts, and the Archidekt pull leaves the fields unset, so neither assigns a category this way.
- A category (`DeckCategory`) has an ID, a name, an optional color, and a sort order. A draft may carry its own
  `categoryId`.
- A category assignment links one entry to one category and says whether it is the entry's primary category. An
  entry has at most one primary category. The store keeps this true: a new primary assignment makes the
  previous primary non-primary, and `local_deck_validate` reports `multiple-primary-categories` for a stored
  deck that breaks it.
- Removing an entry or a category removes its assignments (database foreign keys with `ON DELETE CASCADE`,
  `src/MtgMcp.Decks/DeckDatabase.cs`).
- A provider binding links a local deck to one remote deck. It holds the provider, the remote ID, and a sync
  baseline used for pull and push.
- A deck revision starts at 1 and rises by one for each successful write batch.

## Zones

- A zone is a lower-cased label on an entry. The store accepts any non-blank zone
  (`src/MtgMcp.Decks/DeckContractValidator.cs`, `DeckValidator.cs`).
- The zones the tools know are `commander`, `main`, `sideboard`, and `maybeboard`
  (`src/MtgMcp.App/Decks/DeckZoneCounts.cs`). `cardsByZone` totals use that order. The default zone of a draft is
  `main`.
- Tools that read a deck by zone take zone names: `zones` on card-list deck sources, `zoneNames` in statistics
  selectors. `stats_simulate` resolves the caller's groups to main-zone entries.
- A zone is local state. Archidekt has no zones. Archidekt tells a deck's zones apart only by category names
  (next section).
- `local_deck_get` filters entries by `zones` (a known zone code or the call is refused as `invalid-zones`),
  `categoryNames` (1 to 100 names, ignoring case and surrounding space, against primary and non-primary categories), and
  `cardNames`. Values inside one filter combine with OR; the filters combine with AND. The values that match no entry of
  the whole deck come back in `zonesNotFound`, `categoryNamesNotFound`, and `cardNamesNotFound`, and no match is a normal
  result with no entries. Deck-wide totals ignore the filters. The cursor covers every filter
  (`src/MtgMcp.App/Decks/DeckCardViewCoordinator.cs`; see [Output control](../output-control.md)).
- `card_list_query` returns the stored entry ID as the row field `entryId` for local deck and deck backup sources, so a
  card-property query gives the IDs a patch or removal needs. Other sources refuse it as `invalid-field`
  (`src/MtgMcp.App/CardLists/CardListCoordinator.cs`). Writes stay keyed by explicit entry IDs; no change targets a
  filter.

## Zone categories

`DeckZoneCategories` (`src/MtgMcp.Core/Decks/DeckZoneCategories.cs`) is the one definition of the categories that
stand for a zone: `Commander` for zone `commander`, `Sideboard` for `sideboard`, and `Maybeboard` for `maybeboard`.
Zone `main` has none. Names match ignoring case and surrounding space. The Archidekt adapter reads the same
definition for its pull mapping, so the local and Archidekt rules cannot drift. Archidekt-only rules (premier
categories, "included in deck") stay in the adapter.

A zone and its zone category are two settings. The local store keeps them separate, because a deck from an import
or another format has zones and no categories. A caller who sets a zone chooses how the categories follow with
`zoneCategories`:

| Value | What happens |
| --- | --- |
| `match` | A non-main zone gives each entry the zone's category as its primary category. The category is created in the same batch when the deck lacks it. The entry's other zone categories are removed, and its other categories stay as non-primary. For zone `main`, every zone category is removed; if one was primary, the change must also give `primaryCategoryId`, or the entry must have no other category, or the change is refused. |
| `leave` | Only the zone changes; no category is touched. |

Where it applies:

- `patch-entries` requires `zoneCategories` whenever it sets `zone`, so the caller always chooses
  (`PatchDeckEntriesRules.FirstProblem`). The server never picks a mode. Giving `zoneCategories` without `zone` is
  refused. `match` for a zone with no zone category (anything besides `commander`, `sideboard`, `maybeboard`, and `main`)
  is refused; use `leave`. `match` with `primaryCategoryId` for a zone that has a zone category is refused, because the
  zone category becomes primary.
- An entry draft (`add-entry`, `local_deck_create`) takes `zoneCategories` as an option, and it defaults to `leave`.
  This differs from `patch-entries` on purpose: a draft's zone defaults to `main`, and an explicit zone cannot be told
  from that default without breaking existing `add-entry`, `local_deck_create`, and Archidekt pull callers. A draft that
  wants the zone category sends `zoneCategories: "match"`.
- `whenCardPresent: "skip"` skips a draft's categories together with its entry.

## Archidekt zone categories

On Archidekt a card's category names decide where it sits. The server maps between categories and zones.

**Pull** (`ArchidektJsonContract.MapZone` in `src/MtgMcp.Archidekt/ArchidektContractMapper.cs`). The server reads
the entry's category names with the primary category first. It checks each name in order:

1. A category named `Commander` (any case), or a category Archidekt marks as premier, gives zone `commander`.
2. A category named `Sideboard` gives `sideboard`.
3. A category named `Maybeboard` gives `maybeboard`.

If no name matches, the entry is in `main`, unless its first category is flagged as not included in the deck.
That case gives `maybeboard`. An entry with no category is `main`.

**Push.** A push sends the local entries with their local categories. It does not turn a zone into a
category. Before it plans writes, it computes the zone the entry's categories would give on Archidekt, using the
same mapping. If that zone is not the entry's local zone, the push is refused with
`entry-zone-contradicts-category` (`ArchidektCoordinator.cs`, `ArchidektSyncPlanner.FindZoneContradictions` in
`src/MtgMcp.Archidekt/ArchidektSyncPlanner.cs`). The check covers entries a push would add or update. The
message names each card, its local entry ID, its local zone, and the zone its categories give
(`ArchidektZoneContradiction` carries the entry ID; `ArchidektSyncNotices.DescribeZoneContradictions` writes the
text). It offers ready `patch-entries` changes and chooses neither: the `leave` fix always, and the `match` fix only when it is accepted and clears the contradiction (otherwise the message says what else to change): set the zone to what the categories
give (`zoneCategories: "leave"`), or keep the zone and make the categories match it (`zoneCategories: "match"`, which
creates a missing zone category). Entries with the same pair of zones share one change, up to 100 entries each.
`archidekt_push_preview` and `archidekt_push_apply` both return this refusal, as does the planner that builds the
preview.

So a zone move on a deck bound to Archidekt is two settings that must agree: the zone and the category. A single
`patch-entries` with `zoneCategories: "match"` sets both.

## Pushing cards to Archidekt

Push sends card changes for one deck through `modifyCards/v2`, which takes a list of cards
(`src/MtgMcp.Archidekt/ArchidektDeckTransport.cs`, `ArchidektDeckOperations.cs`):

- A run of consecutive added entries goes out in one request of up to 25 cards (`ArchidektSyncPlanner.CardBatchSize`).
  Every other operation is one request. Each card of an accepted batch is matched to its returned relation by
  `patchId`.
- Archidekt refuses a batch that holds a bad card as a whole (a 400 that names no card) and changes nothing. After a
  rejected batch of adds, the push sends that batch's cards one at a time, once per push. A card Archidekt refuses is
  `not-applied` and the push continues, and the outcome is `partial`. A second rejected batch is not resent
  (`not-sent`; push again). A `429` stops the push, and a `403`, `404`, `422`, or any other failure after a write
  started leaves the operation `unknown`; the statuses of the operations before it are kept. Each added card is
  confirmed by the `patchId` Archidekt echoes (adds only); an add that does not come back is `unknown`.
- A `partial` push reads the Archidekt deck back, as a finished push does (`ArchidektCoordinator.ApplyPushAsync`). The
  new baseline is that read, and the local entries whose operations were `applied` are linked to their Archidekt rows.
  An entry whose add or update was `not-applied`, `not-sent`, `not-attempted`, or `unknown` keeps no new link
  (`ArchidektLocalMapper.CreatePushBaseline`), so it stays a local change and the next push preview offers only those
  changes. A new entry that did not land is left out of the linking, so it cannot take the row of a landed entry with
  the same card. An `unknown` add that did land shows as a local removal of its Archidekt row plus the local add, so
  the next push replaces it instead of adding it twice. A category whose rename did not land stays linked by its
  Archidekt ID, so the next push keeps its flags. Every stale zone is corrected from the entry's local categories,
  whether or not the entry's own operation landed. When the read fails or the request limit is reached, the baseline
  stays as it was and the `notice` says so.
- After a `partial` push or a `verification-mismatch`, the read is compared with the read before the push
  (`ArchidektSyncPlanner.ChangedOutsidePush`). When the deck details, a category, or a card row that no `applied` or
  `unknown` operation touched changed, or more rows or categories appeared than such operations created, someone else
  edited the deck during the push. The baseline then records a composed deck (`ComposeAfterOutsideEdit`): what those
  operations touched or created comes from the read after the push and is linked as usual, and everything else comes
  from the read before it, with fingerprints computed from that content. The outside edit is then a change on
  Archidekt: the push preview asks for a pull, the pull takes the edit and keeps the local additions, and the push
  does not revert it. A mismatch that the outside edit alone explains is recorded as a finished push. The `notice`
  says to preview a pull before pushing again.
- A local row that names no printing takes the printing of its linked baseline row in the comparison
  (`ArchidektSyncPlanner.Diff`), so the printing Archidekt chose for a card added by name is not a local change.
- `ArchidektSyncPlanner.OperationRequests` counts one request per batch or other operation, plus the cards of the
  largest batch again, so the fallback fits in the prediction.
- `predictedProviderRequests` on the push preview is the figure checked against the limit of 150 requests for one
  call of `archidekt_push_apply` (`providerRequestLimit`, requests to Archidekt). It counts the operations, the printing searches already made, the reads the apply
  makes, room to retry each read after a rate-limit response, and the reserve for one batch fallback. Before this change it counted operations, searches,
  and one read, so it was smaller for the same push. A push over the limit is refused in the preview and the apply with
  the limit, the predicted count, an estimate of how many operations fit, and the advice to apply part of the local changes first
  (`ArchidektOperationBudget.DescribeOverLimit`).

## Change kinds

`local_deck_apply_changes` is the only tool that edits an existing deck. Its `changes` list is a closed set of
variants. `kind` selects one (`src/MtgMcp.App/Decks/DeckChangeInput.cs`). Each maps to a Core `DeckChange` case
(`src/MtgMcp.Core/Decks/DeckChanges.cs`).

| `kind` | What it does |
| --- | --- |
| `update-metadata` | Replaces name, description, and format. All three are sent. |
| `add-entry` | Adds one entry from an `entryDraft`. A card name or a set and collector number gets its identity from installed card data. A name not found is still added and listed in `issues`. |
| `update-entry` | Replaces one entry. The whole entry is sent, with its `entryId`. To change a few fields, or many entries, use `patch-entries`. |
| `patch-entries` | Changes only the listed fields of 1 to 100 distinct entries by ID (`quantity`, `zone`, `sortOrder`, `finish`, `language`, a `printing`, `primaryCategoryId`, `addCategoryIds`, `removeCategoryIds`, and `zoneCategories`). The patch runs against each entry's state at that point in the batch, so it sees earlier changes and entries added earlier. Card identity (`cardName`, `oracleId`) cannot be patched. |
| `remove-entry` | Removes one entry by `entryId`, or 1 to 100 distinct entries by `entryIds`; exactly one of the two. |
| `add-category` | Adds one category from a `categoryDraft`. |
| `update-category` | Replaces one category. The whole category is sent, with its `categoryId`. |
| `remove-category` | Removes one category by ID. |
| `assign-category` | Assigns a category to an entry. With `isPrimary` true, the previous primary stays assigned as non-primary. |
| `unassign-category` | Removes one assignment. |
| `upsert-provider-binding` | Creates or replaces a provider binding and its baseline. |
| `remove-provider-binding` | Removes a provider binding by ID. |

`patch-entries` rules (`src/MtgMcp.Core/Decks/PatchDeckEntriesRules.cs`, applied in
`src/MtgMcp.Decks/DeckEntryPatcher.cs` inside the batch transaction):

- A field left out stays as it is. A patch with no field besides `entryIds` and `zoneCategories` is refused.
- The `printing` is a `printingId` alone, or a `setCode` with a `collectorNumber`. App resolves it against each entry's
  own card as `add-entry` does. A printing that cannot be verified (unresolved, mismatched to the entry's card, set not found, or card data not installed) refuses the batch with `invalid-deck-change`, and nothing is written.
- `primaryCategoryId` makes that category primary; one the entry already has as non-primary becomes primary. The
  previous primary assignment is removed, unless that category is also in `addCategoryIds`, which keeps it as
  non-primary. This differs from `assign-category`, which keeps the previous primary as non-primary.
- `zone` requires `zoneCategories` (see Zone categories).
- These are refused with `invalid-deck-change`, the batch is rolled back, and nothing is written: no field; a
  duplicate, unknown, or empty entry ID; an unknown or empty category ID; the same category in two of the three
  category fields; and a zone change that breaks the zone category rules.
- A later `add-entry` with `whenCardPresent: "skip"` compares against the zone and quantity the patch sets.

`whenCardPresent` is a batch option. `add` (default) adds another entry for a card already in the same zone and
reports `already-in-deck`. `skip` leaves that add out and reports it (`src/MtgMcp.App/Decks/DeckAddPresence.cs`).

## Batch semantics

- A batch runs in order in one transaction (`SqliteDeckStore.ApplyChangesAsync`). Any failure rolls back the
  whole batch. A successful batch raises the revision by one, however many changes it holds.
- The call needs `expectedRevision`. If the deck is at another revision, the call is refused with a `conflict`
  (`stale-deck-revision`) and `current.revision` holds the current one.
- An empty `changes` list is refused. A batch has no change-count limit.
- A later change can refer to an entry or category an earlier change in the same batch added, if the earlier
  change carried its own ID.
- Removing or updating a row that does not exist refuses the batch with a not-found result
  (`deck-entry-not-found`, `deck-category-not-found`, `deck-provider-binding-not-found`).
- Within one batch, two `assign-category` changes that make different categories primary for the same entry are
  refused. Across batches, a new primary replaces the old one.
- The result is a summary: the new revision, counts, `cardsByZone`, the IDs of changed entries and categories
  (at most 100 IDs), and `issues` (at most 30). Pass `returnDeck: true` to get the whole deck
  (`src/MtgMcp.App/Decks/DeckWriteResult.cs`).
- If every change was a skipped add, nothing is written and the revision does not change.
- Only the batch tool writes entries and categories of an existing deck. Pull apply, identity apply, category
  rules apply, and duplicates removal each write through their own guarded path. Duplicates removal returns
  changes for this tool to apply.
