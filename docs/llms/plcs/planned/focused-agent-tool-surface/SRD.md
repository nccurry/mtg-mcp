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
- A user gets the full Archidekt round trip by configuring credentials only.
  Provider tools without credentials stay hidden.
- Clients can check parameter values before a call.
- An agent can page through many cards without filling its context.
- An agent can test its own deck theories with repeatable sampling, with
  every play choice stated in the request.
- The team can show the change helped, using a fixed task set.

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
- One new sampling tool, `stats_simulate`, and the retirement of
  `deck_on_curve_estimate`.
- An agent task set and a before/after record.

## Non-Scope

- Card recommendations, quality scores, or automatic deck changes.
- Full-game simulation, opponent models, combat, or colored-mana rules.
- Changes to provider HTTP contracts, how credentials are loaded or stored,
  or local storage formats. Gating only checks whether credentials are
  present.
- Changes to `MtgMcp.Core` beyond what an enum type move strictly needs. Enum
  types belong in App or the owning adapter.
- Tool name aliases for removed tools.
- EDHREC data (inclusion, synergy, average decks, salt). It has no official
  public API.
- Card collection or ownership tracking.
- Bracket or power-level judgments.
- A deck revision history table.
- Playgroup redesign. Its tools keep their names and inputs. Only shared
  checks that apply to every tool, such as enum schemas, touch them.

## Public Surface Changes

| Surface | Action | Expected result | Compatibility |
| --- | --- | --- | --- |
| Default selection | Start the server with no options and no credentials. | `decks`, `scryfall`, `stats`, and `spellbook` in `remote` mode: 45 tools. | Wider than today (`decks`, `scryfall`, `stats` in `local` mode). |
| Credential gating | Start with `~/.mtg-mcp/archidekt.json` and `playgroup.json` present. | All six toolsets: 82 tools. | Archidekt and Playgroup no longer need `--toolsets`. |
| Missing credentials | Start with `--toolsets archidekt` and no Archidekt credentials. | `archidekt` stays off; the capabilities resource says `credentials-missing`; stderr names the setting to add. | Today the tools would appear and fail when called. |
| Read-only | `--mode read-only` | Every write tool is hidden: 32 tools without credentials, 55 with both. | Unchanged behavior; no longer the only way to limit tools. |
| Renamed tools | Call `local_deck_get` instead of `deck_get`. | Same behavior. | Breaking. The CHANGELOG maps every old name. |
| Merged tools | Call a new name such as `scryfall_cards_get`. | Same data as the old tools it replaces. | Old names return "unknown tool". The CHANGELOG maps each old name. |
| Tag search | `scryfall_search` with `query: "otag:ramp t:sorcery mv<=2 f:commander"` | Scryfall's matching cards. | No behavior change; descriptions now say so. |
| Deck card view | `deck_get` with `include: ["cards","tags"]` | Each entry has concise card fields and oracle tag slugs, or `cardData: "not-installed"` / `"unresolved"`. | Additive. |
| Category preset | `deck_category_rules_preview` with `common-v1` | `invalid-input` naming inline rules as the replacement. | Breaking. |
| Unknown properties | Send a field a tool does not define. | `invalid-input` naming the field. | Breaking; ships in phase 3. |
| Enum parameters | Send `"freshnessPolicy": "refresh"`. | Same behavior as today. | Wire-compatible. Bad values are now rejected by the schema. |
| Card formats | Call `scryfall_search` with no format, or with `responseFormat: "concise"`. | Today's card rows by default; small rows when `concise` is asked for. | Default output unchanged. Breaking only for `includeRaw: true`, which becomes `responseFormat: "full"`. |
| `card_lists_compare` | Compare a search snapshot with an Archidekt deck using `difference`. | Counts, a page of result cards, unmatched entries, and source markers. | New tool. |
| `card_list_query` | Group a local deck by `manaValue` with `quantity`, `pips`, and `sum:priceUsd`. | One row per mana value with counts, pips by color, and price sums. | New tool. |
| `deck_validate` rules | `deck_validate` with `rules: "commander"`. | Structural checks plus size, commander, identity, singleton, and legality facts, each with `passed` and the entries involved, plus a Game Changer count and list. | Additive; without `rules` the result is unchanged. |
| Card facts | `responseFormat: "concise"` | Rows also carry `tags`, `cheapestPriceUsd`, `gameChanger`, and `edhrecRank`. | Additive. |
| Conflict results | Apply with a stale revision. | `conflict` with the current revision in `data`. | Additive. |
| `stats_simulate` | Send a deck, groups, rules, conditions, sample count, and seed. | Hit counts, rates, intervals, applied rules, seed, and fingerprint. | New tool. |
| `deck_on_curve_estimate` | Removed in phase 6. | Use the `on-curve` preset of `stats_simulate`. | Breaking. Old seeds replay to the same result through the preset. |

