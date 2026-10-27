# Focused Agent Tool Surface PLC Packet

> [!IMPORTANT]
> This packet is a plan. It does not authorize code changes until the owner
> approves it and sets `Implementation authorized: Yes`.

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/focused-agent-tool-surface/`
- Owner: mtg-mcp
- Created: 2026-10-25
- Last updated: 2026-10-27
- Current phase: Amended with research gaps (2026-10-27); awaiting review and implementation authorization
- Implementation authorized: No

## Summary

The server's main job is to help an agent find and filter Magic cards, show
facts and repeatable math about decks, and build and sync decks with
Archidekt. The agent makes every deckbuilding choice. The server returns
facts and calculation results only.

The current surface follows that rule, but it is larger and harder to use than
it needs to be:

- Useful toolsets are off until configured: Archidekt and Playgroup need
  `--toolsets` even when credentials exist, Spellbook is opt-in, and Archidekt
  uploads need `--mode remote`.
- Some names are unclear: `deck_get` could mean a local, Archidekt, or
  Playgroup deck, and "corpus" is internal jargon.
- `all` shows 97 tools, some of which duplicate each other.
- Nine single-step deck edit tools repeat what `deck_apply_changes` already
  does.
- Tag filtering already works through `scryfall_search` with `otag:`, but
  nothing tells the agent, and there is no one-call view of a deck's cards
  with their fields and tags.
- About 20 parameters list their allowed values only in prose, so clients
  cannot check them.
- Card search pages are large, which makes broad filtering expensive.
- The `common-v1` category preset is a server-chosen definition of "ramp",
  "card draw", "removal", and "recursion".
- Deck analysis does not use card data. `stats_deck_summary` counts zones,
  categories, and printings, but the agent must supply mana values, colors,
  prices, and tags itself, so a curve, pip count, or price total takes many
  calls.
- There are no Commander rule facts and no way to compare a deck with an
  earlier version.
- The only sampled simulation answers one fixed question. Planned goldfish
  work would put play decisions inside the server.
- Checking whether each spell is castable on curve takes one
  `stats_mana_availability` call per spell.
- Commander pairing (Partner, Background, and others), companion rules, and
  bracket-related facts such as mass land denial and extra turns are left to
  the agent, which often gets them wrong.
- Playgroup tools return raw records only, so a win rate means paging through
  every game by hand.
- Failed calls look like successes to the client (`isError` is never set),
  the server sends no instructions, and provider text written by other users
  is not marked as untrusted.

This packet makes these changes:

1. Turn every toolset on by default, keep Archidekt and Playgroup off until
   their credentials are configured, make `remote` the default mode with
   `read-only` as an option, rename tools so each name says its data source,
   and merge read tools that differ only in how an item is looked up.
2. Make Scryfall query syntax the documented way to filter by tag, and add
   `include: ["cards","tags"]` to `deck_get`.
3. Remove the `common-v1` preset; agents send their own category rules.
4. Publish allowed values as JSON Schema enums, reject unknown fields, and
   document the statistics request fields.
5. Add `responseFormat` and `fields` so an agent can ask for small card
   results. Today's format stays the default.
6. Put the current revision in every conflict result.
7. Add `card_lists_compare`, which takes references to search results and
   decks (local or Archidekt) and returns their union, intersection,
   difference, or symmetric difference, without the agent copying card lists.
   Local backups and Archidekt snapshots are also sources, for "what changed".
8. Add `card_list_query`, which filters, groups, counts, sums, and sorts any
   card list using installed card data, including mana pips and color
   sources. One call gives a curve, type mix, role counts, price totals, or a
   sorted table for cut decisions.
9. Add card facts (`tags`, cheapest price across printings, Game Changer flag,
   EDHREC rank) and Commander rule facts in `deck_validate` (size, commander,
   color identity, singleton, legality, Game Changers).
10. Add `stats_simulate`, where the agent states every play choice and the
    server only shuffles, draws, counts, and reports. Its first rule set covers
    land drops, ramp, card draw, mulligans (including Commander's free first
    mulligan), colored mana costs, and conditions, and it can return sample
    hands.
11. Measure agent behavior with a fixed task set before and after, with
    held-out tasks.
12. Add `stats_castability`: the exact chance of paying each spell's colored
    cost on curve from the agent's own source groups, in one call.
13. Add Commander pairing and companion facts to `deck_validate`, and
    `spellbook_deck_bracket_facts_get` for Spellbook's per-card and per-combo
    bracket facts without its bracket label.
14. Add `playgroup_games_stats_get` for win rates and game counts by deck
    or player.
15. Add card fields for oracle text, every format legality, game
    availability, Reserved List, layout, and every Scryfall price column.
16. Set `isError` on failures, send server instructions, add examples to
    complex tool descriptions, cap result size, mark untrusted provider text,
    add a confirmation hint on destructive Archidekt writes, and ship three
    MCP prompts.
17. Add `deck_duplicates_preview`: keep one copy of each card across a
    deck's zones and categories, by a group priority the agent orders. It
    returns `deck_apply_changes` edits; the existing push sends them to
    Archidekt.

Playgroup's 16 existing tools are frozen: they keep their names and inputs.
Item 14 adds one new tool.

## Before and After

| Start with no options | Today | After this packet |
| --- | --- | --- |
| Toolsets | `decks`, `scryfall`, `stats` | All six, minus providers without credentials |
| Mode | `local` | `remote` |
| Tools shown, no credentials | 55 | 48 |
| Tools shown, Archidekt and Playgroup credentials | 55 (providers need `--toolsets`) | 86 |
| With `--mode read-only`, both credentials | 33 | 59 |
| Everything (`all`, `remote`) | 97 | 86 |

The exact per-tool mapping and final names are in
[FIXTURES.md](FIXTURES.md#old-to-new-tool-map). The surface architecture test
pins the new numbers once each phase lands.

## Main Workflow After the Change

| Step | Tool |
| --- | --- |
| Find cards by kind or tag | `scryfall_search` with `otag:ramp t:sorcery mv<=2 f:commander` |
| Read details for a few cards | `scryfall_cards_get`, `scryfall_card_details` |
| See a deck with card fields and tags | `deck_get` with `include: ["cards","tags"]` |
| Search results not already in a deck, or any union/intersection/difference | `card_lists_compare` |
| Curve, types, role counts, pips, sources, prices, sorted card tables | `card_list_query` |
| Commander rule facts, including pairing and companion | `deck_validate` with `rules: "commander"` |
| Chance of casting each spell on curve | `stats_castability` |
| Game Changers, mass land denial, extra turns, two-card combos | `spellbook_deck_bracket_facts_get` |
| Win rates in a playgroup | `playgroup_games_stats_get` |
| One copy of each card across an Archidekt deck's groups | `deck_duplicates_preview`, `deck_apply_changes`, then the Archidekt push pair |
| What changed since a backup or Archidekt snapshot | `card_lists_compare` with `deck-backup` or `archidekt-snapshot` |
| Exact math or sampling | `stats_*`, `stats_simulate` |
| Build or edit a local deck | `deck_create`, `deck_apply_changes` |
| Download from or upload to Archidekt | `archidekt_pull_preview`/`_apply`, `archidekt_deck_create`, `archidekt_push_preview`/`_apply` |

This table uses working names. Phase 3 renames `deck_*` to `local_deck_*`
and a few Scryfall tools; see [final names](FIXTURES.md#final-tool-names).

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance rules.
- [SADD.md](SADD.md): design, merge rules, schemas, and the simulation model.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): phases and checks.
- [FIXTURES.md](FIXTURES.md): old-to-new tool map, agent task set, and
  simulation, card set, card list query, deck rule, castability, Spellbook
  bracket facts, and Playgroup statistics cases.

## Decision Snapshot

| Decision | Status | Reason | Detail |
| --- | --- | --- | --- |
| Merge only tools with the same annotations and mode. | Proposed | Annotations and mode filtering work per tool. Merging a read with a write would hide reads in `read-only` mode or mark reads as destructive. | [SADD](SADD.md#merge-rule) |
| Turn every toolset on by default. | Owner decision, 2026-10-25 | Every tool is a deckbuilding tool; context cost is acceptable. | [SADD](SADD.md#toolsets-and-modes) |
| Keep Archidekt and Playgroup off until credentials are configured. | Owner decision, 2026-10-25 | Their tools only fail without credentials. Presence is checked without a network call. | [SADD](SADD.md#toolsets-and-modes) |
| Make `remote` the default mode; keep `read-only` and `local` as options. | Owner decision, 2026-10-25 | Uploads are part of the main workflow; destructive tools stay marked and guarded. | [SADD](SADD.md#toolsets-and-modes) |
| No new toolsets; keep the six existing ones. | Proposed | Read-only mode already hides writes, so `deck-edit` and `scryfall-admin` splits are not needed. | [SADD](SADD.md#toolsets-and-modes) |
| Name every tool `<source>_<object>_<action>`; rename `deck_*` to `local_deck_*` and remove "corpus". | Owner decision, 2026-10-25 (`local_deck_*` confirmed) | With every toolset on, similar tools must say which source they use. | [SADD](SADD.md#tool-names) |
| Use `scryfall_search` with `otag:` as the main tag filter. | Proposed | Scryfall already supports it; one call replaces several. | [SADD](SADD.md#card-search-and-tags) |
| Add `include` to `deck_get` for card fields and tags. | Proposed | One call gives the agent everything it needs to group and count a deck. | [SADD](SADD.md#deck-card-view) |
| Remove the nine single-step deck edit tools. | Proposed | `deck_apply_changes` already accepts every one of these operations. | [SADD](SADD.md#deck-tools) |
| Remove the `common-v1` preset. | Proposed | It encodes the server's view of card roles. | [SADD](SADD.md#removal-review) |
| Keep local backups and exact identity reconciliation. | Proposed | Backups copy the whole database; reconciliation is exact-match only. | [SADD](SADD.md#removal-review) |
| Freeze Playgroup's existing tools; add one aggregate read tool. | Owner decision, 2026-10-25; amended 2026-10-27 | The 16 existing tools stay unchanged. Querying a playgroup is a stated goal, and win rates need an aggregate. | [SADD](SADD.md#playgroup-game-statistics) |
| Publish enums through `JsonStringEnumConverter`. | Proposed, needs a spike | Values stay the same on the wire. The schema output from MCP C# SDK 2.2.0 must be confirmed first. | [SADD](SADD.md#enum-schemas) |
| Keep `standard` as the default card format; add opt-in `concise` with an optional `fields` list. | Owner decision, 2026-10-25 | Today's output stays the default. Agents that filter many cards ask for `concise`. | [SADD](SADD.md#card-response-formats) |
| State every simulation play choice in the request; fix and list the few engine rules. | Proposed | Keeps decisions with the agent and makes results explainable. | [SADD](SADD.md#engine-rules) |
| Replace `deck_on_curve_estimate` with a `stats_simulate` preset. | Proposed | One sampling engine and one replay contract. | [SADD](SADD.md#on-curve-migration) |
| Add `card_lists_compare` that takes list references, not lists. | Owner decision, 2026-10-25 | Large lists stay on the server; the set logic is exact and makes no judgment. | [SADD](SADD.md#card-list-comparison) |
| Recompute comparison pages from source markers instead of storing results. | Proposed | Chaining works through nested requests with no new storage. | [SADD](SADD.md#rules) |
| Add a `draw-cards` simulation rule. | Owner decision, 2026-10-25 | Card draw changes land-drop and mana results. | [SADD](SADD.md#rule-and-check-kinds-first-version) |
| Add `card_list_query` over the shared list sources. | Owner decision, 2026-10-25 | One grouped query covers curve, types, roles, pips, sources, and prices. | [SADD](SADD.md#card-list-query) |
| Count pips and sources from printed card data only; leave enters-tapped unmodeled. | Proposed | Scryfall has no enters-tapped field; guessing from text would be a judgment. | [SADD](SADD.md#mana-metrics) |
| Add card facts: tags, cheapest price, Game Changer, EDHREC rank. | Owner decision, 2026-10-25 | Needed for alternatives, budget, and bracket questions. | [SADD](SADD.md#card-response-formats) |
| Add Commander rule facts to `deck_validate`. | Owner decision, 2026-10-25 | Exact rule checks; reverses the 0.9.0 no-legality rule. No bracket or verdict. | [SADD](SADD.md#deck-rule-facts) |
| Use backups and Archidekt snapshots as history, not a revision table. | Owner decision, 2026-10-25 | No storage format change. | [SADD](SADD.md#list-sources) |
| Add free first mulligan and sample hands to simulation. | Owner decision, 2026-10-25 | Commander mulligan rule and inspectable examples. | [SADD](SADD.md#engine-rules) |
| Leave out EDHREC data and collections. | Owner decision, 2026-10-25 | EDHREC has no official public API; collections need their own plan. | [SRD](SRD.md#non-scope) |
| Keep colored payment in `stats_simulate`. | Owner decision, 2026-10-27 | Today's on-curve estimator pays colored costs, so SIM-004 replay requires it; on-curve colored casting is the most common consistency question. | [SADD](SADD.md#engine-rules) |
| Set `isError` on every failed result. | Owner decision, 2026-10-27 | The MCP tools spec reports execution failures this way, so models notice and fix calls. | [SADD](SADD.md#protocol-behavior) |
| Send server instructions built from the active selection. | Owner decision, 2026-10-27 | Clients that load tools on demand see only names and instructions at start. | [SADD](SADD.md#protocol-behavior) |
| Add examples, a size limit, untrusted-text notes, a confirmation hint, and prompts. | Owner decision, 2026-10-27; design proposed | MCP and Claude Code guidance; `_meta` and prompts depend on the phase 1 SDK spike. | [SADD](SADD.md#protocol-behavior) |
| Add `stats_castability` as a new tool. | Owner decision, 2026-10-27; design proposed | Whole-deck on-curve castability in one exact call; a list mode in `stats_mana_availability` would break the one-result-type rule. | [SADD](SADD.md#castability) |
| Add Commander pairing and companion facts. | Owner decision, 2026-10-27 | Exact rules facts that agents often get wrong. Unknown Partner variants and unlisted companions report `unknown` or `not-checked`. | [SADD](SADD.md#deck-rule-facts) |
| Use Spellbook `/estimate-bracket` for factual flags only. | Owner decision, 2026-10-27 | Reverses the Spellbook packet's exclusion; `bracketTag` is removed before any result. | [SADD](SADD.md#spellbook-bracket-facts) |
| Add oracle text, legalities, games, Reserved List, layout, and all price columns as card fields. | Owner decision, 2026-10-27 | Lets agents count tapped lands, check other formats and platforms, and price in EUR or tix without the server judging. | [SADD](SADD.md#card-list-query) |
| Deduplicate by an agent-ordered group priority, on the local deck, then push. | Owner decision, 2026-10-27 | Keeps the copy choice with the agent and reuses revision checks, backups, and the Archidekt push. Ties are reported, not broken. | [SADD](SADD.md#deck-duplicates) |
| Ship the two bug fixes with the full plan. | Owner decision, 2026-10-27 | The whole packet will be implemented together; no separate patch release. | [Plan](IMPLEMENTATION_PLAN.md#release-plan) |
| Ship phases 1 to 5 as `0.11.0`; ship `stats_simulate` as `0.12.0`. | Proposed | The surface cleanup and list comparison are useful alone; simulation is the largest change. | [Plan](IMPLEMENTATION_PLAN.md#release-plan) |

## Project and Surface Impact

- `MtgMcp.App`: tool classes, toolset manifests and selection, parameter
  types, descriptions, `deck_get` includes, and conflict data.
- `MtgMcp.Scryfall`: a projection for concise and standard card results.
- `MtgMcp.OnCurve`: becomes the sampling engine behind `stats_simulate`. The
  project may be renamed `MtgMcp.Simulation` in phase 6.
- `MtgMcp.App`: `card_lists_compare` and `card_list_query`, with one shared
  list source resolver for search snapshots, local decks, backups, Archidekt
  decks, and Archidekt snapshots; Commander rule facts in `deck_validate`.
- `MtgMcp.Scryfall`: pure set, filter, group, and metric functions; the
  `game_changer` mapping and the extra card facts.
- `MtgMcp.Decks`: read-only loading of one deck from a backup file.
- `MtgMcp.App`: the shared `isError` wrapper, server instructions, prompts,
  `_meta` on destructive Archidekt writes, `stats_castability`, pairing and
  companion facts, and the two new provider tools.
- `MtgMcp.Statistics`: reused unchanged by `stats_castability`.
- `MtgMcp.OnCurve`: group `produces` and the `can-cast` condition reuse
  `OnCurveManaPayment`.
- `MtgMcp.Spellbook`: one new `/estimate-bracket` request and mapping that
  drops `bracketTag`.
- `MtgMcp.Playgroup`: a pure game aggregation function; no new endpoint.
- `tests/MtgMcp.Architecture.Tests`: the approved surface list and counts.
- App, E2E, and package smoke tests; README, `docs/toolsets.md`, and
  CHANGELOG.

`MtgMcp.Core`, storage formats, operation modes, credential handling, and
the 16 existing Playgroup tools do not change. The only provider HTTP
contract change is the Spellbook `/estimate-bracket` read.

## Owner Decisions

Answered by the owner on 2026-10-25:

1. Deck writes stay on by default. (Superseded by item 6: everything is on
   by default.)
2. `standard` stays the default card format; `concise` is opt-in.
3. Simulation (now phase 6) is approved as planned. This approval serves as
   the `experimental-goldfish-feasibility` decision for caller-ruled sampling
   only; full-game goldfish play stays deferred. Phase 6 step 1 records this
   in the rewrite umbrella's post-cutover registry.
   `configurable-decision-models`, `conservative-goldfish-v2`,
   `simulation-profile-evidence`, and `stats-lab-interaction-readiness` were
   closed as superseded and moved to `completed/` on 2026-10-27.
4. Add `card_lists_compare` in its own phase before simulation, and add the
   `draw-cards` simulation rule.
5. Add `card_list_query`, card facts, mana pip and source metrics, Commander
   rule facts, and backup and Archidekt snapshot list sources, plus the free
   first mulligan and sample hands. Leave out EDHREC and collections. Phase 4
   step 1 records the rule-facts change in the rewrite umbrella.
6. Turn every toolset on by default; the context cost is acceptable. Keep
   Archidekt and Playgroup off without credentials. There are no admin tools,
   only deckbuilding tools, so there is no admin toolset. Read-only is an
   option that is off by default, which makes `remote` the default mode.
   Make sure tool names are clear.

Added by the owner on 2026-10-27, after a review of MCP guidance and common
Commander deck analysis:

7. Fix colored payment in `stats_simulate`, set `isError` on failures, and
   send server instructions.
8. Add description examples, a result size limit, untrusted-text notes, a
   confirmation hint on destructive Archidekt writes, MCP prompts, and
   held-out eval tasks.
9. Add `stats_castability`, pairing and companion facts, Spellbook bracket
   facts without a label, Playgroup game statistics, and the extra card
   fields. Phase 4 step 1 records the Spellbook `/estimate-bracket` change in
   the rewrite umbrella.

10. Add an Archidekt deduplication workflow: the agent orders the groups
    that decide which copy stays, and it runs on the bound local deck before
    the existing push. Fix the two reported bugs as part of the full plan,
    not as a separate patch.

No open questions remain. The 2026-10-27 additions had an independent
design review on the same day; see Validation Evidence.

## Planning Readiness

- [x] The current surface was listed from source on 2026-10-25.
- [x] `deck_apply_changes` was checked to cover every removed single-step tool.
- [x] Backups, the `common-v1` preset, and identity reconciliation were read
      to decide keep or remove.
- [x] MCP guidance sources are recorded in the SRD.
- [x] An independent design review examined this packet; its findings are
      addressed (see Validation Evidence).
- [ ] The enum and unknown-property schema spikes have run (phase 1, step 1).
- [ ] The SDK spike for `isError`, tool `_meta`, instructions, and prompts
      has run (phase 1, step 1).
- [x] An independent design review has examined the 2026-10-27 additions;
      its findings are addressed (see Validation Evidence).
- [x] The owner has answered the open questions (2026-10-25).
- [ ] The owner has set `Implementation authorized: Yes`.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-10-25 | Source inventory of tools, toolsets, and annotations | Done | 97 tools; counts match README and `docs/toolsets.md`. |
| 2026-10-25 | `DeckChangeInput` union check | Done | Covers metadata, entry, category, assignment, and provider-binding changes (`src/MtgMcp.App/Decks/DeckChangeInput.cs:11-21`). |
| 2026-10-25 | Enum converter use in source | Not present | No `JsonStringEnumConverter` exists yet; phase 1 adds and proves it. |
| 2026-10-25 | Independent design review 1 | Findings addressed | Two P1 (Archidekt round trip setup; unstated simulation play choices) and four P2 (tag path, concise fields, removal review, export placement) were fixed. Later owner decisions replaced the toolset-link fix with default-on toolsets. |
| 2026-10-25 | Independent design review 2 | Findings addressed | Counts and source citations verified. Fixed: working-name versus final-name rule; unknown-property rejection moved to phase 3 as a breaking change; merge rule rewritten and merged inputs given a required `kind`; one owning-toolset rule for list sources; credential presence defined per setting; small fingerprint-bound cursor; backup schema check; job split between `deck_get`, `card_list_query`, and `stats_deck_summary`; SRD table fixes; Game Changer fact without `passed`; Archidekt IDs as strings; optional `expectedRevision` for read-only deck sources. |
| 2026-10-25 | Removal review sources | Done | Backups copy all of `decks.db` (`DeckWriteTools.cs:314`); `common-v1` maps four role names to four tag IDs (`DeckCategoryRuleResolver.cs:26-32`); reconciliation is exact-only (`DeckIdentityReconciliation.cs:708`). |
| 2026-10-27 | Gap review against MCP guidance and Commander analysis practice | Findings adopted | Today's on-curve estimator pays colored costs (`OnCurveCalculator.cs:143-186`, `OnCurveMana.cs:266`), so a generic-only `stats_simulate` could not meet SIM-004. No `IsError` use in `src/` or `tests/`; no server instructions. `stats_mana_availability` takes one requirement per call (`TurnManaInverseModels.cs:90`). Spellbook find-my-combos results already carry raw almost-included combos. Deck zones are free text, so a `companion` zone needs no storage change. Added SIM-010, ERR-002, SURF-009, SCHEMA-006, SAFE-001, SAFE-002, SIZE-001, PROMPT-001, EVAL-003, RULE-003, RULE-004, FACT-002, QUERY-007, CAST-001, CAST-002, BRACKET-001, PG-001, PG-002. |
| 2026-10-27 | Bugs from real `0.10.0` use, reported by another session | Confirmed in source; adopted | `deck_apply_changes` returns only the SDK's generic error when `kind` is not the first property: `[JsonPolymorphic]` at `DeckChangeInput.cs:10`, with no `AllowOutOfOrderMetadataProperties` in `src/`. Archidekt push preview counts reordered secondary categories as changes: ordered `SequenceEqual` at `ArchidektSyncPlanner.cs:540`, while the post-write check at `:460` compares as a set. In one push, 43 of 52 entry updates were order-only. Added SCHEMA-007, ERR-003, ARCH-001 to phase 1. |
| 2026-10-27 | Third bug from the same session | Confirmed in source; adopted | After `archidekt_push_apply`, the new baseline maps IDs only from remote rows keyed by `StableGuid` of the provider ID (`ArchidektLocalMapper.cs:127-142`, called at `ArchidektCoordinator.cs:523`). Locally created entries and categories are never linked, so the next preview planned a remove and re-add for 2 pushed cards and showed the pushed category as removed plus added. Added ARCH-002 to phase 1, cases A04 to A07. |
| 2026-10-27 | Independent design review 3 (2026-10-27 additions) | Findings addressed | 18 findings, all checked against source and adopted. P1: the `on-curve` preset could not replay today's land choice (`OnCurveTrial.cs:117,133,225-290`), so `pick: "target-first"` and `can-cast` `byTurn`/`inHand` were added. `stats_castability`'s conditional value needs new exact math, and hybrid and Phyrexian costs are skipped (`TurnManaInverseModels.cs:78-112`). Playgroup games have no commander field or winning turn (pinned OpenAPI `Game` and `Participation`), so `groupBy: commander` was dropped and the metric is `averageTotalRounds`. P2: FACT-002 narrowed to unmapped fields (`ScryfallMapper.cs:51-56,78`), with `fields` able to pick extra fields; commander eligibility includes Backgrounds; "up to N" singleton limits; Lutri, Changeling, and companion identity and legality; joint payment for spent mana; castability limited to deck sources; `unlinked` rows block the next push; size limit only for new and changed outputs. P3: case and acceptance mismatches, the error example, the baseline risk row, held-out tasks runnable on `0.10.0`, NAME-001 exceptions, SCHEMA-008 description length, DEDUP matching, and the binding-option location. Not verified by the review and moved to phase 4 checks: `/estimate-bracket` field names and Scryfall pairing keywords. |

## Agent Task Results

Filled in by EVAL-002. Held-out tasks are reported in their own rows.

| Run | Build | Tasks | Correct (median of 3) | Tool calls | Tool-result tokens | Invalid-parameter errors |
| --- | --- | --- | --- | --- | --- | --- |
| Baseline | `0.10.0` | Tuning tasks | Not run | | | |
| Baseline | `0.10.0` | Held-out tasks | Not run | | | |

## Result Size Measurements

Filled in by phase 1 (SIZE-001): the largest default result of every tool,
in UTF-8 characters, and whether it exceeds 40,000. Not measured yet.

## Deferred Work

- Full-game goldfish play, opponent models, and Phyrexian life payment in
  simulation.
- Any change to the 16 existing Playgroup tools.
- Spellbook's `bracketTag` and any bracket or power label.
- EDHREC data (inclusion, synergy, average decks, salt) and card collections.
- Enters-tapped land modeling in mana counts.
- A deck revision history table.
- Moving reference data to MCP resources. Resource access varies by client, so
  agent-needed data stays reachable through tools.
- Adapting to MCP 2026-07-28 fields such as `ttlMs`, which the SDK owns. Phase
  1 only records what SDK 2.2.0 emits.
