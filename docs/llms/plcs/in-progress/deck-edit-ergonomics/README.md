# Deck Edit Ergonomics Plan

## Current Work

- Status: In progress (phase 0 done; phases 1, 2, 3a, and 5 running)
- Owner: mtg-mcp (owner session)
- Last updated: 2026-11-01, branch `deck-edit-ergonomics/integration` at `645ff5b`
- Implementation authorized: Yes (owner, 2026-11-01: "Start implementing")
- Selected work and endpoint: phases 0 to 6, merged into `main`, audited, released in the next version, and
  installed.
- State: drafted and reviewed once (independent read-only review, 2026-11-01). Updated the same day with three
  more session reports (see Sources).
- Decisions: the owner accepted D-2, D-3, and D-5 and approved the D-7 live probe on 2026-11-01.
- Next action: wave A (phase 0 facts and probe, phases 1, 2, and 3a) in parallel worktrees, then wave B (phase 3b, phase 4), then phase 5.
- Blocker: none.
- Resume check: this file exists on `main` or on `docs/deck-edit-ergonomics-plc`.

## Sources

Four agent sessions reported on real Archidekt deck work. All four were on `0.13.0` or `0.13.1`, before the
`0.13.3` fixes.

1. Blood Avatar deck (347 entries): moved 13 maybeboard cards to the sideboard and added 5 sideboard cards. This
   took a local PowerShell JSON-RPC script, because direct tool calls would have flooded the context or meant
   writing 49 changes by hand.
2. Wilson deck: moved 23 cards between zones, about 70 changes. Asked for a one-step zone move that also sets the
   matching Archidekt category, and for a commander-damage goldfish.
3. Long deck-tuning session: reported large outputs that forced the conversation to be summarized, plus these
   failed calls:
   - `discardLocalChanges` was needed on both the pull preview and the pull apply.
   - `sortOrder` was given as a non-integer.
   - A push over about 40 operations hit the request limit.
   - A GUID conversion error didn't name its field.
4. Sideboard session: adding 15 cards took about 8 calls (pull preview, every page of the deck, a category ID
   lookup, Scryfall IDs for each card, apply, push preview, push apply, a check on Archidekt). It also reported
   the three gaps that `0.13.3` fixed.

## Problem

The reports describe seven general problems. The plan fixes each one across every tool it affects, not only the
instance that was reported.

| Shape | Reported instances | Where else it shows up |
| --- | --- | --- |
| S1. Finding entries means paging through the whole deck. | 4 pages to find 13 named cards; 3 pages of raw JSON to find entry IDs in a 200-card deck. `card_list_query` filters by zone and card fields but returns no entry IDs. | Any edit that targets a zone, a category, or a card property ("every aura in the maybeboard"). |
| S2. Changing entries means restating each one, one fact per change. | One zone move took 3 changes, one of them copying 11 fields; 23 moves took about 70 changes. | Quantity, printing, and finish changes; recategorizing; removing many entries. |
| S3. Zones and Archidekt categories are two settings that must agree. | The `entry-zone-contradicts-category` refusal; Maybeboard cards that also carry a non-primary Sideboard category; a zone move needs the category ID looked up by name first. | Every zone move and every add into the sideboard, maybeboard, or commander zone on an Archidekt-bound deck. |
| S4. Creating something takes a second change to finish it. | `add-entry` cannot set categories. | `local_deck_create` and imports share the same entry draft. |
| S5. Results show whole objects, not what changes. | An 18-edit push preview was about 25 KB and forced a context summary. | `archidekt_sync_diff` and `archidekt_pull_preview` return the same difference rows. |
| S6. A refused call does not say how to fix it. | A generic `pull-preview-changed` when `discardLocalChanges` differs; a zone refusal that names the card but not the entry or the fix; a request-limit refusal with no way to split; type errors on `sortOrder` and GUIDs. | Every preview and apply pair, and every typed input field. |
| S7. Push cost grows with each card, not each push. | A push over about 40 operations exceeded the request limit. | Every large sync. `modifyCards/v2` accepts a `cards` list, but mtg-mcp sends one card per request (`ArchidektDeckTransport.cs:241-256`). |

The reports also raised points that are not in this plan. See Handled Elsewhere.

## Requirements

