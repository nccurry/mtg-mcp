# Deck Analysis Ergonomics Requirements

## Purpose

A user asked an agent to check five Archidekt decks for the commander Wilson,
Refined Grizzly, one deck per color pair: which green or artifact cards from
any deck's main, sideboard, or maybeboard belong in the others. The server
already has the tools to answer this cheaply: `card_list_query` and
`card_lists_compare` both read an `archidekt-deck` source directly. The agent
never found them. It read every entry of every deck instead, then had no
filter for "fits mono-green" or "green or artifact" once it did find the
query tool.

The required outcome: an agent that starts from an Archidekt deck question
reaches the card-list tools first, can express the usual Commander filters in
one call, and pays fewer tokens when it does read a whole deck.

## Scope

- In scope:
  - server instruction and tool description text that points Archidekt and
    deck questions to `card_list_query` and `card_lists_compare`;
  - two `card_list_query` filter additions: a "fits within" operator for list
    fields and one OR group;
  - a smaller concise shape for `archidekt_deck_get` and
    `archidekt_snapshots_get` (kind `get`);
  - a smaller input schema for the three tools that take a card-list source
    (`card_list_query`, `card_lists_compare`, and `stats_castability`), with
    the same accepted inputs;
  - clearer wording for `inDeck`;
  - `docs/reference.md`, `CHANGELOG.md`, and surface tests for each change.
- Out of scope:
  - new tools, toolsets, or source kinds; comparing more than two lists in one
    call (README open question);
  - changes to `standard` output, write tools, operation modes, or the
    Archidekt HTTP contract;
  - skills, prompts, or the plugin, which
    [deckbuilding-agent-guidance](../deckbuilding-agent-guidance/README.md)
    owns.
- Constraints:
  - tool count stays 86 / 73 / 59 by mode;
  - every description starts with its data source and stays within 4,096
    characters; server instructions stay within
    `ServerInstructions.MaximumLength` (2,000);
  - pages aim for 40,000 characters and never pass 50,000 (the limit from
    [agent-feedback-fixes](../../completed/agent-feedback-fixes/README.md));
  - no compatibility aliases; a removed field follows
    [versioning](../../../../versioning.md) or an owner-approved clean break.

## Current System And Prerequisites

Inspected `main` at `ece3ba9` on 2026-11-07. The code is unchanged since
`d532ae5` (`0.15.0` plus a README rewrite); the two later commits edit only
planned packets.

What the session did (2026-11-07, one Claude Code session, mtg-mcp `0.15.0`):

