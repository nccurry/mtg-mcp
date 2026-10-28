# Software Requirements: Focused Agent Tool Surface

## Audience and Purpose

This document is for maintainers and implementing agents. It states what the
revised MCP surface must do and how each requirement is checked.

Tool names here are today's working names. Phase 3 renames them to the final
names in [FIXTURES](FIXTURES.md#final-tool-names); from then on, tests and
error messages use the final names.

## Product Rule

The server returns facts, exact calculations, and seeded sampled results. The
agent chooses cards, judges quality, and decides what to try next. Where a
calculation needs a rule that involves judgment, such as which cards count as
ramp or when to mulligan, the agent supplies that rule in the request. The
server does not ship its own role definitions.

## Outcomes

- Every deckbuilding tool is available by default, and tool names make clear
  which data source each one uses.
- An agent finds cards of a given kind, including by official tags, in one
  search call.
- An agent sees a deck's cards with their fields and tags in one call.
- An agent combines or compares search results and decks, such as "search
  results not already in my Archidekt deck", without copying card lists.
- An agent gets a deck's curve, type mix, role counts, pips, color sources,
  and price totals in one call each, and a sorted card table for cut
  decisions.
- An agent gets Commander rule facts for a deck without judging them itself.
- An agent compares a deck with an earlier backup or Archidekt snapshot.
- An agent removes duplicate copies of a card across an Archidekt deck's
  groups, keeping one copy in the group the agent ranks highest, with one
  preview call, one local apply, and the existing push.
- A user gets the full Archidekt round trip by configuring credentials only.
  Provider tools without credentials stay hidden.
- Clients can check parameter values before a call.
- An agent can page through many cards without filling its context.
- An agent can test its own deck theories with repeatable sampling, with
  every play choice stated in the request, including colored mana costs.
- An agent gets the exact chance of casting every spell in a deck on curve
  from its own source groups, in one call.
- An agent gets Commander pairing and companion facts, and the card and combo
  facts that the Commander Brackets discuss, without a bracket verdict.
- An agent gets win rates and game counts for a playgroup's decks or players
  in one call, with sample sizes.
- A client can tell a failed call from a successful one, and an agent that
  has loaded no tools yet learns what the server is for.
- The team can show the change helped, using a fixed task set with held-out
  tasks.

## Scope

- Default selection of every toolset, credential gating for `archidekt` and
  `playgroup`, and `remote` as the default mode.
- A tool naming rule and the renames it requires.
- Tool merges for `decks`, `scryfall`, and `archidekt`.
- Descriptions that make Scryfall query syntax the main way to filter by tag.
- A card and tag view on `deck_get`.
- Removal of the `common-v1` category preset.
- Enum schemas for string choice parameters.
- Descriptions for statistics request fields.
- Card response formats for Scryfall card results.
- One tool, `card_lists_compare`, for exact set operations on card lists given
  by reference, including local backups and Archidekt snapshots as sources.
- One tool, `card_list_query`, to filter, group, count, sum, and sort any card
  list using installed card data, with mana pip and source counts.
- More card facts: tags, cheapest price across printings, Game Changer flag,
  and EDHREC rank.
- Commander rule facts in `deck_validate`.
- Current revision in conflict results.
- One new sampling tool, `stats_simulate`, with colored payment, and the
  retirement of `deck_on_curve_estimate`.
- One new exact tool, `stats_castability`, for on-curve castability of every
  spell in a card list.
- Commander pairing and companion facts in `deck_validate`.
- One new read tool, `deck_duplicates_preview`, that finds a card's copies
  across zones and categories and returns the `deck_apply_changes` edits that
  keep one copy, by an agent-ordered group priority.
- One new Commander Spellbook tool, `spellbook_deck_bracket_facts_get`, that
  returns Spellbook's per-card and per-combo bracket facts without its bracket
  label.
- One new Playgroup tool, `playgroup_games_stats_get`, that totals games by
  deck or player.
- More card fields: oracle text, every format legality, game availability,
  Reserved List, layout, and all Scryfall price columns.
- `isError` on failed results, server instructions, examples in complex tool
  descriptions, a fixed result size limit, untrusted-text notes, a
  confirmation hint on destructive remote writes, and a few MCP prompts.
- An agent task set with held-out tasks and a before/after record.

## Non-Scope

- Card recommendations, quality scores, or automatic deck changes.
- Full-game simulation, opponent models, or combat.
- Changes to provider HTTP contracts, how credentials are loaded or stored,
  or local storage formats, except one new Commander Spellbook read
  (`POST /estimate-bracket`). Playgroup statistics use the existing games
  list operation. Gating only checks whether credentials are present.
- Spellbook's `bracketTag` and any other bracket label. The new Spellbook tool
  drops it.
- Changes to `MtgMcp.Core` beyond what an enum type move strictly needs. Enum
  types belong in App or the owning adapter.
- Tool name aliases for removed tools.
- EDHREC data (inclusion, synergy, average decks, salt). It has no official
  public API.
- Card collection or ownership tracking.
- Bracket or power-level judgments.
- A deck revision history table.
- Playgroup redesign. Its 16 existing tools keep their names and inputs. Only
  shared checks that apply to every tool, such as enum schemas and `isError`,
  touch them. The one new tool, `playgroup_games_stats_get`, is additive.

## Public Surface Changes

| Surface | Action | Expected result | Compatibility |
| --- | --- | --- | --- |
| Default selection | Start the server with no options and no credentials. | `decks`, `scryfall`, `stats`, and `spellbook` in `remote` mode: 48 tools. | Wider than today (`decks`, `scryfall`, `stats` in `local` mode). |
| Credential gating | Start with `~/.mtg-mcp/archidekt.json` and `playgroup.json` present. | All six toolsets: 86 tools. | Archidekt and Playgroup no longer need `--toolsets`. |
| Missing credentials | Start with `--toolsets archidekt` and no Archidekt credentials. | `archidekt` stays off; the capabilities resource says `credentials-missing`; stderr names the setting to add. | Today the tools would appear and fail when called. |
| Read-only | `--mode read-only` | Every write tool is hidden: 35 tools without credentials, 59 with both. | Unchanged behavior; no longer the only way to limit tools. |
| Renamed tools | Call `local_deck_get` instead of `deck_get`. | Same behavior. | Breaking. The CHANGELOG maps every old name. |
| Merged tools | Call a new name such as `scryfall_cards_get`. | Same data as the old tools it replaces. | Old names return "unknown tool". The CHANGELOG maps each old name. |
| Tag search | `scryfall_search` with `query: "otag:ramp t:sorcery mv<=2 f:commander"` | Scryfall's matching cards. | No behavior change; descriptions now say so. |
| Deck card view | `deck_get` with `include: ["cards","tags"]` | Each entry has concise card fields and oracle tag slugs, or `cardData: "not-installed"` / `"unresolved"` (found entries carry `"found"`). | Additive. |
| Category preset | `deck_category_rules_preview` with `common-v1` | `invalid-input` naming inline rules as the replacement. | Breaking. |
| Unknown properties | Send a field a tool does not define. | `invalid-input` naming the field. | Breaking; ships in phase 3. |
| Enum parameters | Send `"freshnessPolicy": "refresh"`. | Same behavior as today. | Wire-compatible. Bad values are now rejected by the schema. |
| Card formats | Call `scryfall_search` with no format, or with `responseFormat: "concise"`. | Today's card rows by default; small rows when `concise` is asked for. | Default output unchanged. Breaking only for `includeRaw: true`, which becomes `responseFormat: "full"`. |
| `card_lists_compare` | Compare a search snapshot with an Archidekt deck using `difference`. | Counts, a page of result cards, unmatched entries, and source markers. | New tool. |
| `card_list_query` | Group a local deck by `manaValue` with `quantity`, `pips`, and `sum:priceUsd`. | One row per mana value with counts, pips by color, and price sums. | New tool. |
| `deck_validate` rules | `deck_validate` with `rules: "commander"`. | Structural checks plus size, commander, identity, singleton, and legality facts, each with `passed` and the entries involved, plus a Game Changer count and list. | Additive; without `rules` the result is unchanged. |
| Card facts | `responseFormat: "concise"` | Rows also carry `tags`, `cheapestPriceUsd`, `gameChanger`, and `edhrecRank`. | Additive. |
| Conflict results | Apply with a stale revision. | `conflict` with a `current` object holding the current `revision` and, for Archidekt, `remoteFingerprint`. | Additive. |
| `stats_simulate` | Send a deck, groups, rules, conditions, sample count, and seed. | Hit counts, rates, intervals, applied rules, seed, and fingerprint. Groups may declare the colors they produce, and a `can-cast` condition checks a colored mana cost. | New tool. |
| `deck_on_curve_estimate` | Removed in phase 6. | Use the `on-curve` preset of `stats_simulate`. | Breaking. Old seeds replay to the same result through the preset, including colored targets. |
| Failed results | Any call that returns `invalid-input`, `conflict`, `not-found`, `not-cached`, `unsupported`, or a provider failure. | The result has `isError: true`; the structured failure body is unchanged. | Additive flag. Clients that ignored failures now see them as errors. |
| Server instructions | Connect a client. | `initialize` (or `server/discover`) returns a short instructions block naming the server's jobs and the first tool for each. | Additive. |
| `deck_validate` pairing and companion | `deck_validate` with `rules: "commander"` on a deck with two commanders or a `companion` zone entry. | A `pairing` fact with each commander's pairing ability and `passed`; a `companion` fact with the restriction and `passed` or `status: "not-checked"`. | Additive. |
| `deck_duplicates_preview` | Send a local deck bound to Archidekt, a group priority such as `Commander`, `Ramp`, `Removal`, `Sideboard`, `Maybeboard`, and `matchBy: "oracle"`. | Each duplicated card with its entries and the kept one, a `changes` list ready for `deck_apply_changes`, the deck revision, and any `ambiguous` cards left unchanged. Apply it locally, then run `archidekt_push_preview`. | New read tool. Changes nothing itself. |
| `stats_castability` | Send a local deck or backup, source groups with colors, a turn rule, play or draw, and `requireLandDrops`. | One row per distinct spell: mana cost, turn checked, and the exact probability of paying it from drawn sources. | New tool. |
| `spellbook_deck_bracket_facts_get` | Send a local deck. | Spellbook's per-card flags (Game Changer, mass land denial, extra turn) and per-combo facts (two-card, mana needed), with no `bracketTag`. | New tool. Spellbook outage returns the provider failure as today. |
| `playgroup_games_stats_get` | Send a playgroup, a group-by choice (deck or player), `maxGames`, and an optional date range. | One row per group with games, wins, win rate, the pod-size baseline, and average total rounds, plus games read, skipped, and outside the range. | New tool. Existing Playgroup tools unchanged. |
| More card fields | `card_list_query` with `where` on `oracleText`, `legalIn`, or `games`. | Matching cards; rows can show every Scryfall price column. | Additive. |
| Prompts | List prompts in a client. | `audit-deck`, `build-from-commander`, and `compare-decks`, each a short workflow that names tools and leaves every judgment to the user and agent. | New. Tools are unchanged. |
| Destructive remote writes | Call an Archidekt delete or push apply in Claude Code. | The tool carries `_meta` `anthropic/requiresUserInteraction: true`, so Claude Code asks the user every time. | Additive. Other clients ignore it. |

Example of a failed result after the change:

```json
{ "isError": true,
  "structuredContent": { "kind": "conflict", "reasonCode": "stale-revision",
    "message": "Deck changed; current revision is 9.", "current": { "revision": 9 } } }
```

Example error after the change, for a value the schema rejects:

```json
{ "kind": "invalid-input", "reasonCode": "invalid-arguments",
  "path": "freshnessPolicy",
  "message": "freshnessPolicy must be one of: default, cache-only, refresh." }
```

## Requirements

| ID | Priority | Requirement | Acceptance | Check |
| --- | --- | --- | --- | --- |
| SURF-001 | Must | With no `--toolsets` option, every toolset whose credentials are present is on; `decks`, `scryfall`, `stats`, and `spellbook` need none. `all` means the same. | Counts as in SURF-006: 30/43/43 with no credentials after phase 3, 35/48/48 after phase 4. | Selection and surface tests. |
| SURF-002 | Must | `archidekt` and `playgroup` are off when their credentials are absent, even when named in `--toolsets`, using the presence rules in the SADD (Archidekt needs both username and password, or a credential file that exists). Presence is checked without calling the provider. An invalid Playgroup configuration fails startup only when `playgroup` is named; otherwise the toolset is off with `credentials-invalid`. The capabilities resource reports the reason, and stderr names the settings. | The selection tests listed in the SADD Test Design. | App tests. |
| SURF-003 | Must | Every merged tool follows the four merge rules in the SADD: same annotations and modes as the tools it replaced, one required choice field, fields conditional on that choice, and one result type or a result union keyed by the same choice. | A test checks each rule for every merged tool. | Architecture test. |
| SURF-004 | Must | Every removed tool has a named replacement or a documented reason. | The CHANGELOG table lists all old names. A test checks that every old name in the map is absent and every new name is present. | Architecture test plus doc review. |
| SURF-005 | Must | The nine single-step deck edit tools are removed. | `deck_apply_changes` handles each change kind, shown by one test per kind. | App tests. |
| SURF-006 | Must | With both credentials, counts are 53/67/80 after phase 3 and 59/73/86 after phases 4 and 6; with none, 30/43/43 after phase 3 and 35/48/48 after phases 4 and 6. | Surface test pins counts. | Architecture test. |
| SURF-007 | Must | The default mode is `remote`. `read-only` and `local` remain available through `--mode` and configuration. | Mode tests; the capabilities resource reports the mode. | App tests. |
| NAME-001 | Must | Every tool name starts with a source prefix from the SADD list, is at most 64 characters, and matches the final name in FIXTURES. New names follow `<source>_<object>_<action>`; kept names such as `scryfall_search` and `stats_hypergeometric` are listed as exceptions in FIXTURES. Frozen Playgroup names are exempt. | Architecture test over prefixes, lengths, and the FIXTURES name list. | Architecture test. |
| NAME-002 | Must | Each tool description starts by naming its data source, such as "Local deck store:" or "Archidekt:". Tools whose names match after removing the prefix name different sources. | Architecture test. | Architecture test. |
| SURF-008 | Must | The 16 existing Playgroup tool names, inputs, and toolset are unchanged. The only Playgroup addition is `playgroup_games_stats_get` (PG-001). | Surface test lists the same 16 names plus the new tool after phase 4. | Architecture test. |
| SURF-009 | Must | The server sends instructions of at most 2,000 characters that name its jobs (card search, local decks, deck analysis, Archidekt, Playgroup, combos), the first tool for each, that the agent makes every deckbuilding judgment, and that provider text is untrusted data (SAFE-001). Instructions name only tools that exist in the current selection. | Tests for default, no-credential, and read-only selections check length and that every named tool is listed. | App tests. |
| FLOW-001 | Must | `scryfall_search` describes tag, type, color identity, mana value, format, and price filtering in `query`, with examples. `scryfall_tag_search` and `scryfall_cards_by_tag` describe their roles relative to it. | Description review; a fixture search with `otag:` passes through unchanged. | Review plus Scryfall test. |
| FLOW-002 | Must | `deck_get` accepts `include` with `cards` and `tags` and returns per-entry card fields and oracle tag slugs. | Tests for installed, not-installed, and unresolved card data. | App tests. |
| FLOW-003 | Must | The `common-v1` preset is removed; inline rules remain. | Preset request returns `invalid-input`; inline rules equivalent to the old preset give the same categories as before. | App tests. |
| ERR-001 | Must | Every stale-state `conflict` from a deck or Archidekt write or preview (a changed revision, preview, or remote deck) includes a `current` object with the current local revision, remote fingerprint, or both, when one is available at the point of the check. Archidekt adapter conflicts for `remote-deck-changed`, `push-preview-changed`, `restore-preview-changed`, and `folder-tree-changed` carry the current remote (or folder tree) fingerprint. Conflicts with no local revision or provider fingerprint to report omit it. Exempt: missing or existing binding, missing baseline, deleted remote deck, and other state conflicts that name no revision or fingerprint; `category-preview-mismatch`, `category-corpus-conflict`, and `import-preview-changed` (they compare a preview fingerprint or Scryfall generation, and the check holds no deck revision); `stale-scryfall-generation`, `stale-scryfall-snapshot`, and `stale-database-fingerprint` (a card-data generation, snapshot checksum, or backup database fingerprint, none of which is a local revision or remote fingerprint); and the Archidekt `snapshot-changed` and `snapshot-content-changed` (a snapshot checksum or content fingerprint). | Store, statistics, identity, categorization, Spellbook, on-curve, and Archidekt pull and push conflict tests, each asserting `current`. | Core, Decks, and App tests. |
| ERR-003 | Must | A request whose arguments fail to bind (bad JSON shape, unknown union `kind`, wrong type) returns `invalid-input` with a reason code and the JSON path of the bad value, never the SDK's generic "An error occurred invoking" text. | Cases X01 to X03. | App tests plus E2E smoke. |
| SCHEMA-007 | Must | Every polymorphic tool input (for example `DeckChangeInput`, statistics events, list sources, simulation rules) accepts its `kind` discriminator in any property position. | Case X01 for `deck_apply_changes` through the real server. An architecture test fails if any tool registration omits `ToolCallPolicy.SerializerOptions`, and a test checks that those options allow out-of-order metadata properties. Together they cover every union without a send-`kind`-last case per type. | App, architecture, and E2E tests. |
| ARCH-001 | Must | Archidekt push preview and apply treat an entry's non-primary categories as a set: order and letter case never create an entry update. The primary category is compared separately. | Cases A01 to A03, including pull, then no-op edit, then push preview with zero entry operations. | Archidekt tests. |
| ARCH-002 | Must | After a verified `archidekt_push_apply`, every local entry and category the push created on Archidekt is linked to the provider relation or category ID Archidekt assigned, in the same transaction as the baseline update. A later push preview never plans a remove-and-re-add of those rows, and plans only the real changes. A created entry that cannot be linked exactly is listed in the apply result's `unlinkedLocalIds` field (omitted when empty) and never guessed; a category with no verified match stores no link. An unlinked row does not block the next push: it is already on Archidekt, and the next push plans one remove and one add for it, so it is not duplicated. | Cases A04 to A07: pull, add an entry and a category, push, add unrelated entries, push preview shows only those entry-adds and no category change; an unlinked row is removed and re-added by the next push. | Archidekt and App tests. |
| ERR-002 | Must | Every failed tool result sets `isError: true` and keeps its structured failure body (kind, reason code, message, data) and text block. Successful results never set it. Protocol errors stay for unknown tools and malformed JSON-RPC only. | Unit tests of the one shared filter cover failure, success, binding exception, tool exception, and cancellation. An E2E test calls every listed tool that has required arguments with `{}` and checks the flag and body. Tools whose arguments are all optional are not called with `{}` because that would run them; they use the same filter. | App unit tests plus E2E. |
| SCHEMA-001 | Must | Every string parameter with a fixed set of values publishes `enum` in its JSON Schema. | A test builds every tool through the real toolset registration and walks each input schema, including `anyOf` and `oneOf` string members. It fails on a string field whose description lists values (matched by the pattern `[start or ": " or "one of "] a, b, or c`) but publishes no `enum`. The pattern is a heuristic; descriptions that name values some other way are not found, so annotated parameters are also listed in `KnownChoiceParameters_PublishEnum`. A known-fault fixture proves the walk reports a missing `enum`. Separate tests check each published value is accepted at run time. | App tests. |
| SCHEMA-002 | Must | Enum values on the wire stay the same as today. | Existing request fixtures pass unchanged. | App and E2E tests. |
| SCHEMA-003 | Must | Each `stats_*` description names its main request fields and includes one valid example request. | The example in each description deserializes and runs successfully in a test. | App tests. |
| SCHEMA-004 | Must | Tool input schemas reject unknown properties. | A test sends one unknown property to every tool and expects `invalid-input`. | Architecture test. |
| SCHEMA-005 | Should | `tools/list` returns tools in the same order on every start. | Two process starts return identical name lists. | E2E smoke test. |
| CARD-001 | Must | `scryfall_search`, `scryfall_cards_get`, and `scryfall_cards_by_tag` accept `responseFormat` with `concise`, `standard`, and `full`. Default is `standard`, which keeps every field of today's output and may only add fields. | Tests for all three values on each tool; a fixture check that every field of today's output is present and unchanged. | App tests. |
| CARD-002 | Must | `concise` includes the fields listed in the SADD, always including Scryfall ID, Oracle ID, and name. | Fixture output check. | Scryfall tests. |
| CARD-003 | Must | `concise` output for a 25-card fixture page is at most 40% of `standard` output size in UTF-8 bytes. | Measured in a test. | Scryfall tests. |
| CARD-004 | Must | `full` returns the same raw provider data as today's `includeRaw: true`, with the same 25-item page cap. | Fixture comparison. | Scryfall tests. |
| CARD-005 | Must | An optional `fields` list, valid only with `concise`, chooses fields from the published concise field list in the SADD. That list is wider than the default concise set, so agents can ask for added fields by name. `scryfallId`, `oracleId`, and `name` are always included. Unknown names return `invalid-input` listing allowed names. | Tests. | Scryfall tests. |
| SET-001 | Must | `card_lists_compare` supports `union`, `intersection`, `difference`, and `symmetric-difference`, with `matchBy` `oracle`, `printing`, and `name`. | One pure-function test per operation and match mode. | Card set tests. |
| SET-002 | Must | Sources `scryfall-snapshot`, `deck`, `deck-backup`, `archidekt-deck`, `archidekt-snapshot`, `cards`, and nested `compare` (at most 3 levels) are accepted by both list tools through one shared resolver. Reading a backup never changes the live database. | One test per source kind and a depth-limit test. | App tests. |
| SET-003 | Must | Results report counts for both sides and the result, list unmatched entries with a reason, and never guess a match. | Fixture with a deck entry that has no Scryfall identity. | App tests. |
| SET-004 | Must | Pages are ordered by name then Oracle ID, and the same request returns the same pages. | Two runs over fixtures give identical pages. | App tests. |
| SET-005 | Must | No result is stored. A changed Archidekt deck between pages returns `conflict` with the new state marker; a stale local deck returns `conflict` with the current revision. | Fake Archidekt server test and stale deck test. | App tests. |
| SET-006 | Must | A list source is accepted only when its owning toolset is on (`decks` for `deck` and `deck-backup`, `archidekt` for Archidekt sources, `scryfall` for the rest). Otherwise `invalid-input` with reason `toolset-not-selected` or `credentials-missing`. | Cases C05 and C11. | App tests. |
| SET-007 | Must | The cursor holds only the request fingerprint, page position, and source markers; a cursor that does not match the resent request is rejected. A request may hold at most 300 `cards` IDs in total. | Cases C12 and C13. | App tests. |
| SET-008 | Must | Backups are opened read-only without migrations. A different schema version returns `unsupported` with `backup-schema-mismatch`; a deck missing from the backup returns `not-found` with `deck-not-in-backup`. | Cases C09, C14, and C15. | App and deck store tests. |
| QUERY-001 | Must | `card_list_query` supports the `where` operators, up to two `groupBy` fields, the metrics, and optional sorted, paged rows described in the SADD, over every list source. | Pure-function test per operator and metric; App test per source kind. | Card list tests. |
| QUERY-002 | Must | Multi-valued fields count a card in each of its groups and set `multiValued: true`; cards with no value go in a `none` group. | Fixture with multicolor and multi-tag cards. | Card list tests. |
| QUERY-003 | Must | `pips` counts colored symbols by color, with hybrid and Phyrexian rules as stated in the SADD; generic and X are not counted; copies multiply. | Fixtures for each symbol kind. | Card list tests. |
| QUERY-004 | Must | `sources` counts copies whose `produced_mana` includes each color, plus `anyColor` and `noProducedMana`; enters-tapped is listed as not modeled. | Fixtures with a basic, a dual, a five-color land, a fetch land, and a mana rock. | Card list tests. |
| QUERY-005 | Must | Sums and averages are exact decimal strings; missing values are left out and counted in `missingValues`. | Fixture with a card that has no USD price. | Card list tests. |
| QUERY-006 | Must | Output is stable: same request, same groups, rows, and fingerprint. | Repeat-run test. | Card list tests. |
| FACT-001 | Must | Card results and `deck_get` card views can include `tags`, `cheapestPriceUsd` (lowest USD price among installed English printings), `gameChanger`, and `edhrecRank`. `tags` and `cheapestPriceUsd` are `null` with a stated reason when card data is not installed; `gameChanger` and `edhrecRank` come from the card object itself and are `null` only when Scryfall omits them. | Fixtures with installed and missing data. | Scryfall tests. |
| RULE-001 | Must | `deck_validate` with `rules: "commander"` reports deck size, commanders and eligibility, color identity and off-identity entries, singleton breaks, and `legalities.commander` failures, each with `passed` and the entries involved, plus a Game Changer count and list without `passed`. Eligibility and singleton follow the rules in the SADD, including Backgrounds in a valid pairing and "up to N" card limits. Missing `game_changer` data gives `status: "unknown"` with the affected entries. | One passing and one failing fixture per fact; cases R01 to R08, R19, and R20. | Deck rule tests. |
| RULE-002 | Must | Rule facts never name a bracket, power level, or overall verdict beyond each fact's `passed` flag. | Schema and description review. | Review. |
| SIM-001 | Must | `stats_simulate` takes groups, opening-hand rules, per-turn rules, and conditions only as structured data. `pick`, `maxPerTurn`, `manaFrom`, `draw-cards` `effect` and `cards`, and mulligan `bottom` are required. | Schema test; a request missing any of them returns `invalid-input`. | Simulation tests. |
| SIM-002 | Must | The same request and seed give byte-identical results on every platform. | Fixed-seed fixtures compared on Windows and Linux CI. | Simulation tests in CI. |
| SIM-003 | Must | Results include seed, random generator version, sample count, applied rules, hits, rate, a 95% Wilson interval per condition, mulligan keep counts, the `libraryEmpty` count, a request fingerprint, and a list of model limits. | Fixture checks. | Simulation tests. |
| SIM-004 | Must | The `on-curve` preset returns the same success counts as `deck_on_curve_estimate` for the same deck, seed, and inputs. | Replay fixtures from today's tool. | Simulation tests. |
| SIM-005 | Must | The tool rejects requests over a work limit (samples × turns × deck size) with a message that states the limit. | Over-limit request test. | Simulation tests. |
| SIM-006 | Must | The engine applies only the fixed engine rules in the SADD and the rules in the request. | One test per engine rule, including overlapping groups rejected, rule order, `maxPerTurn`, every `pick` value (including `target-first`), both `manaFrom` values, bottoming order, keep at limit, `draw-cards` timing (rule 7), and joint payment of spent mana and `can-cast` costs (rule 9). | Simulation tests. |
| SIM-007 | Must | `draw-cards` supports `effect` `once` and `each-turn`; drawn cards are usable by later rules in the same turn; extra draws from `each-turn` cards happen at each later draw step. | Cases S14 to S17. | Simulation tests. |
| SIM-008 | Must | The mulligan block requires `freeFirstMulligan`; when true, the first mulligan bottoms no card. | Case S18. | Simulation tests. |
| SIM-009 | Must | `sampleHands` (0 to 10, required) returns full detail for the first samples, and those samples are the same ones counted in the results. | Case S19. | Simulation tests. |
| SIM-010 | Must | Groups may declare `produces` colors (W, U, B, R, G, C, or `any`) with a group `default` and per-entry `byEntry` overrides; a card without them produces generic mana only. A `can-cast` condition takes a mana cost, either `turn` or `byTurn`, and an optional `inHand` entry that must be in hand. It passes when the untapped sources can pay the cost together with all mana spent that turn by rules, by one exact source-to-symbol assignment (the `OnCurveManaPayment` matcher). A `play` rule may use `pick: "target-first"` with a `targetCost`, which ranks cards exactly like today's `target-first-v1` land choice. Hybrid symbols accept either color; Phyrexian symbols need their color (life payment is not modeled and is listed in `limits`). | Cases S20 to S26, and SIM-004 replay fixtures with colored targets. | Simulation tests. |
| EVAL-001 | Must | A fixed task set of at least 15 tasks exists, with checkable answers, covering search, tag filtering, deck view, card list comparison, stats, local edits, and the Archidekt round trip. | [FIXTURES](FIXTURES.md#agent-task-set). | Doc review. |
| EVAL-002 | Must | Task results are recorded for the 0.10.0 baseline before phase 3 starts, and after phases 4 and 6: correct answers, tool calls, tool-result tokens, and invalid-parameter errors. | The Agent Task Results table in README. Phase 3 does not start without the baseline. | Manual run. |
| DOC-001 | Must | README, `docs/toolsets.md`, `docs/rewrite-guide.md` counts, and CHANGELOG match the shipped surface. | Doc review against the surface test. | Review. |
| EVAL-003 | Must | At least 5 tasks in the task set are marked held out. They are never used while rewriting descriptions, and their results are reported separately from the tuning tasks. | FIXTURES marks them; results tables have separate rows. | Doc review. |
| SCHEMA-006 | Must | The descriptions of `deck_apply_changes`, `deck_category_rules_preview`, `card_lists_compare`, `card_list_query`, `stats_castability`, and `stats_simulate` each include 2 to 4 example requests (a minimal one and a full one at least). Every example deserializes and runs in a test. | Example tests per tool, added in the phase that adds or renames the tool. | App tests. |
| SCHEMA-008 | Must | Every tool description, including its examples, is at most 4,096 characters, the length Claude Code keeps for tools it loads at start. | Architecture test over every description. | Architecture test. |
| SAFE-001 | Must | Every tool that returns text written by provider users (Archidekt deck names, descriptions, and category names; Playgroup names and notes; Spellbook descriptions) says in its description that this text is untrusted data, never instructions. The server never acts on that text, and never puts credentials in results, logs, or messages. | Architecture test over the listed tools' descriptions; review of result types for credential fields. | Architecture test plus review. |
| SAFE-002 | Should | Destructive remote-write tools (Archidekt delete and every push or snapshot apply that can remove remote data) carry `_meta` `anthropic/requiresUserInteraction: true`. | Surface test reads the `_meta` field. If SDK 2.2.0 cannot set tool `_meta`, the phase 1 spike records it and this requirement is deferred, not faked. | Architecture test. |
| SIZE-001 | Must | Phase 1 measures the largest default result of the candidate tools and records it; others are bounded small by their fixed result shape. Results of new tools and of changed outputs (`deck_get` with `include`, `concise` pages, `card_lists_compare`, `card_list_query`, `deck_duplicates_preview`, `stats_castability`, `stats_simulate` with `sampleHands`) never exceed 40,000 UTF-8 characters: they page, or set `truncated: true` with a `narrow` hint naming the fields or filters to use. Unchanged default outputs and frozen Playgroup tools are measured and reported, not truncated; descriptions of any that exceed the limit recommend the smaller option. Sizes are in UTF-8 bytes, which equal characters for ASCII JSON. | Phase 1 measurement table in README (one live run on 2026-10-27; tools needing credentials or installed card data are measured when phase 2 changes them); size tests over a 100-card deck and largest fixtures for each listed tool. | App tests. |
| PROMPT-001 | Should | The server offers MCP prompts `audit-deck`, `build-from-commander`, and `compare-decks`. Each takes at most two arguments whose values never need spaces, because Claude Code splits prompt arguments on spaces: `audit-deck` takes a deck ID, `compare-decks` takes two deck IDs, and `build-from-commander` takes a Scryfall card ID. Each names tools by final name and states that the user and agent make every judgment. | Prompt list test; a test checks that every tool a prompt names exists. | App tests. |
| RULE-003 | Must | With two cards in the `commander` zone, `deck_validate` with `rules: "commander"` reports a `pairing` fact: each card's pairing ability (Partner, Partner with a named card, Friends forever, Choose a Background, Doctor's companion, or a Partner variant), read from Scryfall `keywords` and oracle text as confirmed against captured card data in phase 4, and `passed` when both cards allow the same pairing. A single commander passes. | Cases R10 to R15. | Deck rule tests. |
| RULE-004 | Should | An entry in a `companion` zone gets a `companion` fact: the restriction text; whether the companion is inside the commanders' color identity and legal in Commander; and `passed` for the restrictions in the SADD table, or `status: "not-checked"` with the reason for the others. | Cases R16 to R18 and R21. | Deck rule tests. |
| FACT-002 | Must | Card data maps Scryfall `games`, `reserved`, and `layout`, which are not mapped today. It also maps `rarity`, `power`, `toughness`, and `loyalty`, which the concise set needs. They are added to `standard`, offered through `fields`, and readable by `card_list_query`. Oracle text for every face, all `legalities`, and every price column are already mapped and need only exposure. | Mapping fixture with each new field present and absent. | Scryfall tests. |
| QUERY-007 | Must | `card_list_query` accepts these fields: `oracleText` (case-insensitive `contains` only), `legalIn` and `bannedIn` (list fields of format keys), `games`, `reserved`, `layout`, and `priceUsdFoil`, `priceUsdEtched`, `priceEur`, `priceEurFoil`, `priceTix`. | Cases Q11 to Q14. | Card list tests. |
| DEDUP-001 | Must | `deck_duplicates_preview` reads one local deck (optional `expectedRevision`) and groups its entries by card using `matchBy` (`oracle`, `printing`, or `name`). A card with more than one copy in total, across every zone and category, is a duplicate. The required `exempt` value is `none` or `commander-singleton-exceptions` (basic lands, cards whose text allows any number, and cards whose text allows up to N, which keep up to N). `matchBy: oracle` needs installed card data; without it the result is `not-cached` naming the sync tool, and `name` still works. | Cases D01, D02, D06, and D08. | App tests. |
| DEDUP-002 | Must | The required `keepPriority` is an ordered list of groups, each `{ "zone": ... }` or `{ "category": ... }`, matched by name without regard to letter case. Each entry's rank is the best rank of its zone, primary category, or any of its categories; entries in no listed group rank last. One copy is kept on the best-ranked entry. When two or more entries share the best rank, the card goes in `ambiguous` with those entries and no change is planned for it. The server never picks a copy by any other rule. | Cases D03 and D04. | App tests. |
| DEDUP-003 | Must | The result lists each duplicate with its entries, quantities, zones, categories, and the kept entry, plus `changes`: `remove-entry` for every other entry and `update-entry` setting quantity 1 on the kept entry when it has more. The changes, sent to `deck_apply_changes` with the returned revision, leave exactly one copy of each resolved card. Entries without the identity `matchBy` needs go in `unmatched`. The tool writes nothing. | Cases D05 and D07, including apply then push preview against the fake Archidekt server. | App and Archidekt tests. |
| CAST-001 | Must | `stats_castability` takes a `deck` or `deck-backup` source, source groups selected by entry or category ID with `produces` colors, a turn rule (`mana-value` or a fixed turn), play or draw, and a required `requireLandDrops` flag. For each distinct mana cost of a nonland card it returns the turn checked and the exact probability that the cards seen by that turn hold sources that can pay the cost, using at most T sources by turn T. With `requireLandDrops: true` it returns P(payable and at least T sources seen) / P(at least T sources seen) from a new exact function; with `false` the value equals `stats_mana_availability` for the same inputs. | Cases K01 to K07: the unconditioned value matches `stats_mana_availability`; the conditioned value matches brute-force enumeration on a small deck. | Statistics tests. |
| CAST-002 | Must | `stats_castability` never states a target probability or a pass. It sorts rows by probability, then name, and reports in `skipped`, with the reason, cards with no mana cost, with `X`, or with hybrid or Phyrexian symbols, which the exact engine cannot express. Card data must be installed; otherwise the result is `not-cached` naming the sync tool. | Cases K04, K05, and K07. | Statistics tests. |
| BRACKET-001 | Must | `spellbook_deck_bracket_facts_get` sends a local deck to Spellbook `POST /estimate-bracket` through the existing adapter (pacing, cache, failure mapping) and returns per-card `gameChanger`, `massLandDenial`, `extraTurn`, and `banned` flags and per-combo `definitelyTwoCard`, `arguablyTwoCard`, `speed`, `manaNeeded`, and `manaValueNeeded`. `bracketTag` and any other label are removed before the result is built. | Fake-HTTP fixtures B01 to B04; a test that `bracketTag` never appears. | Spellbook and App tests. |
| PG-001 | Must | `playgroup_games_stats_get` reads a playgroup's games through the existing paced games list operation, up to a required `maxGames`, maps each game to a typed record, and groups by `deck` or `player`. Optional `from` and `to` dates filter on `ended_at` after reading, because the list has no date filter or stated order. Each row has games, wins, win rate, the pod-size baseline (mean of 1 / players), and `averageTotalRounds` from the games' `total_rounds`. | Fake-HTTP cases P01 to P05. | Playgroup and App tests. |
| PG-002 | Must | The result states games read, games skipped with reasons (no winner, missing deck), games outside the date range, and whether `maxGames` stopped the read, in which case the date range may be incomplete and the result says so. It makes no ranking or judgment. Commanders are not a group key: game data has no commander field, and agents map decks to commanders with `playgroup_deck_get`. | Cases P03 and P04. | Playgroup tests. |

## Risks

| Risk | Effect | Response |
| --- | --- | --- |
| SDK 2.2.0 may not emit `enum` for `[AllowedValues]` parameters. | SCHEMA-001 cannot pass as designed. | Proven in phase 1 (2026-10-27): `[AllowedValues]`, a subclass of it, and `JsonStringEnumConverter` all publish `enum`. If a later SDK drops this, add the enum through the existing `TransformSchemaNode` hook used in `StatisticsToolsetManifest`. |
| SDK 2.2.0 may not let the server reject unknown properties. | SCHEMA-004 cannot pass as designed. | Phase 1 checks it. Fallback: set `additionalProperties: false` through the same schema hook and check in the tool wrapper. The fallback must be applied wherever a tool builds its own schema options (`ToolRegistration`, `DeckBatchWriteTools`, `StatisticsToolsetManifest`), not only on the default `WithTools` path. |
| Rejecting unknown properties (SCHEMA-004) breaks clients that send extra fields today. | Calls that used to work fail. | It ships in phase 3 with the other breaking changes and is listed in the 0.11.0 release notes. |
| Old backups have an older `decks.db` schema. | Comparisons with old backups fail. | Read-only open without migration; `backup-schema-mismatch` names both versions (SET-008). Support for old versions is later work. |
| Each comparison page re-reads Archidekt sources. | Slow paging and provider load for large results. | One paced read per Archidekt source per page; descriptions suggest `pageSize` 100; grouped queries without rows need one page. |
| Remote writes are visible by default. | An agent could delete or overwrite an Archidekt deck without the user choosing `remote` mode first. | Destructive tools keep `Destructive = true` so clients ask first; push and delete keep their preview, fingerprint, and acknowledgment guards; README shows `--mode read-only` and `--mode local`. |
| More tools visible at once. | The model may pick a similar tool from the wrong source. | NAME-001 and NAME-002; the task set records wrong-tool calls. |
| Credentials are present but wrong. | Provider tools appear but fail. | `*_auth_status` tools report it; errors say which setting to check. |
| Agents keep using the large `standard` card format and never ask for `concise`. | Less context saved than possible. | The `scryfall_search` description recommends `concise` for broad filtering; the task set records token use. |
| Removing `common-v1` breaks saved agent prompts that use it. | Preset requests fail. | The error names inline rules, and the CHANGELOG shows the equivalent inline rule set. |
| `concise` drops a field an agent needs. | Extra follow-up calls. | `fields` and `standard` remain; the task set measures call counts. |
| A simulation rule still hides a play choice. | Breaks the product rule. | The engine rules are fixed, listed, and tested one by one (SIM-006); every other choice is a required request field (SIM-001). |
| `game_changer` is not mapped from Scryfall data today. | Game Changer facts are missing. | Phase 2 adds the field to the card mapping with a fixture; if the installed data lacks it, the fact reports `status: "unknown"` with the affected entries rather than zero (RULE-001). |
| Rule facts are read as a full legality verdict. | Agents over-trust them. | Descriptions state what each fact checks and what it does not (partner rules, banned-as-companion, bracket). |
| Grouping by tag over large lists is slow. | Long calls on big saved searches. | A work limit on cards × group values, stated in the error, like the simulation limit. |
| Agent evals are not repeatable run to run. | Before/after numbers are noisy. | Run each task three times; report the median. Evals inform the owner but do not gate release. |
| Argument binding happens in the SDK before the tool method runs. | ERR-003 cannot replace the generic error from inside the tool. | The phase 1 spike finds the SDK hook (call filter or custom binding); if none exists, tools take the raw arguments as `JsonElement` and bind them in App. |
| Descriptions get tuned to the fixed tasks. | Gains do not carry over to real use. | Held-out tasks (EVAL-003) are reported separately. |
| Setting `isError` changes how some clients show failures. | A client may hide the structured body or stop a workflow. | The structured body and text block stay. The baseline runs on `0.10.0`, before ERR-002; the phase 4 task-set run is the first to show any client effect, and is compared with the baseline. |
| `/estimate-bracket` reverses the Spellbook packet's exclusion of that endpoint. | Bracket labels leak into results. | The adapter removes `bracketTag` before building the result; a test fails if it appears (BRACKET-001). Phase 4 step 1 records the reversal in the rewrite umbrella. |
| Playgroup statistics read many game pages. | Slow calls and provider load. | Existing pacing, a game limit stated in the request and result, and date filters (PG-001, PG-002). |
| Pairing abilities change with new sets. | A new Partner variant is reported wrong. | Pairing reads Scryfall `keywords`; an unknown Partner variant returns `status: "unknown"` with the keyword instead of a pass or fail. |
| Colored simulation is slower than generic. | Work limit hit sooner. | The work limit counts colored conditions; the error states the limit. |
| SDK 2.2.0 may not support tool `_meta` or prompts. | SAFE-002 or PROMPT-001 cannot pass. | Both are Should. The phase 1 spike records support; unsupported items are deferred with the reason. |

## Assumptions

- Users are on MCP clients that read `tools/list` at start; no live tool list
  changes are needed.
- Pre-1.0 users accept a breaking change with a clear mapping table.

## Sources

- MCP tools specification, 2025-11-25:
  <https://modelcontextprotocol.io/specification/2025-11-25/server/tools>
- MCP 2026-07-28 changelog:
  <https://modelcontextprotocol.io/specification/2026-07-28/changelog>
- Anthropic, "Writing effective tools for agents":
  <https://www.anthropic.com/engineering/writing-tools-for-agents>
- GitHub MCP server toolsets and read-only mode:
  <https://github.com/github/github-mcp-server>
- Scryfall search syntax, including `otag:` and `atag:`:
  <https://scryfall.com/docs/syntax>
- Claude Code MCP behavior (tool search, output limits, `_meta`, prompts):
  <https://code.claude.com/docs/en/mcp>
- Anthropic, "Advanced tool use" (tool search, input examples):
  <https://www.anthropic.com/engineering/advanced-tool-use>
- MCP security best practices:
  <https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices>
- Commander Brackets updates, October 2025 and February 2026:
  <https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-october-21-2025>,
  <https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026>
- Commander Spellbook API schema (`/estimate-bracket`):
  <https://backend.commanderspellbook.com/schema/>
- Frank Karsten, colored sources for consistent casting (2022):
  <https://www.tcgplayer.com/content/article/How-Many-Sources-Do-You-Need-to-Consistently-Cast-Your-Spells-A-2022-Update/dc23a7d2-0a16-4c0b-ad36-586fcca03ad8/>
  The server does not ship his thresholds; agents may use them as their own
  target.