| ID | Shape | Required behavior | Acceptance criteria |
| --- | --- | --- | --- |
| REQ-001 | S1 | `local_deck_get` accepts `zones` (local zone codes; an unknown code is refused as invalid input naming the allowed codes) and `categoryNames` (trimmed and case-insensitive, matching primary and non-primary assignments) beside `cardNames`. Values within one filter combine with OR; filters combine with AND. Only matching entries are returned, with deck-wide totals unchanged. No match is a normal result with no entries, not an error. Each filter value that matches nothing is listed in `zonesNotFound` or `categoryNamesNotFound`, as `cardNamesNotFound` already is. The new filters join the cursor fingerprint, which already covers `cardNames` and `responseFormat`. `include` (cards, tags) applies to the filtered entries only. | Tests: zone filter; category filter on primary and non-primary assignments; both combined with `cardNames`; an unknown zone is refused; a category with no match is listed; a cursor refuses a changed filter; `include: ["cards"]` with a filter returns card data only for matches. On a 347-entry deck fixture, a concise call with `categoryNames: ["Maybeboard"]` returns its 13 entries in one page. |
| REQ-002 | S2 | `local_deck_apply_changes` gets a `patch-entries` change kind. It takes `entryIds` (1 to 100, no duplicates) plus only the fields that change: `quantity`, `zone`, `sortOrder`, `finish`, `language`, a `printing` group (`printingId`, or `setCode` and `collectorNumber`, resolved against the entry's card as `add-entry` resolves them), and category edits `primaryCategoryId`, `addCategoryIds`, and `removeCategoryIds`. A field left out stays unchanged. The same patch applies to every listed entry, including entries added earlier in the same batch. A category already assigned as non-primary becomes primary when named in `primaryCategoryId`. A new primary category removes the previous primary's assignment unless that category is in `addCategoryIds` (D-2). These are refused with `invalid-deck-change` and no write: a patch with no fields; the same category ID in two of the category fields; a duplicate or unknown entry ID; an unknown category ID. Card identity (`cardName`, `oracleId`) cannot be patched. | Tests: a quantity-only patch; a printing patch is resolved and reports identity issues as `add-entry` does, also for an entry added earlier in the batch; each refusal above, with no write; `addCategoryIds` keeps the old primary as non-primary. |
| REQ-003 | S2 | `remove-entry` accepts `entryIds` (1 to 100) in place of `entryId`; exactly one of the two must be given (D-3). | Tests: one change removes 13 entries; both or neither field is refused; an unknown ID refuses the batch with no write. |
| REQ-004 | S4 | `DeckEntryDraft` accepts `categoryIds` and `primaryCategoryId`. An `add-entry` may name a category added earlier in the same batch by its caller-chosen `categoryId`. `whenCardPresent: "skip"` skips the categories together with the entry. Wherever the draft is accepted (`add-entry`, `local_deck_create`, imports), the fields are honored or refused with a message; none silently drops them. The Archidekt pull mapper leaves them unset. | Tests: an add into a category is one change; a category added in the same batch is assigned; an unknown category refuses the batch; a skipped add assigns nothing; `DeckContractValidator.Normalize` keeps the fields; `local_deck_create` with drafted categories. |
| REQ-005 | S5 | `archidekt_sync_diff`, `archidekt_pull_preview`, and `archidekt_push_preview` accept `responseFormat` (`full` by default, or `concise`). A concise difference row is `{ path, state, fields: [{ name, baseline, local, remote }] }` and lists only the fields whose values differ. For an added or removed entry, it lists the fields of the side that exists. Fingerprints, guards, summaries, operations, and apply availability are the same in both formats. Paging measures the size of the format returned, so concise pages hold more rows; `nextDifferenceOffset` keeps its meaning. `archidekt_snapshot_restore_preview` is excluded: its single difference row holds two content fingerprints, not entry JSON. | Tests on an 18-edit push built with fake HTTP (no recorded push fixture exists; phase 3 builds one): the concise result is at most one third the size of the full result, with the same `previewFingerprint` and `applyAvailable`. Field-level tests cover an added entry, a removed entry, a zone and category change, and a conflict. |
| REQ-006 | S6 | When `archidekt_pull_apply` gets a preview fingerprint that does not match, and the preview with the other `discardLocalChanges` value does match, the refusal says which value the preview used and to pass the same value. Each preview result's apply notice names the options its apply call must repeat. `confirmRemovals` already has a named refusal (`push-removals-unconfirmed`); it gets a regression test. A wrong confirmation phrase on `archidekt_snapshot_restore_apply` and the delete tools (`confirmation-required`) states the exact phrase expected. | Tests: a pull apply without the `discardLocalChanges` its preview used is refused with a message naming the option and value; a pull and a push preview notice each name their options; the existing `confirmRemovals` refusal keeps naming the option; a wrong restore or delete phrase shows the expected phrase. |
| REQ-007 | S3, S6 | When push preview refuses entries whose zone disagrees with their categories, the notice names each local entry ID and the one `patch-entries` change that fixes it: `zoneCategories: "match"` with the zone the caller keeps (REQ-010). The notice offers both directions, setting the zone or keeping it, and the server chooses neither. `ArchidektZoneContradiction` gains the local entry ID. | A test with a zone-only edit shows both fixes; applying either makes the push available, including when the deck has no Sideboard category yet. |
| REQ-008 | all | Tool descriptions point to the new options: `update-entry`, `assign-category`, and `unassign-category` point to `patch-entries`, and `local_deck_get` names its filters. The reference (`docs/reference.md`, or the README if the redesign has not merged), `CHANGELOG.md`, and `docs/output-control.md` describe the changes. Each description stays under the 4,096-character limit. | Description and architecture tests; doc review. |
| REQ-009 | all | Once a deck is pulled, both reported workflows take at most 3 or 4 tool calls and no hand-copied entry fields, no category ID lookup, and no Scryfall ID lookup. (a) Move 13 maybeboard cards to the sideboard, add 5 sideboard cards, then push: `local_deck_get` (filtered, concise), one `local_deck_apply_changes` (one `patch-entries` and 5 `add-entry`, all with `zoneCategories: "match"`), `archidekt_push_preview` (concise), `archidekt_push_apply`. (b) Add 15 sideboard cards and push: one `local_deck_apply_changes`, `archidekt_push_preview`, `archidekt_push_apply`. | Two E2E tests on the deck fixture against fake HTTP, each asserting the call count and the pushed Archidekt categories. |
| REQ-010 | S3 | `patch-entries` and entry drafts accept `zoneCategories` (`match` or `leave`). It is required whenever the change sets a zone, so the caller always chooses (D-5). With `match`: the entry's primary category becomes the zone category of the new zone (Commander, Sideboard, or Maybeboard, matched case-insensitively), and the category is created in the same batch if the deck lacks it. Every other zone category assignment is removed from the entry. For zone `main`, zone categories are removed; if one was primary, the change must also give `primaryCategoryId`, or the entry must have no other category; otherwise it is refused with a message saying so. With `leave`, only the zone changes. The names come from one Core definition that the Archidekt adapter also uses. | Tests: maybeboard to sideboard with a non-primary Sideboard category already present ends with exactly one zone category; sideboard to main with and without `primaryCategoryId`; the zone category is created when missing; `leave` changes only the zone; a zone change without `zoneCategories` is refused; a pushed result has no zone contradiction. |
| REQ-011 | S1 | `card_list_query` accepts `entryId` as a row field for local deck and deck backup sources, so a card-property query ("every aura in the maybeboard") returns the IDs an edit needs. Other sources report the field as unavailable, as they already do for deck-only fields. | Tests: rows with `entryId` for a deck source; a filter on `subtypes` and `zone` returns the expected IDs; a Scryfall source refuses or omits the field consistently with `zone`. |
| REQ-012 | S6 | Every refused tool argument names its JSON path and the expected type ("an integer", "a UUID"), including values inside polymorphic change lists and nested drafts. Phase 0 reproduces the reported GUID and `sortOrder` errors on `0.13.3`. If they already meet this, the requirement closes with regression tests. | Tests: a non-integer `sortOrder` and a malformed GUID inside `changes[3]` of `local_deck_apply_changes` each name the full path and type. |
| REQ-013 | S6, S7 | A request-limit refusal on a push preview says how many operations fit and that the push can be split by applying part of the local changes first. The preview summary shows predicted requests against the limit before any refusal. | Tests: an over-limit push preview names the limit, the predicted count, and the split advice; a preview under the limit shows both numbers. |
| REQ-014 | S7 | Push sends card mutations for one deck in `modifyCards/v2` requests of up to 25 cards each, not one card per request. The probe found no size limit, so 25 is a conservative starting value held in one constant. Each card in a successful batch is matched to its returned relation by `patchId`. Archidekt rejects a batch with a bad card as a whole (HTTP 400, no card named, deck unchanged), so after a failed batch the push sends that batch's cards one at a time to find the card that fails. It reports that card per card, as today. The request prediction counts this fallback in its reserve. | Fake-HTTP tests: a 40-card push makes 2 mutation requests; a batch with one bad card falls back to single sends, applies the good cards, names the bad card, and leaves the baseline consistent with what landed; predicted requests are never fewer than requests sent. |
| REQ-015 | S1-S7 | Descriptions and server instructions state facts agents missed: `add-entry` needs only a card name (resolution fills the IDs; give `printingId` or `setCode` and `collectorNumber` to choose a printing instead of Archidekt's first-listed one); `archidekt_push_apply` reads Archidekt back and verifies each card, so no separate check is needed; `card_list_query` groups by `subtypes` (such as Aura and Equipment) and by `tags`; `stats_castability`, `stats_mana_availability`, and `stats_simulate` answer land-count and on-curve questions. | Description tests within the length limit; server instruction tests. |
| REQ-016 | docs | A `docs/architecture/` folder describes the design and conventions an agent or contributor needs before changing code: `README.md` (the current `docs/architecture.md` moved here, as the index), `mcp-surface.md` (tool naming and prefixes, description rules, parameter and result conventions, preview and apply pairs, fingerprints and the options an apply repeats, `responseFormat`, paging, errors and reason codes, operation modes and annotations), `deck-model.md` (entries, zones, categories, Archidekt zone categories, change kinds, and batch semantics), and `code-style.md` (a short summary that links `AGENTS.md` and `docs/llms/csharp-best-practices.md` instead of copying them). Every link to `docs/architecture.md` (`AGENTS.md`, `llms.txt`, other docs) points to the new folder; no stub file is left behind. | `git diff --check`; a link check over the changed docs; every statement checked against the code at the commit it lands on. |
| REQ-017 | docs | The naming rule is written in `mcp-surface.md` and enforced by an architecture test over every registered tool: tool names are snake_case with a registered prefix; parameter and result property names are camelCase; change kinds, discriminators, reason codes, and other fixed values the server defines are kebab-case; values passed through from a provider (such as Scryfall's `not_legal` and tag slugs) keep the provider's spelling. | An architecture test that fails on a snake_case or kebab-case property, a camelCase discriminator, or an unprefixed tool, with an allowlist only for named provider pass-through values. |
| REQ-018 | docs | Writing the pages doubles as a consistency check of the MCP surface against the written rules. Any inconsistency that can be fixed without breaking a client is fixed in this phase: a description, a default, an error message, an output field added beside an existing one. Breaking inconsistencies, such as renaming a field or changing paging style, are listed in the plan as findings for the owner to decide, not changed. | The inventory is recorded in Evidence; every non-breaking repair has a test. |

Non-goals:

- **Writes that target a filter**, such as "move every Maybeboard entry". Writes stay keyed by explicit entry IDs;
  the REQ-001 and REQ-011 reads give the IDs in one call. A filter-targeted write would change entries the caller
  never saw.
- **Removing `update-entry`, `assign-category`, or `unassign-category`.** They keep working; their descriptions
  point to `patch-entries`. Removal would follow the deprecation rules in `docs/versioning.md` and is not planned.
- **Partial updates for deck metadata and categories.** Each has 3 fields, and leaving out `description` cannot be
  told apart from clearing it.
- **Merging zones into categories in the local store** (D-6).
- **Filters on other deck read tools.** `stats_local_deck_summary`, `local_deck_validate`, and
  `local_deck_duplicates_preview` report on the whole deck by design.
- **New tools.** The tool count stays the same.
- **Renaming existing tools, parameters, or fields.** The names already follow one rule per kind of name
  (REQ-017), and a rename would break clients with no compatibility alias allowed.

## Handled Elsewhere

| Report | Where it goes |
| --- | --- |
| Adding a card already in the deck gives no warning; the duplicates tool won't pick between identical copies; no one-call membership check. | Fixed in `0.13.3` (`local-deck-agent-safety`): the `already-in-deck` issue, `whenCardPresent: "skip"`, identical-copy tie settling, and `cardNames` with `concise` on `local_deck_get`. |
| Tools not loaded in Claude Code; the 0.13.0 output-schema bug; tools missing from ToolSearch. | The schema bug was fixed in `0.13.1`. ToolSearch loading is a client harness issue. |
| "The server just disconnected again." | Not reproduced; no log came with the report. It needs the client's MCP log for that session before it can be planned. |
| Commander-damage goldfish ("chance of 21 commander damage by turn N"). | Separate plan. `stats_simulate` has rule kinds for draws, plays, casts, mana, and counts (`StatisticsSimulateInputs.cs`), and no damage, counters, or combat. Adding caller-stated damage and counter rules is new `MtgMcp.Simulation` rule kinds under the product rule that the sampler applies only stated rules. A "ready-made setup" can be a documented example request, not hidden defaults. |
| Popularity and inclusion data (EDHREC, Moxfield, public Archidekt decks). | `docs/potential-features.md` ("Popularity and tournament evidence") records that the site terms do not allow direct EDHREC, Moxfield, or public Archidekt deck collection. It holds the future `popularity-evidence-sources` PLC, which needs a permissioned source. Agents fetching those sites directly are outside mtg-mcp. |

## Current System

Inspected at `c4fa0f1` (2026-11-01, read-only):

- **Change kinds.**
  - They are a closed polymorphic input set (`DeckChangeInput.cs:10-22`). The discriminators are listed for schema
    enrichment in `DeckBatchWriteTools.cs:23-36`.
  - They map one-to-one onto the Core union `DeckChange` (`DeckChanges.cs:71`).
  - `update-entry` takes a whole `DeckEntry`. `assign-category` keeps a previous primary as non-primary.
  - A batch has no change-count limit; written IDs share a 100-ID result budget.
- **Entry drafts.**
  - `DeckEntryDraft` (`DeckModels.cs:48-59`) has no categories.
  - It is shared by `add-entry`, `local_deck_create` (`DeckContractValidator.cs:13`, `:42`), imports
    (`DeckInterchangeService.cs:348`), and the Archidekt pull mapper (`ArchidektLocalMapper.cs:40`, `:174`).
  - `DeckCategoryDraft` already takes an optional caller-chosen `CategoryId` (`DeckModels.cs:73-77`).
  - `whenCardPresent` handling lives in `DeckAddPresence.cs`.
- **Deck reads.**
  - `local_deck_get` has `cardNames` and `responseFormat` (`DeckReadTools.cs:84-130`).
  - `DeckCardViewCoordinator` owns filtering and the cursor fingerprint (`:356-375`).
  - `card_list_query` keeps each deck entry's ID internally (`CardListResolver.cs:172-177`, `:729`). It returns the
    ID only for unmatched entries (`:152`), not as a row field. It needs installed card data; `local_deck_get` does
    not.
- **Zones on Archidekt.**
  - Archidekt has no zones. Pull gives the zone from categories (`ArchidektContractMapper.cs:480-504`): Commander
    or a premier category, Sideboard, Maybeboard, else main or maybeboard by "included in deck".
  - Push sends categories. Push preview refuses entries whose zone disagrees with their categories
    (`ArchidektSyncPlanner.cs:703-725`; notice in `ArchidektSyncNotices.cs:101`).
  - That refusal record carries only the card name and the two zones (`ArchidektSyncPlanner.cs:1067`).
- **Difference rows.**
  - `ArchidektDifference` (`ArchidektModels.cs:247-252`) holds `path`, `state`, and three whole JSON values.
  - It is returned by `ArchidektSyncDiffView` (`ArchidektReadResults.cs:87`), `ArchidektSyncPreviewView` (`:361`;
    pull and push), and `ArchidektSnapshotRestorePreviewView` (`:118`).
  - The snapshot row (`ArchidektSnapshotOperations.cs:357`) holds two content fingerprints.
- **Pull and push apply.**
  - Pull apply re-runs the preview with its `discardLocalChanges`, which the preview fingerprint includes
    (`ArchidektCoordinator.cs:265`, `:300`). A mismatch gives a generic `pull-preview-changed`.
  - Push apply refuses a missing `confirmRemovals` by name (`:732-735`).
- **Request limits.**
  - Each Archidekt operation is capped at 150 provider requests, paced at 30 per minute (`ArchidektOptions.cs:24-32`).
  - The plan counts one request per card mutation plus reads and reserved retries
    (`ArchidektDeckOperations.cs:281-285`).
  - An over-limit preview fails with `request-limit-exceeded` and no split advice (`ArchidektRequestPacer.cs:38-46`).
  - Card mutations go to `modifyCards/v2` with a one-element `cards` list (`ArchidektDeckTransport.cs:244-256`).
- **Argument errors.** Binding errors already name the JSON path (`ToolCallPolicy.cs:298-340`). Whether a bad GUID
  or `sortOrder` inside a polymorphic change list keeps the full path and names the expected type is unverified
  (phase 0).

## Design And Decisions

- **D-1, S2 as a Core change.**
  - Add `PatchDeckEntriesChange` to the Core `DeckChange` union. The store applies it against each entry's current
    state inside the batch transaction, so a patch sees changes made earlier in the same batch and refuses with a
    single issue.
  - Rejected: expanding a patch in App into existing primitive changes. That needs the current deck state before
    the transaction and turns one refusal into several.
  - App resolves the `printing` group with `DeckEntryResolver` before the store runs, using the stored card name,
    or the drafted card name for an entry added earlier in the batch. The store writes the resolved values.
- **D-2, primary replacement (accepted by the owner, 2026-11-01).**
  - A new `primaryCategoryId` removes the previous primary's assignment; `addCategoryIds` keeps it as non-primary.
  - Rejected: keeping the old primary, as `assign-category` does. That brings back the extra change this plan
    removes.
- **D-3, removal lists (accepted by the owner, 2026-11-01).**
  - `remove-entry` takes `entryIds` in place of `entryId`.
  - Rejected: a `remove-entries` kind, which adds a second change kind for one job, and removal inside
    `patch-entries`, which mixes deleting entries with editing them.
- **D-4, no second categorization path.**
  - `local_deck_category_rules_apply` assigns categories from rules over card data. `patch-entries` assigns
    categories the caller chose to entries the caller named. They answer different questions.
  - `replacePrimary` on `assign-category`, proposed earlier, is dropped: `patch-entries` covers it.
- **D-5, zone categories are the caller's explicit choice (accepted by the owner, 2026-11-01).**
  - Whenever a change sets a zone, it must say `zoneCategories: "match"` or `"leave"`. `match` gives Archidekt's
    rule in one change, which is the "move entries to zone X and set the matching category" operation that was
    asked for. A required field keeps the choice with the caller, as the simulation rules do for engine choices.
  - The zone category names (Commander, Sideboard, Maybeboard) move into one Core definition the Archidekt mapper
    also reads. Archidekt-only rules, such as premier categories and "included in deck", stay in the adapter.
  - Rejected: `match` by default, which hides a category write behind a zone change, and a dedicated `move-entries`
    kind, which would be a second way to do what `patch-entries` with `zone` does.
- **D-6, zones stay separate from categories in the local store.**
  - Local decks are provider-neutral: imports and other formats have zones without categories.
  - Making zones a kind of category would change storage and every deck tool for one provider's convention.
    REQ-010 removes the cost of keeping the two in step.
- **D-7, batched card mutations (live probe approved by the owner, 2026-11-01).**
  - `modifyCards/v2` takes a `cards` list. Phase 0 sends one 2-card and one deliberately half-invalid batch to a
    throwaway Archidekt deck to learn the response shape, whether the batch is all-or-nothing, and any size limit.
  - That is a live provider mutation, so it runs only with the owner's approval, on a deck created for the probe.
    It is recorded as a fixture afterwards.
  - Without that evidence, REQ-014 is dropped and REQ-013 stands alone.
- **S1:** extend the existing `cardNames` path in `DeckCardViewCoordinator` into one entry filter covering names,
  zones, and category names. `card_list_query` adds `entryId` to the row fields it already has for deck sources.
  The two reads stay separate:
  - `local_deck_get` works without card data and returns stored state for editing.
  - `card_list_query` analyzes card properties.
- **S4:** apply drafted categories after the entry is inserted, in the same transaction, through the code
  `assign-category` uses. Update `DeckContractValidator.Normalize` and `DeckAddPresence` so the fields survive
  normalization and are skipped together with a skipped add.
- **S5:** the field-level form is computed in the Archidekt adapter (`MtgMcp.Archidekt`), which owns the entry JSON
  shape, from the same rows the full form uses. App's `ArchidektResultPaging` picks the format and measures it.
  Fingerprints are computed before formatting.
- **S6, pull options:** on a pull fingerprint mismatch, recompute the preview fingerprint with the other
  `discardLocalChanges` value, from data already fetched and with no second provider read. Name the option only
  when that fingerprint matches.
- **S6, zone refusals:** add the local entry ID to `ArchidektZoneContradiction`. Target rows are built in local
  entry order in `ArchidektLocalMapper.ToRemoteTarget`.

### MCP Surface

- No tools are added or removed. The tool count, the annotations, and operation-mode visibility do not change:
  - `local_deck_apply_changes` writes.
  - The Archidekt diff, previews, and `card_list_query` stay read-only.
  - `patch-entries` is available exactly where `local_deck_apply_changes` is.
- Input schema changes:
  - `local_deck_get` gains `zones` and `categoryNames`.
  - The change discriminators gain `patch-entries` (the `DeckChangeInput` `JsonDerivedType` list and
    `DeckBatchWriteTools.ChangeKinds`).
  - `remove-entry` gains `entryIds`.
  - `DeckEntryDraft` gains `categoryIds`, `primaryCategoryId`, and `zoneCategories`.
  - Three Archidekt tools gain `responseFormat`.
  - `card_list_query` row fields gain `entryId`.
- Output schema changes:
  - The `notFound` lists on `local_deck_get`.
  - The concise difference row type.
  - Predicted requests against the limit in the push preview summary.
- Surface tests: tool schema and output-schema snapshots, description length, and the architecture tests that
  check tool registration and mode visibility.

### Fixtures

- Deck tests use in-memory stores built in the tests.
- Archidekt tests use fake HTTP: a 347-entry deck with 13 Maybeboard entries (some also carrying a non-primary
  Sideboard category), an 18-edit push built from it, and a 40-card push.
- The D-7 probe, if approved, becomes a recorded fixture with owner data scrubbed, following the
  `recorded-provider-fixtures` scrub rules.
- No other live provider calls.

## Steps And Verification

0. **Phase 0, facts:**
   - Record the preview and apply pairs with the options each must repeat, including the confirmation phrase of
     `archidekt_snapshot_restore_apply`.
   - Reproduce the GUID and `sortOrder` errors on `0.13.3` (REQ-012).
   - Run the D-7 probe if the owner approves it.
1. **Phase 1 (REQ-001, REQ-011):** `DeckReadTools`, `DeckCardViewCoordinator`, `CardListResolver` and row fields,
   view models, tests.
2. **Phase 2 (REQ-002 to REQ-004, REQ-010):**
   - Core `DeckChanges`, `DeckModels`, and the zone category definition.
   - The `MtgMcp.Decks` store.
   - `DeckChangeInput`, `DeckBatchWriteTools`, `DeckAddPresence`, and `DeckContractValidator`.
   - Tests.
3. **Phase 3 (REQ-005 to REQ-007, REQ-013, REQ-014):**
   - The Archidekt difference form, and the three views and their paging.
   - The pull option message and the zone notice with entry IDs.
   - Request-limit advice and batched mutations.
   - Fake-HTTP fixtures and tests.
4. **Phase 4 (REQ-012):** argument error paths and types, if phase 0 shows a gap.
5. **Phase 5 (REQ-016 to REQ-018):** the `docs/architecture/` folder, the naming rule and its architecture test,
   and the surface consistency inventory with its non-breaking repairs. The pages describe the code as it is on
   the integration branch; phase 6 updates them for features that land later.
6. **Phase 6 (REQ-008, REQ-009, REQ-015):** descriptions, server instructions, docs (including the
   `docs/architecture/` pages the new change kinds and formats touch), the E2E workflow tests, the full audit,
   merge to `main`, release, and install.

Each phase runs focused tests, then `task ci`, `task test:integration`, and `task test:e2e` from PowerShell.

Phases 1, 3, 4, and 5 touch separate files from phase 2 and can run in parallel worktrees. Phase 3 has two
exceptions:

- REQ-007 names `patch-entries` in its fix text, so its apply tests run only after phase 2 merges.
- The Core zone category definition lands in phase 2 before phase 3 uses it.

Phase 6 needs all the others.

Audit: at the end, run the full audit with every specialist over the whole change. Fix every supported finding in
scope, including P3. Run at most 3 passes. Re-audit only after a serious finding or a substantial change.

Execution boundary: findings during implementation do not add requirements or phases automatically. Work beyond
these requirements needs the owner's approval. Routine repairs within scope can go ahead.

Versioning: the changes add parameters, a change kind, fields, and a row field, and the default formats do not
change. Per `docs/versioning.md` they are additive. Batched mutations change the requests sent, not the tool
contract. The CHANGELOG names:

- `patch-entries` and `zoneCategories`
- `remove-entry` `entryIds`
- the concise difference row
- `card_list_query` `entryId`

## Risks And Open Questions

- **Batch size.** A 100-entry patch plus adds can exceed what one result reports. The existing 100-ID budget and
  exact `changeCounts` apply.
- **Category names in filters.** Archidekt decks can have two categories whose names differ only in case (repaired
  on pull in `0.13.3`). The filter and `zoneCategories: "match"` treat them as one name.
- **A non-Archidekt deck with categories named Sideboard.** `match` acts on names only when the caller sets it, so a
  deck that uses those names differently is changed only on request.
- **Batched mutations and partial failure.** If Archidekt applies part of a batch before failing, the baseline must
  record exactly what landed. REQ-014 depends on the probe showing per-card results.

## Evidence And Completion

Phase 0 (2026-11-01, integration at `9b1279c`):

- **Preview and apply pairs.**
  - Pull repeats `discardLocalChanges`; a mismatch gives a generic `pull-preview-changed` (`ArchidektCoordinator.cs:292-328`, fingerprint `:1064-1082`).
  - Push repeats `confirmRemovals` only above the threshold, and already names it (`push-removals-unconfirmed`).
  - Snapshot restore needs the phrase `restore snapshot {snapshotId}`; a wrong phrase gives `confirmation-required` without showing the phrase. The delete tools work the same way.
  - Category rules and identity reconcile carry their options in an apply token.
- **REQ-012.** The path is already complete, for example `changes[3].entry.sortOrder` and `changes[3].entryId`. The type is named as `System.Int32` or `System.Guid`, not "an integer" or "a UUID", so phase 4 stays. A `sortOrder` given as the string `"2"` is accepted. `ChangeListArgumentErrorTests` (`b7bee8e`, 7 cases) records today's wording.
- **D-7 probe** (one private throwaway deck, deleted afterwards, no other deck touched):
  - A 2-card `modifyCards/v2` batch returned 201, with one `add` object per card in request order (`deckRelationId`, echoed `patchId`).
  - A batch of one valid card and one invalid card returned 400 `{"error":"Unable to add one or more cards"}`, and the deck was byte-identical afterwards. A failed batch is all-or-nothing and names no card.
  - No size limit was found.
  - REQ-014 is kept, with a single-card fallback after a failed batch.
  - The scrubbed responses are untracked under the phase 0 worktree's `artifacts/probe/`. Phase 3b turns them into a fake-HTTP fixture.

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
| REQ-010 | planned | |
| REQ-011 | planned | |
| REQ-012 | planned | |
| REQ-013 | planned | |
| REQ-014 | planned | |
| REQ-015 | planned | |
| REQ-016 | planned | |
| REQ-017 | planned | |
| REQ-018 | planned | |

The plan is complete when every requirement is verified, or dropped with recorded evidence where the plan allows it
(REQ-012, REQ-014). Every requirement must be verified on both the merge commit and the release commit, the audit
must be closed, and the release must be installed.