| Step | Observed |
| --- | --- |
| Tool search "archidekt deck" | Returned 10 `archidekt_*` tools, most of them writes. `card_list_query` and `card_lists_compare` did not appear: their names have no `archidekt` and their descriptions start with "Scryfall:". |
| Five `archidekt_deck_get` calls, `concise`, `pageSize: 200` | Six calls: the Voltron deck (230 entries, 20 categories) was cut at 159 entries by the size limit. Each result also carried every category's `providerCategoryId`, `includedInPrice`, `isPremier`, and `sortOrder`, and an `extensions` object with the owner's avatar URL, the featured image URL, `viewCount`, and `commentRoot`. |
| Card facts | Entries carry no type, color, or mana value, so "green or artifact cards" across about 800 entries would have needed many `scryfall_cards_get` calls. |
| One `card_list_query` on `archidekt-deck` 28039545, zones maybeboard and sideboard, `colorIdentity in ["G"]` | Answered in one call: 38 cards with zone and type line. It also returned Decimate, Domri, and other green-red cards, and could not return colorless artifacts in the same call. |
| Loading the `card_lists_compare` and `card_list_query` schemas | Each writes out the whole source union three times (at the source, at a nested `compare.left`, and at that one's `right`) before later copies refer back with JSON-pointer `$ref`s. `card_lists_compare` does this for `left` and `right` separately. They were the two largest tool definitions loaded in the session. |

The same gap appears in the
[deckbuilding-agent-guidance](../deckbuilding-agent-guidance/README.md) usage
count: one session compared a deck with an Archidekt snapshot using a
PowerShell script, and `card_lists_compare` with Archidekt sources was not
used.

Code read:

- List-field matching: [ScryfallListQuery.cs](../../../../../src/MtgMcp.Scryfall/ScryfallListQuery.cs),
  `Matches`: for a list field every operator means "the card's list holds any
  of the values", so `colorIdentity in ["G"]` matches `["G","R"]`, and an
  empty list (a colorless card) never matches. List fields accept only `in`
  and `contains`; any other operator is refused with `invalid-operator`. Only
  `in` takes an array, and an empty array is refused with `invalid-value`.
  `where` conditions combine only with AND. Some list fields (`colors`,
  `colorIdentity`, `keywords`) return `[]` when empty; others (`games`,
  `legalIn`, `bannedIn`, `category`) can be null, which counts as a missing
  value.
- [CardListCoordinator.cs](../../../../../src/MtgMcp.App/CardLists/CardListCoordinator.cs)
  reads `where` in three more places: the cursor fingerprint
  (`RequestHash.Of`), `NeedsFacts` (loads tag and price data only when a
  field needs it), and `FindEntryIdPath` (refuses `entryId` for Archidekt
  sources). `ScryfallListQuery.MissingFor` reports every filtered field in
  `missingValues`.
- Operator lists in text: the `card_list_query` description ("where holds
  filters that must all match (ops eq, ne, lt, lte, gt, gte, in, contains)",
  `CardListTools.cs`) and the `Op` property description ("list fields take
  in and contains", `CardListModels.cs`).
- Concise deck entries and header:
  [ArchidektResultPaging.cs](../../../../../src/MtgMcp.App/Archidekt/ArchidektResultPaging.cs)
  builds the header with `deck.Categories` (typed `RemoteDeckCategory`) and
  `deck.Extensions` (up to 8,000 bytes) in both formats, for
  `archidekt_deck_get` and `archidekt_snapshots_get` alike (`PageDeck`).
  [ArchidektReadResults.cs](../../../../../src/MtgMcp.App/Archidekt/ArchidektReadResults.cs)
  `ArchidektDeckEntryView` is the concise entry, and
  `ArchidektStandardDeckEntryView` inherits its `setCode` and
  `collectorNumber`. The concise `narrow` text mentions the printing.
  `archidekt_deck_get` defaults to `responseFormat: "standard"`.
- `inDeck`: `ArchidektJsonContract.IsInDeck` in
  [ArchidektContractMapper.cs](../../../../../src/MtgMcp.Archidekt/ArchidektContractMapper.cs)
  is Archidekt's `includedInDeck` for the entry's primary category, or its
  first category when none is primary; an entry with no category, or whose
  category has no flag, counts as in the deck. `MapZone` assigns a
  zone-named category (such as Sideboard) to that zone before the inclusion
  check, so the current description's "(it is then in the maybeboard zone)"
  and the matching line in `docs/reference.md` are wrong for such entries.
  Archidekt decks may mark a Sideboard category as included, so sideboard
  entries can show `inDeck: true` while `size.mainAndCommander` leaves them
  out.
- Source union: [CardListSourceInput.cs](../../../../../src/MtgMcp.App/CardLists/CardListSourceInput.cs)
  is a recursive union of seven kinds (six leaf sources and `compare`),
  taken by `card_list_query`, `card_lists_compare` (as two parameters), and
  `stats_castability`. Schema shaping runs per node and per parameter
  through the `AIJsonSchemaCreateOptions` hooks in `ToolRegistration`, which
  cannot see the whole root. No test bounds input schema size; descriptions
  are bounded by `ToolSchemaSurfaceTests.MaximumDescriptionLength` (4,096).
- Instruction jobs: [ServerInstructions.cs](../../../../../src/MtgMcp.App/Hosting/ServerInstructions.cs),
  `Jobs`, line "Analyze a card list (it groups by subtypes, such as Aura, and
  by tags)". The live instructions are 1,666 of 2,000 characters.

Prerequisite: none outside this repository. P4 depends on the MCP C# SDK
passing a `$defs` section through to `tools/list` unchanged; the risk table
names the check.

## Requirements

IDs are permanent: never renumber, reuse or rename them.

| ID | Priority | Requirement | Check |
| --- | --- | --- | --- |
| REQ-001 | Must | The server instruction job for card lists says it reads saved searches, local decks, and Archidekt decks and snapshots directly, and names filtering by card facts and comparing two decks. The instructions stay within `ServerInstructions.MaximumLength`. | `ServerInstructionsTests` asserts the job text names Archidekt decks and the length limit → pass; the P1 note records the new length (1,666 today), so the merge with deckbuilding-agent-guidance REQ-015 can check the shared 2,000 budget. |
| REQ-002 | Must | The `archidekt_deck_get` and `archidekt_snapshots_get` descriptions each add one sentence: to filter entries by type, color, or other card facts, or to compare decks, use `card_list_query` or `card_lists_compare` with an `archidekt-deck` or `archidekt-snapshot` source instead of reading every entry, and to read every entry, pass `responseFormat: "concise"`. | A surface test asserts both descriptions contain `card_list_query` and `card_lists_compare` and stay within 4,096 characters → pass. |
| REQ-003 | Must | The first sentence of the `card_list_query` and `card_lists_compare` descriptions names Archidekt decks and local decks as list sources, after the "Scryfall:" source prefix. | A surface test asserts the first sentence of each contains "Archidekt" → pass. Manual: in a fresh Claude Code session, a tool search for "archidekt deck" lists `card_list_query` or `card_lists_compare` in its top 10; result recorded under P1. |
| REQ-004 | Must | `card_list_query` `where` accepts operator `within` on list fields (`colors`, `colorIdentity`, `producedMana`, and the other list fields): a card matches when every value in its list is one of the given values, compared without case. Its value must be an array; anything else is refused with `invalid-value` at `.value`. Unlike `in`, an empty array is allowed. A field that returns an empty list (`colors`, `colorIdentity`, `keywords`) matches, so colorless cards fit any identity, and `within: []` matches only empty lists; a null value is missing and does not match. `within` on a number or text field is refused with `invalid-operator`, path `where[i].op`, as other wrong operators are today. Existing operators keep their meaning. | `ScryfallListQueryTests`: with `colorIdentity within ["G"]`, Llanowar Elves and Sol Ring match and Decimate does not; `colorIdentity within []` matches Sol Ring only; `within` with a string value is refused at `.value`; `manaValue within [1]` is refused with `invalid-operator` at `.op` → pass. |
| REQ-005 | Must | `card_list_query` accepts `whereAny`, a list of 2 to 10 conditions of the same shape as `where`. A card passes `whereAny` when at least one of its conditions matches, and it must also pass every `where` condition. A `whereAny` with fewer than 2 or more than 10 conditions, or an invalid condition, is refused with `invalid-input` and the path of the offending item. Omitting `whereAny` keeps today's results. `whereAny` takes part everywhere `where` does: the cursor fingerprint, loading tag and price data for its fields, and the `entryId` refusal for Archidekt sources (path `whereAny[i].field`). Its fields appear in `missingValues` with the count of cards missing each one; such a card can still pass through another `whereAny` condition, and the description says so. | `ScryfallListQueryTests` and `CardListQueryTests`: the request in [Expected Experience](#expected-experience) over a fixture with Rancor, Decimate, Sol Ring, and Rogue's Passage returns Rancor and Sol Ring only; a request without `whereAny` returns the same rows as before; one-item and eleven-item lists are refused with paths; `whereAny` with `tags contains` on a deck source returns tagged rows; a cursor from one `whereAny` is refused with `invalid-cursor` for another; `entryId` in `whereAny` on an Archidekt source is refused at `whereAny[0].field`; `missingValues` lists a `whereAny` field → pass. |
| REQ-006 | Must | With `responseFormat: "concise"`, `archidekt_deck_get` and `archidekt_snapshots_get` (kind `get`) return entries with only `cardName`, `quantity`, `zone`, `categoryNames`, `oracleId`, and `inDeck`; the first page's `categories` hold only `name` and `includedInDeck`; and the header has no `extensions` or `extensionsOmitted`. The concise `narrow` text no longer mentions the printing. `standard` output is unchanged. | `ArchidektDeckReadTests`: a fixture deck read in concise has exactly those entry keys, those category keys, and no `extensions`; the same deck in standard matches today's output → pass. Waits on the README open question about removing fields. |
| REQ-007 | Must | A concise read of a 250-entry Archidekt deck with 20 categories and about 7,000 bytes of extensions, with `pageSize: 200`, returns 200 entries on its first page and stays within `ResultSizeLimit.MaximumBytes` (40,000 bytes). | `ToolBudgetWorld` gains parameters for the category count and an extensions payload; the test with `remoteDeckSize: 250`, 20 categories, and 7,000 bytes of extensions returns 200 entries within the limit. The P3 note records that the same test returns fewer than 200 entries on the code before P3 → pass. |
| REQ-008 | Must | The input schemas of `card_list_query`, `card_lists_compare`, and `stats_castability` each define the list-source union once, and every other place it appears (each parameter, nested `compare` sides) refers to that definition. In `card_list_query`, `whereAny` items refer to the same item definition as `where` items. Every `$ref` in the final schema resolves. Each of the three input schemas, serialized as `tools/list` returns it and measured after P2, is at most 8,000 characters. Accepted inputs, nesting depth (3), ID limits (150 per list, 300 per request), and refusals do not change. | `ToolSchemaSurfaceTests` measures the three schemas → each at most 8,000 characters (the P4 note records the sizes before and after); `ToolSchemaSdkTests` reads each schema through the SDK client and resolves every `$ref`; the existing `CardListCompareTests`, `CardListQueryTests`, and castability tests pass unchanged → pass. |
| REQ-009 | Should | The `archidekt_deck_get` description and `docs/reference.md` say `inDeck` is Archidekt's `includedInDeck` for the entry's primary category (its first category when none is primary), that an entry with no category or no flag counts as in the deck, and that a sideboard entry can be `inDeck: true` while `size.mainAndCommander` counts only commander and main. Both drop the claim that an excluded entry is always in the maybeboard zone. | Surface test asserts the description mentions `includedInDeck` and not "maybeboard zone"; doc review in P5 → pass. |
| REQ-010 | Must | `docs/reference.md` and `CHANGELOG.md` describe every change above (removed concise fields under Breaking if the owner approves REQ-006 as written), surface baselines are updated, and the tool count stays 86 / 73 / 59. | `task ci` passes; `ToolSchemaSurfaceTests` tool count → 86; doc review against this table in P5. |
| REQ-011 | Must | The `card_list_query` description and the `Op` property description list `within` and describe `whereAny`; the description stays within 4,096 characters after REQ-003, REQ-004, and REQ-005 text is added. | A surface test asserts both texts name `within` and the description names `whereAny`; the existing description length test passes → pass. |

## User Experience And Public Surface

| Surface | Action or input | Expected result | Compatibility |
| --- | --- | --- | --- |
| Server instructions | Client reads instructions at connect | Card-list job names Archidekt and local deck sources | Text only |
| `archidekt_deck_get`, `archidekt_snapshots_get` descriptions | Agent reads tool list | One sentence pointing to the card-list tools; `inDeck` meaning | Text only |
| `card_list_query`, `card_lists_compare` descriptions | Agent searches tools for "archidekt" or "deck" | First sentence names Archidekt and local decks | Text only |
| `card_list_query` `where` | `op: "within"` on a list field | Card matches when its list is a subset of the values | Additive operator |
| `card_list_query` | `whereAny` list | Cards that match any listed condition and all `where` conditions | Additive argument |
| `archidekt_deck_get`, `archidekt_snapshots_get` | `responseFormat: "concise"` | Entries without `setCode` and `collectorNumber`; header without `extensions` and without four category fields | Breaking for concise readers if approved; `standard` unchanged |
| `card_list_query`, `card_lists_compare`, `stats_castability` input schemas | Client loads the tool | Same inputs, schema at most 8,000 characters | Shape of the schema document changes; accepted JSON does not |

## Expected Experience

### API

"Cards from this deck's maybeboard and sideboard that fit a mono-green deck
and are either green or artifacts":

```json
{
  "source": {"kind": "archidekt-deck", "remoteDeckId": "28039545", "zones": ["maybeboard", "sideboard"]},
  "where": [{"field": "colorIdentity", "op": "within", "value": ["G"]}],
  "whereAny": [
    {"field": "colorIdentity", "op": "contains", "value": "G"},
    {"field": "cardTypes", "op": "contains", "value": "Artifact"}
  ],
  "groupBy": ["zone"],
  "rows": {"fields": ["name", "zone", "typeLine"]}
}
```

Result: mono-green cards such as Rancor and Heroic Intervention, and colorless
artifacts in those zones; no Decimate, no Domri, Anarch of Bolas, and no
colorless lands such as Rogue's Passage.

Refusal, with today's wrong-operator reason and message:

```json
{"kind": "invalid-input", "reasonCode": "invalid-operator", "path": "where[0].op",
 "message": "Field 'manaValue' accepts only these operators: eq, ne, lt, lte, gt, gte, in."}
```

Concise deck read, first entry and first category:

```json
{"cardName": "Arcane Signet", "quantity": 1, "zone": "main",
 "categoryNames": ["Artifact"], "oracleId": "0bc7f093-...", "inDeck": true}
{"name": "Artifact", "includedInDeck": true}
```

## Public Contracts

- `card_list_query` request: `where` items gain the operator value `within`
  (list fields only); a new optional `whereAny` array holds 2 to 10 items of
  the `where` item shape. Evaluation: all `where` items AND (any `whereAny`
  item). Refusals use `invalid-input` with `path`, as
  [MCP surface](../../../../architecture/mcp-surface.md) requires.
- `archidekt_deck_get` and `archidekt_snapshots_get` concise result: entry and
  category fields as REQ-006 lists; summary, fingerprints, evidence, paging
  fields, and `narrow` unchanged.
- `card_list_query`, `card_lists_compare`, and `stats_castability` input
  schemas: same accepted JSON; the document uses a shared definition for the
  source union.

## Quality Attributes

| Attribute | Scenario | Pass/fail measure |
| --- | --- | --- |
| Result size | Concise read of a 250-entry, 20-category Archidekt deck | 200 entries on page one, under 40,000 characters (REQ-007) |
| Definition size | `tools/list` entry for each card-list tool | Input schema at most 8,000 characters (REQ-008) |
| Determinism | `where` without `whereAny`, old operators | Same rows and order as before (REQ-005) |

## Traceability

Phase state lives in the README `## Phases` table.

| Requirement | Design section | Phase |
| --- | --- | --- |
| REQ-001 to REQ-003, REQ-009 | [Discovery text](SADD.md#discovery-text) | P1 |
| REQ-004, REQ-005, REQ-011 | [Filters](SADD.md#filters) | P2 |
| REQ-006, REQ-007 | [Concise deck reads](SADD.md#concise-deck-reads) | P3 |
| REQ-008 | [Schema size](SADD.md#schema-size) | P4 |
| REQ-010 | All | P5 |

## Risks And Assumptions

| Item | Type | Affected requirements or phases | Blocks work? | Owner | Resolution or permitted assumption |
| --- | --- | --- | --- | --- | --- |
| A root `$defs` section may not survive the SDK, or a client may not resolve it; moving nodes can leave today's JSON-pointer `$ref`s dangling | Risk | REQ-008, P4 | Yes: P4 design | P4 implementer | First step of P4: a `ToolSchemaSdkTests` case reads a hand-shaped schema with `$defs` through the SDK client and resolves every `$ref`. Clients already resolve the JSON-pointer `$ref`s in today's schemas (seen in this session). The post-pass rewrites every existing pointer. If `$defs` fails, the owner chooses the fallback in [SADD.md](SADD.md#decisions-and-alternatives). |
| Sibling REQ-011 (`tokenKeywords`, `tokenTypes`) grows the field enums in the same schemas | Risk | REQ-008 | No | @nccurry | The 8,000 budget is measured on whichever lands later; the README open question on order covers it. |
| Agents do not read the new description text | Risk | REQ-001 to REQ-003 | No | @nccurry | The manual REQ-003 check measures discovery once; skills from deckbuilding-agent-guidance are the stronger route. |
| Removing concise fields breaks a client script that reads `setCode` | Risk | REQ-006 | Yes: P3 | @nccurry | README open question. Every REQ-006 removal is a removed field, so without approval P3 stops and the owner chooses the fallback in [SADD.md](SADD.md#decisions-and-alternatives). |
