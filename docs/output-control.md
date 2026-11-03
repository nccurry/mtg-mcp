# MCP Output Control

## Current: card response formats and the result size limit

The Scryfall card tools `scryfall_search`, `scryfall_cards_get`, and
`scryfall_installed_cards_by_tag` take `responseFormat`:

| Value | Result |
| --- | --- |
| `standard` (default) | Every card field this server returns. New fields are added, never removed. |
| `concise` | A small set of fields for comparing many cards. `fields` picks which; `scryfallId`, `oracleId`, and `name` are always included. |
| `full` | `standard` plus the raw provider objects. Pages are limited to 25 rows. |

`local_deck_get` with `include` returns concise card fields and oracle tag slugs for a
page of entries, using the same field names.

Results that can grow with the caller's data stay under 40,000 UTF-8 bytes of
structured result. The text block of a tool result repeats the structured JSON,
and a client passes only one of the two to the model. A `concise` page, and a
`local_deck_get` page with `include`, keeps the leading items that fit and then sets
`truncated: true`, a `narrow` hint, and a `nextCursor` that continues from the
first item left out. The same cut applies in the `standard` and `full` formats, so a page of real cards
can hold fewer cards than `pageSize`; use `concise` or `fields` to fit more cards in a page.

The server aims for 40,000 UTF-8 bytes of structured result. This is a target below the hard ceiling: Claude
Code warns above about 10,000 tokens and saves a tool
text result of more than 50,000 characters to a file, showing the agent only the path (code.claude.com/docs/en/mcp,
"MCP output limits and warnings"). The ceiling is the same whatever `MAX_MCP_OUTPUT_TOKENS` is set to, so the fix is
small, high-signal results, not a larger per-tool limit. The deck write tools therefore return a summary unless
`returnDeck` is true, and `local_deck_get`, `archidekt_deck_get`, `archidekt_push_preview`,
`archidekt_pull_preview`, `archidekt_sync_diff`, `archidekt_snapshot_restore_preview`, `archidekt_folders_get`, and
`archidekt_snapshots_get` (kind `get`) page. `ToolResultBudgetTests` calls every tool in `remote` mode with the largest
offline fixtures and fails on any result, successful or not, over 50,000 characters, and on a tool that grows with
its data but never pages or cuts a result with those fixtures. It has one explicit list of tools it cannot drive
offline (Playgroup, Commander Spellbook, and the Archidekt writes the fake has no route for). A newly registered
tool fails the test until it is driven or in that list. `local_deck_import_preview`,
`local_deck_identity_reconcile_preview`, and `local_deck_category_rules_preview` page with `cursor` (the
fingerprint and apply token cover the whole preview), `local_deck_export_bundle` returns one chunk of one artifact by
UTF-16 code unit (`pageSize` counts code units and a chunk never splits a surrogate pair), and the Scryfall card
tools cut a page to the limit in every format. A paged deck result (the
local and Archidekt deck reads and the import preview) also cuts its free text: the name to 1,000 UTF-8 bytes on every
page (`nameTruncated`), and the description to 8,000 UTF-8 bytes on the first page only (`descriptionTruncated` and
`descriptionLength`), so a long description cannot push every page over the limit.

