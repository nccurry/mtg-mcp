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
- An entry draft (`DeckEntryDraft`) is an entry without a required ID. It has no categories. `add-entry`,
  `local_deck_create`, imports, and the Archidekt pull all use it. Categories are set afterward through category
  assignments.
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
message names each card with its local zone and the zone its categories give. The fix is to change the entry's
zone or categories, then preview again. `archidekt_push_preview` and `archidekt_push_apply` both return this
refusal, as does the planner that builds the preview.

So a zone move on a deck bound to Archidekt is two settings that must agree: the zone and the category.

## Change kinds

`local_deck_apply_changes` is the only tool that edits an existing deck. Its `changes` list is a closed set of
variants. `kind` selects one (`src/MtgMcp.App/Decks/DeckChangeInput.cs`). Each maps to a Core `DeckChange` case
(`src/MtgMcp.Core/Decks/DeckChanges.cs`).

| `kind` | What it does |
| --- | --- |
| `update-metadata` | Replaces name, description, and format. All three are sent. |
| `add-entry` | Adds one entry from an `entryDraft`. A card name or a set and collector number gets its identity from installed card data. A name not found is still added and listed in `issues`. |
| `update-entry` | Replaces one entry. The whole entry is sent, with its `entryId`. |
| `remove-entry` | Removes one entry by ID. |
| `add-category` | Adds one category from a `categoryDraft`. |
| `update-category` | Replaces one category. The whole category is sent, with its `categoryId`. |
| `remove-category` | Removes one category by ID. |
| `assign-category` | Assigns a category to an entry. With `isPrimary` true, the previous primary stays assigned as non-primary. |
| `unassign-category` | Removes one assignment. |
| `upsert-provider-binding` | Creates or replaces a provider binding and its baseline. |
| `remove-provider-binding` | Removes a provider binding by ID. |

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
