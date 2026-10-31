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
`local_deck_identity_reconcile_preview`, and `local_deck_category_rules_preview` page with `offset` (the
fingerprint and apply token cover the whole preview), `local_deck_export_bundle` returns one chunk of one artifact by
character `offset`, and the Scryfall card tools cut a page to the limit in every format. A paged deck result (the
local and Archidekt deck reads and the import preview) also cuts its free text: the name to 1,000 UTF-8 bytes on every
page (`nameTruncated`), and the description to 8,000 UTF-8 bytes on the first page only (`descriptionTruncated` and
`descriptionLength`), so a long description cannot push every page over the limit.

Every paged result follows one offset rule: an `offset` from 0 up to the number of rows is valid (at the number of
rows the page holds no row), and a negative offset or one past the end is refused with `invalid-offset` and the size
of the list. A result with two lists (operations and differences) has one offset for each list, and the next offsets
are given for both. The three local previews, the push and pull previews, and the snapshot restore preview take an
optional `expectedPreviewFingerprint` on later pages; pass the fingerprint of the first page and a preview that
changed in between is refused with `preview-changed` instead of paging a different preview.

`card_lists_compare` takes the same `responseFormat` and `fields`, and pages its result with a `nextCursor`; a page
that would pass the size limit keeps the leading cards that fit and sets `truncated` and `narrow`. `card_list_query`
returns groups and, when `rows` is asked for, a page of rows. Groups are cut first to a fixed share of the limit
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

`archidekt_deck_get` and `archidekt_snapshots_get` (kind `get`) take `zones` and `responseFormat`.

| `responseFormat` | Entry fields |
| --- | --- |
| `concise` (default) | `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`, `collectorNumber`, `oracleId`, `inDeck`. |
| `full` or `standard` | The concise fields plus every other entry field: `providerRelationId`, `providerCardId`, `printingId`, `language`, `finish`, `primaryCategoryName`, `sortOrder`. |

`zones` (`commander`, `main`, `sideboard`, `maybeboard`) limits the entries; omit it for every zone. Entries are
ordered by zone in that order, then card name, then relation ID. `offset` and `nextOffset` count the chosen entries,
`shownEntryCount` says how many there are, and a bad offset is refused with `invalid-offset` naming that count. The
summary on every page covers the whole deck whatever `zones` asks for (entry, card, and category counts,
`cardsByZone`, `duplicates`, `repeatsOutsideMain`, and the Commander `size` fact). A cut page's `narrow` says
which entries are shown, how to read the rest, and to pass `responseFormat: "full"` for every field.

### Local deck pages

Every `local_deck_get` page carries deck-wide totals: `entryCount`, `cardCount`, and `cardsByZone`. A cut page's
`narrow` says "This page shows N of M entries; the deck is not missing the rest. Follow nextCursor." and then how to
get smaller pages (a smaller `pageSize`, or fewer `fields` when the page has card data). The
`local_deck_apply_changes` summary carries the same totals.

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