Every paged result follows one cursor rule: pass `nextCursor` as `cursor` with the same other arguments and the same
`pageSize`-bounded request; `nextCursor` is null on the last page. The cursor is opaque. It records the position (one
position for each list, so a result with two lists, such as operations and differences, still has one `nextCursor`;
a two-list preview shows all operations before any differences) and what the page depends on: the request, the local
deck revision, the preview fingerprint, the Archidekt fingerprint, or the export artifact and its checksum. The
cursor is tied to that state. Its checksum detects accidental edits only, and every reader bounds the position
again, so a forged position is `invalid-cursor`. A cursor whose data changed since the first page is refused
with `cursor-stale` and the current state in `current`; a malformed, edited, out-of-range, or foreign cursor is refused with
`invalid-cursor` and `path` `cursor`. A page size outside the tool's range is `invalid-page-size` with `path`
`pageSize`. The paged previews therefore take no fingerprint argument on later pages. See
[MCP surface](architecture/mcp-surface.md#paging).

`card_lists_compare` takes the same `responseFormat` and `fields`, and pages its result with `cursor` and `nextCursor`; a page
that would pass the size limit keeps the leading cards that fit and sets `truncated` and `narrow`. `card_list_query`
returns groups and, when `rows` is asked for, a page of rows, paged with the top-level `pageSize`, `cursor`, and
`nextCursor`. Groups are cut first to a fixed share of the limit
when rows are present, rows are cut to fit the rest, and a cut result sets `truncated` with a `narrow` hint that says
what to change. `local_deck_duplicates_preview`, `stats_castability`, and `spellbook_deck_bracket_facts_get` cut their
lists the same way and say how many items they left out (`unmatchedOmitted`, `skippedOmitted`,
`unflaggedCardsOmitted`, `combosOmitted`).

In `concise`, `tags` and `cheapestPriceUsd` (lowest USD price among English
printings) come from installed card data. When it is not installed they are
`null` and `limits` says why. The result also names the format it used in
`responseFormat`.

### Page counts and cut pages

`pageSize` is a maximum. `scryfall_search`, `scryfall_cards_get`, `scryfall_installed_cards_by_tag`, and
`card_lists_compare` report two counts on every page: `returnedCount` (the cards on the page) and
`requestedCount` (the cards the page would hold without the size limit). A cut page also sets
`cutReason: "result-size-limit"` and a `narrow` text with the numbers: the kept and requested counts, the average
bytes per card on the page, the card where `nextCursor` continues (no card is skipped), and that leaving
`oracleText` out of `fields` fits more cards in the concise format. An uncut page has no `cutReason`.
`oracleText` is usually the largest field.

### Archidekt deck reads

`archidekt_deck_get` and `archidekt_snapshots_get` (kind `get`) take `zones`, `pageSize` (up to 200, default 100),
`cursor`, and `responseFormat`.

| `responseFormat` | Entry fields |
| --- | --- |
| `standard` (default) | The concise fields plus every other entry field: `providerRelationId`, `providerCardId`, `printingId`, `language`, `finish`, `primaryCategoryName`, `sortOrder`. |
| `concise` | `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`, `collectorNumber`, `oracleId`, `inDeck`. |

`zones` (`commander`, `main`, `sideboard`, `maybeboard`) limits the entries; omit it for every zone. Entries are
ordered by zone in that order, then card name, then relation ID. `pageSize` and `nextCursor` count the chosen entries,
and `shownEntryCount` says how many there are. A deck that changed between pages is refused with `cursor-stale`. The
summary on every page covers the whole deck whatever `zones` asks for (entry, card, and category counts,
`cardsByZone`, `duplicates`, `repeatsOutsideMain`, and the Commander `size` fact). A cut page's `narrow` says
which entries are shown, how to read the rest, and to pass a smaller `pageSize` or `responseFormat: "concise"` to fit
more.

### Local deck pages

Every `local_deck_get` page carries deck-wide totals: `entryCount`, `cardCount`, and `cardsByZone`. A cut page's
`narrow` says "This page shows N of M entries; the deck is not missing the rest. Follow nextCursor." and then how to
get smaller pages (a smaller `pageSize`, or fewer `fields` when the page has card data). The
`local_deck_apply_changes` summary carries the same totals.

`responseFormat` is `standard` (the default; every stored entry field plus the category assignment rows) or
`concise` (`entryId`, `cardName`, `quantity`, `zone`, and `categoryNames` only). With `include`, concise entries still add
`cardData`, `card`, and `tags`. `cardNames` (1 to 100 names) returns only the matching entries and lists the names with
no match in `cardNamesNotFound`. `totalEntries` and `nextCursor` count the matching entries; `entryCount`,
`cardCount`, and `cardsByZone` always cover the whole deck. The cursor is tied to `cardNames`
and `responseFormat` along with `include` and `fields`, so a cursor with a changed filter or format is refused.

`zones` and `categoryNames` filter the same way and are tied into the cursor too (`DeckCardViewCoordinator`):

- `zones` takes zone codes (`commander`, `main`, `sideboard`, `maybeboard`). Any non-empty code is accepted; a code that matches no
  entry is listed in `zonesNotFound`. An empty list or a blank code is refused as `invalid-zones`.
- `categoryNames` takes 1 to 100 names, matched ignoring case and surrounding space against an entry's primary and
  non-primary categories. A bad list is refused as `invalid-category-names`.
- Values inside one filter combine with OR, and the three filters (`cardNames`, `zones`, `categoryNames`) combine
  with AND. `zonesNotFound` and `categoryNamesNotFound` list the values that match no entry of the whole deck, as
  `cardNamesNotFound` does. No match is a normal result with no entries.
- `totalEntries` and `nextCursor` count the matching entries; `entryCount`, `cardCount`, and `cardsByZone` still cover the
  whole deck. With `include`, card data is read for the matching entries only. A concise read of a 347-entry deck with
  `categoryNames: ["Maybeboard"]` returns its 13 maybeboard entries in one page.

### Archidekt difference rows

`archidekt_sync_diff`, `archidekt_pull_preview`, and `archidekt_push_preview` take `responseFormat`, which is
`standard` (the default) or `concise`; any other value is refused with `invalid-response-format`.
`archidekt_snapshot_restore_preview` does not take it, because its one difference row holds two content
fingerprints, not entry JSON.

| `responseFormat` | Difference row |
| --- | --- |
| `standard` | `path`, `state`, and the whole `baselineValue`, `localValue`, and `remoteValue` (the JSON of one entry, category, or text value). |
| `concise` | `path`, `state`, and `fields`: one `{ name, baseline, local, remote }` per field whose values differ. For an added or removed row, the fields of the side that exists. A row for one text value, such as the deck name, lists that value under its last path segment. |

The format changes only how rows are shown. The `previewFingerprint`, the guards, the summary, the operations, and
`applyAvailable` are computed before formatting and are the same in both formats. Paging measures the rows of the
format returned, so a concise page holds more rows; `nextCursor` keeps its meaning, and the `narrow` text
says to pass `responseFormat: "concise"` again on the next page. An 18-edit push preview on a 347-entry fake deck is
36,996 characters in `standard` (cut at 17 of 19 rows) and 6,923 in `concise` (all 19 rows)
(`ArchidektConciseDifference.cs`, `ArchidektResultPaging.cs`). The pull and push previews also return `applyOptions`, the
options the apply must repeat, and `archidekt_push_preview` returns `providerRequestLimit` beside
`predictedProviderRequests` (see [MCP surface](architecture/mcp-surface.md)).

## Historical: `detailLevel`

> Historical reference: these removed legacy short-output aliases are not a
> rewrite compatibility requirement. Each approved child PLC defines its output
> limits and schema; stable `0.9.0` does not carry `compact` or workspace
> compatibility aliases just to preserve the old API.

Tools that offered `detailLevel` used one shared vocabulary:

| Value | Use |
| --- | --- |
| `summary` | Small bounded response for routine agent loops. |
| `normal` | Bounded response with supporting evidence or compact rows. |
| `full` | Raw or full-fidelity model payload. |

The released `0.8.x` server accepts legacy `compact` inputs as `summary`, and
`includeWorkspace=true` as a compatibility shortcut for `detailLevel=full`.
An older plan called this a `0.10.0` deprecation window; the clean-break `0.9.0`
program supersedes that forward-looking version claim. Maintenance of the
released legacy line may keep the aliases, but rewrite code does not reproduce
them.
