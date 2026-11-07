# Deck Analysis Ergonomics Architecture And Design

## Design Goal

Make the existing card-list tools the path agents take for deck questions,
using text, two small filter additions, and smaller outputs and schemas. No
tool, source kind, or toolset is added. The tradeoff: description text is
easy to skip, so the filters and the size limits carry the measurable part of
the change.

## Context And Boundaries

- `MtgMcp.Scryfall` owns list-query evaluation (`ScryfallListQuery`):
  operators, field kinds, and matching. REQ-004 and REQ-005 change it.
- `MtgMcp.App` owns tool descriptions, server instructions, the card-list tool
  inputs and their schema (`CardListTools`, `CardListSourceInput`), and the
  Archidekt result views (`ArchidektResultPaging`, `ArchidektReadResults`).
- `MtgMcp.Archidekt` owns the provider contract and `IsInDeck`; nothing there
  changes. The extensions and categories are still read and kept for
  `standard` and for sync; only the concise view leaves them out.
- Public contracts are in [SRD.md](SRD.md#public-contracts).

## Decisions And Alternatives

| Decision or option | Status | Reason |
| --- | --- | --- |
| D-1: Point agents to the card-list tools with instruction and description text | Proposed | Cheapest change that reaches every client. Skills in deckbuilding-agent-guidance reach only plugin users. |
| A generic "deck analysis" router tool | Rejected | Product rules forbid routers and intent inference (AGENTS.md). |
| Renaming `card_list_query` to carry an `archidekt` prefix | Rejected | The tool reads seven source kinds; a provider prefix would misstate it and break the one-prefix-per-source naming rule. |
| D-2: New operator `within` for "list is a subset of values" | Proposed | Says what it checks, and leaves `in` and `contains` unchanged. |
| Change `in` on list fields to mean subset | Rejected | Silently changes results of existing requests. |
| Reuse `lte` on list fields as subset | Rejected | Overloads a number comparison; harder to read and to document. |
| D-3: One OR group, `whereAny`, beside `where` | Proposed | Covers "this or that" (green or artifact) with a flat argument and a small schema addition. |
| Nested boolean groups inside `where` (`anyOf`, `allOf`, `not`) | Rejected | A recursive union grows the schema this packet is shrinking, and two-level logic has not been asked for. Revisit if agents need two OR groups. |
| Leave OR to the Scryfall query string | Rejected | Works only for `scryfall-snapshot` sources, not for decks. |
| D-4: Concise Archidekt reads drop printing, category provider fields, and `extensions` | Proposed; waits on the owner (README open question) | Concise exists for agents reading many entries; none of these fields answers a deck question, and `oracleId` stays for joins with card-list sources. |
| Fallback for D-4: keep today's concise fields and add a `fields` choice for entries | Alternative if D-4 is refused | Additive and compatible, but the default stays large, and agents rarely set optional size fields. |
| D-5: Define the source union once under a root `$defs` in each of the three schemas and refer to it by `$ref`, applied as one post-pass on the final input schema | Proposed | Keeps every accepted input and its validation. The per-node `AIJsonSchemaCreateOptions` hooks cannot see the whole root, so the hoist runs after the SDK builds the schema and rewrites every existing JSON-pointer `$ref`. JSON-pointer `$ref`s already appear in today's schemas and clients resolve them. |
| Fallback for D-5: a nested `compare` takes only leaf sources (depth 2) | Alternative if `$defs` fails in the SDK or a client | Smaller schema without `$defs`, but it lowers the documented nesting depth from 3 to 2, a breaking input change that needs the owner. |
| D-6: `whereAny` fields are reported in `missingValues` like `where` fields | Proposed | One rule for both lists. The count is a fact about the data; the description says a card missing one `whereAny` field can still match another. |
| Leave `whereAny` fields out of `missingValues` | Rejected | Hides cards that lack tag or price data, which the agent needs to know about. |

## Fixed Constraints And Implementation Choices

- Descriptions start with their data source; `card_list_query` and
  `card_lists_compare` keep "Scryfall:" because card facts come from Scryfall
  data, and name the deck sources after it.
- Refusals use `invalid-input` with `path`
  ([MCP surface](../../../../architecture/mcp-surface.md)); no new reason code.
- Matching stays case-insensitive and ordinal, as `Matches` does today.
- Suggested, not required: implement `within` by adding it to the list-field
  operators in `AllowedOperators`, giving it the array parsing `in` has but
  not the empty-array refusal, and adding one branch in `Matches` before the
  list shortcut (which today ignores the operator). Implement `whereAny` by
  validating it with the same `ValidateCondition` and keeping a second check
  list in the plan.
- Suggested, not required: for D-5, a post-pass on
  `McpServerTool.ProtocolTool.InputSchema` in `ToolRegistration.Create` for
  the three tools, rather than hand-writing JSON.

## Building Blocks

| Component | Responsibility | Changed behavior |
| --- | --- | --- |
| `ServerInstructions.Jobs` | Instruction text | Card-list job names deck and Archidekt sources (REQ-001) |
| `ArchidektReadTools` | Archidekt read descriptions | Pointer sentence; `inDeck` wording (REQ-002, REQ-009) |
| `CardListTools`, `CardListModels` | Card-list tool descriptions and inputs | First sentence; operator text; `whereAny` input (REQ-003, REQ-005, REQ-011) |
| `CardListCoordinator` | Request fingerprint, data loading, source checks | `whereAny` in `RequestHash.Of`, `NeedsFacts`, and `FindEntryIdPath` (REQ-005) |
| `ScryfallListQuery` | Filter evaluation | `within` operator; any-of group; `missingValues` for `whereAny` (REQ-004, REQ-005) |
| `ToolRegistration` | Tool schema shaping | Post-pass that hoists the source union for `card_list_query`, `card_lists_compare`, and `stats_castability` (REQ-008) |
| `ArchidektResultPaging`, `ArchidektReadResults` | Deck pages | Concise entry and header views; concise `narrow` text (REQ-006, REQ-007) |
| `ToolBudgetWorld` (tests) | Fake remote decks | Category-count and extensions parameters (REQ-007) |

## Runtime And Data Flow

### Discovery text

No runtime change. Instruction and description strings change; their length
tests stay in force.

### Filters

`card_list_query` validates `where` and `whereAny` items with the same
condition validator, then for each resolved card: keep it when every `where`
check matches and, if `whereAny` is present, at least one of its checks
matches. A card with no value fails that one condition; under `whereAny` it
can still pass through another (D-6). For `within`, a card's list value is
checked element by element against the given values; an empty list passes and
a null value is missing.

`CardListCoordinator` treats `whereAny` like `where` in the three places it
reads conditions: the cursor fingerprint, so a cursor cannot be replayed with
a different OR group; `NeedsFacts`, so tag and price data load for `whereAny`
fields; and `FindEntryIdPath`, so `entryId` is refused for Archidekt sources
in either list.

### Concise deck reads

The page builder (`PageDeck`, shared by `archidekt_deck_get` and
`archidekt_snapshots_get`) picks the header and entry view by format. Concise
builds a header with a new category view (name and `includedInDeck`) and no
extensions; the size measure uses the concise header, so more rows fit
(REQ-007). Today `ArchidektStandardDeckEntryView` inherits `setCode` and
`collectorNumber` from the concise entry, so the two records are split: the
concise view drops them and the standard view keeps them. The concise
`narrow` text drops its mention of the printing. `standard` output does not
change. The tools' output schema is the generic operation-result schema, so
it does not change either.

### Schema size

After the SDK builds each input schema for `card_list_query`,
`card_lists_compare` (two parameters), and `stats_castability`, a post-pass
moves the source union to a root `$defs` entry, points every place it
appeared at it, and rewrites each existing JSON-pointer `$ref` so none
dangles. In `card_list_query`, `where` and `whereAny` items share one item
definition. Input binding and validation code do not change, so depth and ID
limits keep their current checks and refusals. P4 runs after P2, so the
budget covers `whereAny`.

## Quality And Safety

- Untrusted Archidekt text: concise results still carry user-written category
  names, and descriptions keep the existing untrusted-data sentence.
- Size: REQ-007 and REQ-008 add measured limits; the 50,000-character ceiling
  is unchanged.

## Test Design

- Unit: `ScryfallListQueryTests` for `within` and `whereAny` semantics and
  refusals.
- App: `CardListQueryTests` for the MCP request shape; `ArchidektDeckReadTests`
  for concise and standard views; `ServerInstructionsTests`.
- Surface: `ToolSchemaSurfaceTests` for description content, the three input
  schema sizes, and the tool count; `ToolSchemaSdkTests` for `$ref`
  resolution through the SDK client.
- Budget: a `ToolBudgetWorld` test with a 250-entry, 20-category remote deck
  and about 7,000 bytes of extensions, shown to fail before P3.
- Coordinator: `CardListQueryTests` for `whereAny` data loading, cursor
  binding, and the Archidekt `entryId` refusal.
- Manual: the REQ-003 tool-search check in a fresh Claude Code session.