Example error after the change, for a value the schema rejects:

```json
{ "kind": "invalid-input", "reasonCode": "invalid-freshness-policy",
  "message": "freshnessPolicy must be one of: default, cache-only, refresh." }
```

## Requirements

| ID | Priority | Requirement | Acceptance | Check |
| --- | --- | --- | --- | --- |
| SURF-001 | Must | With no `--toolsets` option, every toolset whose credentials are present is on; `decks`, `scryfall`, `stats`, and `spellbook` need none. `all` means the same. | Counts as in SURF-006: 30/43/43 with no credentials after phase 3, 32/45/45 after phase 4. | Selection and surface tests. |
| SURF-002 | Must | `archidekt` and `playgroup` are off when their credentials are absent, even when named in `--toolsets`, using the presence rules in the SADD (Archidekt needs both username and password, or a credential file that exists). Presence is checked without calling the provider. An invalid Playgroup configuration fails startup only when `playgroup` is named; otherwise the toolset is off with `credentials-invalid`. The capabilities resource reports the reason, and stderr names the settings. | The selection tests listed in the SADD Test Design. | App tests. |
| SURF-003 | Must | Every merged tool follows the four merge rules in the SADD: same annotations and modes as the tools it replaced, one required choice field, fields conditional on that choice, and one result type or a result union keyed by the same choice. | A test checks each rule for every merged tool. | Architecture test. |
| SURF-004 | Must | Every removed tool has a named replacement or a documented reason. | The CHANGELOG table lists all old names. A test checks that every old name in the map is absent and every new name is present. | Architecture test plus doc review. |
| SURF-005 | Must | The nine single-step deck edit tools are removed. | `deck_apply_changes` handles each change kind, shown by one test per kind. | App tests. |
| SURF-006 | Must | With both credentials, counts are 53/67/80 after phase 3 and 55/69/82 after phases 4 and 6; with none, 30/43/43 after phase 3 and 32/45/45 after phases 4 and 6. | Surface test pins counts. | Architecture test. |
| SURF-007 | Must | The default mode is `remote`. `read-only` and `local` remain available through `--mode` and configuration. | Mode tests; the capabilities resource reports the mode. | App tests. |
| NAME-001 | Must | Every tool name follows `<source>_<object>_<action>` with a prefix from the SADD list, at most 64 characters, and the renames in FIXTURES are applied. Frozen Playgroup names are exempt. | Architecture test over all names. | Architecture test. |
| NAME-002 | Must | Each tool description starts by naming its data source, such as "Local deck store:" or "Archidekt:". Tools whose names match after removing the prefix name different sources. | Architecture test. | Architecture test. |
| SURF-008 | Must | Playgroup tool names, inputs, and toolset are unchanged. | Surface test lists the same 16 names. | Architecture test. |
| FLOW-001 | Must | `scryfall_search` describes tag, type, color identity, mana value, format, and price filtering in `query`, with examples. `scryfall_tag_search` and `scryfall_cards_by_tag` describe their roles relative to it. | Description review; a fixture search with `otag:` passes through unchanged. | Review plus Scryfall test. |
| FLOW-002 | Must | `deck_get` accepts `include` with `cards` and `tags` and returns per-entry card fields and oracle tag slugs. | Tests for installed, not-installed, and unresolved card data. | App tests. |
| FLOW-003 | Must | The `common-v1` preset is removed; inline rules remain. | Preset request returns `invalid-input`; inline rules equivalent to the old preset give the same categories as before. | App tests. |
| ERR-001 | Must | Every `conflict` result from a deck or Archidekt write or preview includes the current local revision or remote state marker. | One test per conflict source. | App tests. |
| SCHEMA-001 | Must | Every string parameter with a fixed set of values publishes `enum` in its JSON Schema. | A test walks every tool input schema and fails on a choice parameter with no `enum`. | Architecture test. |
| SCHEMA-002 | Must | Enum values on the wire stay the same as today. | Existing request fixtures pass unchanged. | App and E2E tests. |
| SCHEMA-003 | Must | Each `stats_*` description names its main request fields and includes one valid example request. | The example in each description deserializes and runs successfully in a test. | App tests. |
| SCHEMA-004 | Must | Tool input schemas reject unknown properties. | A test sends one unknown property to every tool and expects `invalid-input`. | Architecture test. |
| SCHEMA-005 | Should | `tools/list` returns tools in the same order on every start. | Two process starts return identical name lists. | E2E smoke test. |
| CARD-001 | Must | `scryfall_search`, `scryfall_cards_get`, and `scryfall_cards_by_tag` accept `responseFormat` with `concise`, `standard`, and `full`. Default is `standard`, which matches today's output. | Tests for all three values on each tool. | App tests. |
| CARD-002 | Must | `concise` includes the fields listed in the SADD, always including Scryfall ID, Oracle ID, and name. | Fixture output check. | Scryfall tests. |
| CARD-003 | Must | `concise` output for a 25-card fixture page is at most 40% of `standard` output size in UTF-8 bytes. | Measured in a test. | Scryfall tests. |
| CARD-004 | Must | `full` returns the same raw provider data as today's `includeRaw: true`, with the same 25-item page cap. | Fixture comparison. | Scryfall tests. |
| CARD-005 | Must | An optional `fields` list narrows `concise` output; unknown names return `invalid-input` listing allowed names. | Tests. | Scryfall tests. |
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
| FACT-001 | Must | Card results and `deck_get` card views can include `tags`, `cheapestPriceUsd` (lowest USD across installed printings), `gameChanger`, and `edhrecRank`; they are `null` with a stated reason when card data is not installed. | Fixtures with installed and missing data. | Scryfall tests. |
| RULE-001 | Must | `deck_validate` with `rules: "commander"` reports deck size, commanders and eligibility, color identity and off-identity entries, singleton breaks, and `legalities.commander` failures, each with `passed` and the entries involved, plus a Game Changer count and list without `passed`. Missing `game_changer` data gives `status: "unknown"` with the affected entries. | One passing and one failing fixture per fact; cases R01 to R08. | Deck rule tests. |
| RULE-002 | Must | Rule facts never name a bracket, power level, or overall verdict beyond each fact's `passed` flag. | Schema and description review. | Review. |
| SIM-001 | Must | `stats_simulate` takes groups, opening-hand rules, per-turn rules, and conditions only as structured data. `pick`, `maxPerTurn`, `manaFrom`, `draw-cards` `effect` and `cards`, and mulligan `bottom` are required. | Schema test; a request missing any of them returns `invalid-input`. | Simulation tests. |
| SIM-002 | Must | The same request and seed give byte-identical results on every platform. | Fixed-seed fixtures compared on Windows and Linux CI. | Simulation tests in CI. |
| SIM-003 | Must | Results include seed, random generator version, sample count, applied rules, hits, rate, a 95% Wilson interval per condition, mulligan keep counts, the `libraryEmpty` count, a request fingerprint, and a list of model limits. | Fixture checks. | Simulation tests. |
| SIM-004 | Must | The `on-curve` preset returns the same success counts as `deck_on_curve_estimate` for the same deck, seed, and inputs. | Replay fixtures from today's tool. | Simulation tests. |
| SIM-005 | Must | The tool rejects requests over a work limit (samples × turns × deck size) with a message that states the limit. | Over-limit request test. | Simulation tests. |
| SIM-006 | Must | The engine applies only the fixed engine rules in the SADD and the rules in the request. | One test per engine rule, including overlapping groups rejected, rule order, `maxPerTurn`, both `pick` values, both `manaFrom` values, bottoming order, keep at limit, and `draw-cards` timing (rule 7). | Simulation tests. |
| SIM-007 | Must | `draw-cards` supports `effect` `once` and `each-turn`; drawn cards are usable by later rules in the same turn; extra draws from `each-turn` cards happen at each later draw step. | Cases S14 to S17. | Simulation tests. |
| SIM-008 | Must | The mulligan block requires `freeFirstMulligan`; when true, the first mulligan bottoms no card. | Case S18. | Simulation tests. |
| SIM-009 | Must | `sampleHands` (0 to 10, required) returns full detail for the first samples, and those samples are the same ones counted in the results. | Case S19. | Simulation tests. |
| EVAL-001 | Must | A fixed task set of at least 15 tasks exists, with checkable answers, covering search, tag filtering, deck view, card list comparison, stats, local edits, and the Archidekt round trip. | [FIXTURES](FIXTURES.md#agent-task-set). | Doc review. |
| EVAL-002 | Must | Task results are recorded for the 0.10.0 baseline before phase 3 starts, and after phases 4 and 6: correct answers, tool calls, tool-result tokens, and invalid-parameter errors. | Results table in this packet. Phase 3 does not start without the baseline. | Manual run. |
| DOC-001 | Must | README, `docs/toolsets.md`, `docs/rewrite-guide.md` counts, and CHANGELOG match the shipped surface. | Doc review against the surface test. | Review. |

## Risks

| Risk | Effect | Response |
| --- | --- | --- |
| SDK 2.2.0 may not emit `enum` for `JsonStringEnumConverter` parameters. | SCHEMA-001 cannot pass as designed. | Phase 1 step 1 proves it on one parameter. If it fails, add the enum through the existing `TransformSchemaNode` hook used in `StatisticsToolsetManifest`. |
| SDK 2.2.0 may not let the server reject unknown properties. | SCHEMA-004 cannot pass as designed. | Phase 1 checks it. Fallback: set `additionalProperties: false` through the same schema hook and check in the tool wrapper. |
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
