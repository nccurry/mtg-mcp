# Focused Agent Tool Surface PLC Packet

> [!IMPORTANT]
> The owner authorized implementation on 2026-10-27. Work proceeds phase by
> phase as listed in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## Lifecycle

- Status: In progress
- Folder: `docs/llms/plcs/in-progress/focused-agent-tool-surface/`
- Owner: mtg-mcp
- Created: 2026-10-25
- Last updated: 2026-10-28
- Current phase: Phase 6 (caller-ruled simulation and 0.12.0), implemented on `fats/phase6` and awaiting its strict audit. Phases 1 and 2 delivered 2026-10-27; phases 3 to 5 delivered 2026-10-28, with the 0.11.0 release prepared but not published (held for the owner); the 0.12.0 release is prepared but not published; the EVAL-002 baseline is deferred to the owner.
- Implementation authorized: Yes (owner, 2026-10-27)
- Phase 1 status: Delivered 2026-10-27 (strict audit passed except EVAL-002).
- Phase 2 status: Delivered 2026-10-27 (strict audit passed).
- Phase 3 status: Delivered 2026-10-28 (strict audit passed after three passes).
- Phase 4 status: Delivered 2026-10-28 (strict audit passed after two passes; agent task rerun deferred with EVAL-002).
- Phase 5 status: Delivered 2026-10-28 except publishing (strict audit passed after two passes; 0.11.0 packed and smoke-tested, not tagged or published).
- Phase 6 status: Implemented 2026-10-28 on `fats/phase6` (strict audit pending; release 0.12.0 packed and smoke-tested, not tagged or published). Not marked complete.
- EVAL-002 baseline: Deferred to the owner. This session has no agent-eval harness, so the `0.10.0` baseline rows below stay "Not run".

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
| Publish choice values as JSON Schema enums through `[AllowedValues]` annotations. | Adopted in phase 1 after the SDK spike | Values and C# types stay the same; works on adapter request records; SDK 2.2.0 publishes it as `enum`. | [SADD](SADD.md#enum-schemas) |
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
- [x] The enum and unknown-property schema spikes have run (phase 1, step 1; results in Validation Evidence, 2026-10-27).
- [x] The SDK spike for `isError`, tool `_meta`, instructions, and prompts
      has run (phase 1, step 1).
- [x] An independent design review has examined the 2026-10-27 additions;
      its findings are addressed (see Validation Evidence).
- [x] The owner has answered the open questions (2026-10-25).
- [x] The owner has set `Implementation authorized: Yes` (2026-10-27).

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-10-25 | Source inventory of tools, toolsets, and annotations | Done | 97 tools; counts match README and `docs/toolsets.md`. |
| 2026-10-25 | `DeckChangeInput` union check | Done | Covers metadata, entry, category, assignment, and provider-binding changes (`src/MtgMcp.App/Decks/DeckChangeInput.cs:11-21`). |
| 2026-10-25 | Enum converter use in source | Superseded | No `JsonStringEnumConverter` existed on this date. Phase 1 chose `[AllowedValues]` for string parameters (see the 2026-10-27 spike row), so the packet adds no converter for them. |
| 2026-10-25 | Independent design review 1 | Findings addressed | Two P1 (Archidekt round trip setup; unstated simulation play choices) and four P2 (tag path, concise fields, removal review, export placement) were fixed. Later owner decisions replaced the toolset-link fix with default-on toolsets. |
| 2026-10-25 | Independent design review 2 | Findings addressed | Counts and source citations verified. Fixed: working-name versus final-name rule; unknown-property rejection moved to phase 3 as a breaking change; merge rule rewritten and merged inputs given a required `kind`; one owning-toolset rule for list sources; credential presence defined per setting; small fingerprint-bound cursor; backup schema check; job split between `deck_get`, `card_list_query`, and `stats_deck_summary`; SRD table fixes; Game Changer fact without `passed`; Archidekt IDs as strings; optional `expectedRevision` for read-only deck sources. |
| 2026-10-25 | Removal review sources | Done | Backups copy all of `decks.db` (`DeckWriteTools.cs:314`); `common-v1` maps four role names to four tag IDs (`DeckCategoryRuleResolver.cs:26-32`); reconciliation is exact-only (`DeckIdentityReconciliation.cs:708`). |
| 2026-10-27 | Gap review against MCP guidance and Commander analysis practice | Findings adopted | Today's on-curve estimator pays colored costs (`OnCurveCalculator.cs:143-186`, `OnCurveMana.cs:266`), so a generic-only `stats_simulate` could not meet SIM-004. No `IsError` use in `src/` or `tests/`; no server instructions. `stats_mana_availability` takes one requirement per call (`TurnManaInverseModels.cs:90`). Spellbook find-my-combos results already carry raw almost-included combos. Deck zones are free text, so a `companion` zone needs no storage change. Added SIM-010, ERR-002, SURF-009, SCHEMA-006, SAFE-001, SAFE-002, SIZE-001, PROMPT-001, EVAL-003, RULE-003, RULE-004, FACT-002, QUERY-007, CAST-001, CAST-002, BRACKET-001, PG-001, PG-002. |
| 2026-10-27 | Bugs from real `0.10.0` use, reported by another session | Confirmed in source; adopted | `deck_apply_changes` returns only the SDK's generic error when `kind` is not the first property: `[JsonPolymorphic]` at `DeckChangeInput.cs:10`, with no `AllowOutOfOrderMetadataProperties` in `src/`. Archidekt push preview counts reordered secondary categories as changes: ordered `SequenceEqual` at `ArchidektSyncPlanner.cs:540`, while the post-write check at `:460` compares as a set. In one push, 43 of 52 entry updates were order-only. Added SCHEMA-007, ERR-003, ARCH-001 to phase 1. |
| 2026-10-27 | Third bug from the same session | Confirmed in source; adopted | After `archidekt_push_apply`, the new baseline maps IDs only from remote rows keyed by `StableGuid` of the provider ID (`ArchidektLocalMapper.cs:127-142`, called at `ArchidektCoordinator.cs:523`). Locally created entries and categories are never linked, so the next preview planned a remove and re-add for 2 pushed cards and showed the pushed category as removed plus added. Added ARCH-002 to phase 1, cases A04 to A07. |
| 2026-10-27 | Independent design review 3 (2026-10-27 additions) | Findings addressed | 18 findings, all checked against source and adopted. P1: the `on-curve` preset could not replay today's land choice (`OnCurveTrial.cs:117,133,225-290`), so `pick: "target-first"` and `can-cast` `byTurn`/`inHand` were added. `stats_castability`'s conditional value needs new exact math, and hybrid and Phyrexian costs are skipped (`TurnManaInverseModels.cs:78-112`). Playgroup games have no commander field or winning turn (pinned OpenAPI `Game` and `Participation`), so `groupBy: commander` was dropped and the metric is `averageTotalRounds`. P2: FACT-002 narrowed to unmapped fields (`ScryfallMapper.cs:51-56,78`), with `fields` able to pick extra fields; commander eligibility includes Backgrounds; "up to N" singleton limits; Lutri, Changeling, and companion identity and legality; joint payment for spent mana; castability limited to deck sources; `unlinked` rows block the next push; size limit only for new and changed outputs. P3: case and acceptance mismatches, the error example, the baseline risk row, held-out tasks runnable on `0.10.0`, NAME-001 exceptions, SCHEMA-008 description length, DEDUP matching, and the binding-option location. Not verified by the review and moved to phase 4 checks: `/estimate-bracket` field names and Scryfall pairing keywords. |
| 2026-10-27 | Phase 1 delivery and strict audit | Passed except EVAL-002 | Implemented on `feat/focused-agent-tool-surface` (ccdf70c) and repaired on `fats/phase1-repair` (b7baf39, c560f85, 5cc4b12, a6d1305). Audit pass 1: 14 specialists on one snapshot; security and performance had no findings; all supported findings fixed (one rejected with E2E evidence: the filter catch path is the live binding path). Pass 2 (correctness, justified by the Archidekt contract change): one P3 fixed. Checks at c560f85: `task ci` passed (828 tests; coverage App 90.94%, Archidekt 91.16%, others 93.83% or higher); full E2E 64/64. Checks at a6d1305: Archidekt tests 99/99 and format check. EVAL-002 baseline deferred to the owner; phase 3 proceeds by owner instruction to execute the whole packet. |
| 2026-10-27 | Phase 1 SDK spike (ModelContextProtocol 2.2.0), results parent-verified | Done; adopted | `[AllowedValues]` and `JsonStringEnumConverter` both publish `enum`, and `ToolSchemaSdkTests` shows a subclass of `AllowedValuesAttribute` does too. The default input schema does not set `additionalProperties:false`. `isError` can be set from a call-tool filter (`AddCallToolFilter`). Argument-binding exceptions propagate through call-tool filters and can be caught there (an E2E binding test failed with only a result-side check and passed once the filter caught the exception). Serializer options are set per registration through `WithTools(target, options)` and `McpServerToolCreateOptions.SerializerOptions`. `McpServerToolCreateOptions.Meta` and `McpMetaAttribute` exist for tool `_meta`. `McpServerOptions.ServerInstructions` exists. `WithPrompts` exists. `docs/design-goals.md` asks for "enums for simple categories"; publishing choices as JSON Schema enums meets that at the schema level, while the C# parameter types stay strings. |
| 2026-10-27 | Phase 1 audit repair: bound method in a call-tool filter | `RequestContext.MatchedPrimitive` is set, but it does not expose the method | The primitive's runtime type is the internal `AIFunctionMcpServerTool`, so a filter cannot read the bound method (`UnderlyingMethod` is not reachable). `ToolCallPolicy` keeps its name-to-method index and now throws at startup if two tools share a name (`IndexToolMethods_DuplicateToolName_Throws`). |
| 2026-10-27 | Phase 1 audit repair: error flag and binding diagnosis | Verified by E2E | The branch that diagnosed a generic SDK error result was removed because the SDK reports binding failures as exceptions; `ToolInputBindingMcpTests` (unknown kind, wrong type, kind last, operation failure) still pass. `EveryToolWithRequiredArguments_EmptyCall_ReturnsFlaggedInvalidArguments` calls every listed tool that has required arguments with `{}` and checks `isError`, `invalid-input`, `invalid-arguments`, and a `path` naming a required argument. Deviation from ERR-002's wording: tools whose arguments are all optional are not called with `{}`, because an empty call would run them (for example a corpus download); their `isError` follows the same one filter, covered by `Filter_ReturnedResult_AppliesKindRule`. |
| 2026-10-27 | Phase 1 audit repair: every-union serializer proof (SCHEMA-007) | Done, as an architecture test | `ToolRegistrations_PassSharedSerializerOptions` scans App source and fails if any `WithTools` or `McpServerTool.Create` call omits `ToolCallPolicy.SerializerOptions` (checked by temporarily removing one; it failed). `SerializerOptions_ReadUnionKindInAnyPosition` checks `AllowOutOfOrderMetadataProperties` is on. `ApplyChanges_KindAfterOtherProperties_AppliesChange` runs a real union with `kind` last through the server. |
| 2026-10-27 | Phase 1 audit repair: E2E launcher cleanup | Checked | After the client is disposed, the `cmd.exe` launcher and its `dotnet` child were both gone within 500 ms, so disposing the stdio transport ends the process tree and the test needs no process tracking. The folder-delete retry stays only for the short delay before Windows releases the folder. |
| 2026-10-27 | Phase 1 audit repair: Archidekt push links | Verified in source and tests | A push no longer blocks on unlinked rows: a later push removes and re-adds an unlinked row (`PreviewPush_AfterPushLeftRowUnlinked_PlansRemoveAndAddWithoutDuplicate`). A successful push cannot normally leave an entry unlinked, because the push verification and the link step use the same row matching; the `unlinkedLocalIds` field is a safety report and is covered at mapper and serialization level. |
| 2026-10-27 | Phase 2 implementation (card response formats, deck card view, size limit) on `fats/phase2` over base 61ba777, then repair after strict audit pass 1 | Checks passed; repair under re-audit | `task ci` from PowerShell after the repair: format check, strict lint build, surface report, 896 non-live tests with coverage (App 91.18%, Archidekt 91.40%, Core 99.82%, Decks 93.83%, OnCurve 97.13%, Playgroup 95.81%, Scryfall 95.18%, Spellbook 94.63%, Statistics 96.45%), process smoke, MCP smoke 54/54. Full E2E run separately. Repair: `fields` and `include` publish `enum` on their items (a schema hook in `ToolItemChoices`); `deck_get` cursor checks fingerprint, then revision, then position, and refuses missing, null, negative, or wrong cursors without an exception; `include: []` rejected; card data is not reported as found when it was not read; reversible cards use the front face and say so; sizes are measured with the server serializer options passed to `ScryfallService`; each page is formatted once; cheapest price reads English printings with one query for all cards on a page; old card-read wrappers removed; names and wording changed as listed in the audit. Tests now use six real Scryfall card objects recorded once (a 15-format staple, transform, adventure, planeswalker, null prices, reversible) and golden JSON recorded from base 61ba777 for the standard card, search page, and single card. Fixture sizes with the server-style serializer: 25 recorded-shape cards were 135,567 bytes in `standard` and 22,082 in `concise` (16%); a `concise` page of 100 held 43 cards in 37,841 bytes; a 100-entry `deck_get` page held 40 entries. Decisions adopted in the SADD and SRD: `rarity`, `power`, `toughness`, `loyalty`, and face `oracleId` are added to `standard`; `gameChanger` and `edhrecRank` come from the card object; only `tags` and `cheapestPriceUsd` need installed data; `cheapestPriceUsd` is the lowest USD price among English printings and is `null` for reversible cards because printings are indexed by top-level Oracle ID (no storage change made). Audit pass 2 repair: reversible cards emit a null cheapest price even when a normal printing shares the front face's Oracle ID; a lookup against a generation that is no longer complete now reports not installed (one connection checks completeness and reads); limit notes describe only the kept cards; the item counter returns its formatted page directly and the page carries it (no formatter state); concise fields are a union of plain, installed-data, and price fields; one schema hook in `ToolItemChoices`; the service requires its result serializer options; the price query returns one row per Oracle ID with a window function and extracts the price once; deck pages always advance. The plan and SRD now say the unknown-property fallback must be applied in `ToolItemChoices.Create`, `DeckBatchWriteTools`, and `StatisticsToolsetManifest`. Output schemas of all operation-result tools are open (`true`); a test pins that card tools are no worse. |
| 2026-10-27 | Phase 2 delivery and strict audit | Passed | Implemented on `fats/phase2` (a4fb2fc) and repaired (aac76e6, 6ff9422). Audit pass 1: 14 specialists; security had one low finding, all supported findings fixed. Pass 2 (correctness, performance, code quality; justified by the structural repair): performance clean; correctness and code-quality findings fixed and verified by diff inspection and checks. Checks at 6ff9422: `task ci` passed (899 tests; coverage App 91.20%, Archidekt 91.40%, others 93.83% or higher); full E2E 64/64. Open owner decision: the `standard` default page still exceeds 40,000 bytes; descriptions recommend `concise` or a smaller `pageSize`. |
| 2026-10-28 | Phase 3 implementation (default selection, names, merges, preset removal) on `fats/phase3` over base c4ed454 | Checks passed; strict audit pending | All 11 steps. Selection: every toolset on by default; `archidekt` and `playgroup` off without credentials, even when named (presence only, no provider call, no secret read), with `disabledReason` in the capabilities resource (schema version 8) and one stderr line; invalid Playgroup configuration fails startup only when named. Default mode is `remote`. Removed the nine single-step deck tools, `deck_category_rules_validate`, and the `common-v1` preset (preview `expectedRevision` is now optional). Merged six tools (`scryfall_cards_get`, `scryfall_card_details`, `scryfall_reference_get`, `scryfall_search_snapshots_get`, `archidekt_folders_get`, `archidekt_snapshots_get`) and applied the FIXTURES names. Unknown-property rejection: root schemas set `additionalProperties: false` through the one registration helper (`ToolRegistration`, which replaced `ToolItemChoices` and now builds every tool, including statistics and the batch tool), the call filter names an unknown top-level argument, and `UnmappedMemberHandling.Disallow` makes nested unknown fields fail binding with their path. Server instructions (at most 2,000 characters, built from selection and mode), untrusted-text notes on 31 provider tools, `anthropic/requiresUserInteraction` on the six destructive Archidekt remote writes (SAFE-002 done, not deferred), and description examples for `local_deck_apply_changes` (3) and `local_deck_category_rules_preview` (2), run by tests. Counts: 30/43/43 with no credentials; 39/53/64 with Archidekt only; 53/67/80 with both (verified by unit tests and by E2E over real processes). `task ci` passed: 1,003 non-live tests; coverage App 91.81%, Archidekt 91.13%, Core 99.81%, Decks 93.83%, OnCurve 97.13%, Playgroup 95.81%, Scryfall 95.45%, Spellbook 94.63%, Statistics 96.45%; MCP smoke 66/66. `task test:integration` passed; full E2E 80/80 (the process tests also check the stderr notice). Test notes: E2E sessions now state their provider credentials so a developer credential file never changes the tool list; the live acceptance manifest and calls were converted to the new surface but not run. EVAL-002 stays deferred. |
| 2026-10-28 | Phase 3 strict audit pass 1 repair on `fats/phase3` (on top of `ac84dc9`) | Repair done; checks recorded in the delivery report; re-audit pending | Fixed the 31 listed items. Safety: untrusted-text notes on `archidekt_push_apply` and `archidekt_snapshot_restore_apply`; `anthropic/requiresUserInteraction` also on `playgroup_game_events_batch_create` (seven tools). Shape: `archidekt_folders_get` returns a union keyed by `kind`; `scryfall_card_details` takes one `ScryfallCardLookup` (resolved to one Oracle ID, then prints and rulings are read); a single fuzzy lookup in `scryfall_cards_get` is validated like the rest and a miss is a `not-found` row. Schemas: every object node is closed (`additionalProperties: false`), union `kind` stays; E2E proves no open object. Calls for hidden or gated tools answer as unknown tools, because the registration step now records each tool method in one catalog (`ToolArgumentCatalog`) and the filter only diagnoses matched tools. Nested unknown fields read "Property 'x' is not accepted here." with long names cut at 64 characters without splitting surrogate pairs. Code: one shared `OperationFailure.Forward`, one `KindFields` helper, credential presence computed once, `DisabledToolset` naming with a per-toolset notice, `IsDefaultEnabled` removed, the unreachable non-inline rule fallback removed, `ToolRegistration` is the single registration helper (the packet previously named `ToolItemChoices.Create`). Docs and wording: server-instruction job labels, "Saved Scryfall requests:" snapshot tools, "corpus" removed from descriptions, versioning exception, AGENTS rules, changelog corrections. Tests: golden literals for category rule decisions, folder and snapshot kind tests, preview-without-revision then apply then conflict, golden server instructions, `ApplyChanges_*` tests moved to `DeckBatchWriteToolsTests`. |
| 2026-10-28 | Phase 3 strict audit pass 2 repair on `fats/phase3` (on top of 8440dd5) | Repair done; checks recorded in the delivery report; re-audit pending | The argument catalog is now owned by each server (a singleton in the service collection filled by `WithRegisteredTools`) and the call filter reads it from the request services; there is no process-wide registry and no test warm-up, and `DiagnoseArguments` and `FindUnknownArgument` fail loudly for a tool the catalog does not hold. The remaining generic failure-forwarding copies use `OperationFailure.Forward`. `scryfall_card_details` checks page size and raw output before it resolves the card. A nested unknown name that holds dots or brackets keeps its own path segment. A single fuzzy lookup in cache-only mode with nothing cached returns a `not-cached` row. A rule source that is not inline returns `invalid-input` at `source`. `KindFields.Require` serves the snapshot ID check. The E2E session retries the working-folder delete when a start fails. The 2026-10-27 phase 2 row above is restored to its original text; the correction is recorded here and in the pass 1 row. |
| 2026-10-28 | Phase 3 delivery and strict audit | Passed | Implemented on `fats/phase3` (ac84dc9) and repaired (8440dd5, 8587861). Audit pass 1: 14 specialists; PLC alignment, correctness, code quality, performance, ponytail, visual code, and file hygiene passed; security, tests, abstraction, docs sync, dead code, and plain language failed on moderate findings, all fixed (31 items). Pass 2 (correctness and security; code quality, abstraction, and tests; justified by the structural repair): correctness passed; the process-wide argument catalog and remaining forwarding copies were fixed (11 items). Pass 3 (correctness and code quality of the per-server catalog): passed, three low notes (no warning when a matched tool is missing from the catalog, one test mixing two registrations, three private forwarding copies in Decks and OnCurve left for phase 4). Parent checks at 8587861: `git diff --check` clean; `task ci` passed (1,034 tests, coverage gates, MCP smoke 68/68); `task test:integration` passed; full E2E 83 passed, 6 live skipped. Open owner decision: the snapshot tools hold every Scryfall request type, but the approved names are `scryfall_search_snapshots_get` and `scryfall_search_snapshot_delete` (FIXTURES line 42 also says the delete tool is unchanged); descriptions now say "Saved Scryfall requests". |
| 2026-10-28 | Phase 4 implementation (card list tools, Commander rule facts, duplicates, castability, bracket facts, game statistics) on `fats/phase4` over base 526190c | Checks passed; strict audit pending | All 13 steps. New tools: `local_deck_duplicates_preview`, `card_lists_compare`, `card_list_query`, `stats_castability`, `spellbook_deck_bracket_facts_get`, `playgroup_games_stats_get`; `local_deck_validate` gained `rules`. Counts: 35/48/48 with no credentials, 44/58/69 with Archidekt only, 59/73/86 with both. Checks: `task ci` passed (1,258 unit and integration tests; 68 smoke E2E); `task test:integration` 57/57; full E2E 83 passed, 6 live skipped, 0 failed; coverage gate passed (line: App 95.11%, Archidekt 92.26%, Core 99.82%, Decks 94.68%, OnCurve 98.62%, Playgroup 96.68%, Scryfall 95.56%, Spellbook 95.37%, Statistics 96.36%). Captured read-only: one Commander Spellbook `/estimate-bracket` response and 75 Scryfall cards (`cards/named`, `cards/search`); Comprehensive Rules text dated 2026-09-25. SADD adjusted to the captured data: Partner keyword rows, Friends forever read from text, exact deck-limit phrases, rule 903.3 eligibility. Design decisions made in phase 4 (not data findings): the castability work limit of 480, duplicate ties decided at the cut line, and 6-place rate rounding. Both owner decisions recorded in the rewrite program registry. EVAL-002 deferred to the owner; T-rows not run. |
| 2026-10-28 | Phase 4 strict audit pass 1 repair on `fats/phase4` (on top of `b062657`) | Repair done; checks recorded in the delivery report; re-audit pending | 10 of 14 specialists failed pass 1; every supported finding was fixed in one round. Highest priority: `stats_castability` works under `--toolsets stats` (it builds its own deck access); one request-wide work budget, turns 1 to 99, and no card dropped silently (`no-scryfall-identity`, `not-in-card-data`, `mana-value-above-turn-limit`); bracket facts cut combos after the card cut and re-measure, fail on a missing flag instead of reading false, and match entries by Oracle ID then front-face name; Jegantha reads the front face cost of adventure cards; one card-face reader in `MtgMcp.Scryfall` (`ScryfallCardFaces`) replaces three parsers; a total entry cap across list sources; tag and price lookups only when a query uses them; repeated groupBy, metric, and row fields are refused; Playgroup reads are page-capped and de-duplicated and count a deck once per game; a commander with no card data makes color identity unknown; the server instructions list the new jobs (still at most 2,000 characters). Tests added for each, plus 8 offline E2E calls of the new tools, union binding tests, and host wiring tests. Checks: `task ci` passed (1,315 unit and integration tests; 68 smoke E2E); `task test:integration` passed; full E2E 91 passed, 6 live skipped, 0 failed; coverage gate passed (line: App 95.38%, Archidekt 92.26%, Core 99.82%, Decks 94.63%, OnCurve 98.62%, Playgroup 96.71%, Scryfall 95.82%, Spellbook 95.45%, Statistics 96.38%). Counts unchanged: 35/48/48, 44/58/69, 59/73/86. Phase 4 is not marked complete. |
| 2026-10-28 | Phase 4 strict audit pass 2 | Passed in all three combined reviews; low notes cleaned up | The three combined pass 2 reviews (code, abstraction and language; plan, docs and tests; correctness, security and performance) all passed. A small cleanup commit on `fats/phase4` handles their low notes: one color list and one type-word reader in `ScryfallCardFaces`, a JSON null key for the missing-value group in `card_list_query`, bracket facts cut flagged cards too (`cardsOmitted`) and measure the final shape, a repeated entry ID in a castability select is refused, castability counts its group views in the size cut, and the missing tests and documentation are added. Phase 4 is not marked complete. |
| 2026-10-28 | Phase 4 delivery and strict audit | Passed | Implemented on `fats/phase4` (b062657), repaired (6c7dcd6), and cleaned up (3ee3fca). Audit pass 1: 14 specialists; visual code, file hygiene, and dead code passed with low notes; the other 11 failed on medium findings, all fixed or dropped by parent triage (stats-only castability, bracket size cut, Jegantha with adventure cards, request-wide work budget, total entry cap, card facts read only when used, strict Spellbook flags, Oracle ID matching, server instruction jobs, and missing tests). Pass 2 (three combined reviews over the repair): all passed; their low notes were cleaned up. Parent checks at 3ee3fca: `task ci` passed (1,319 unit and integration tests, coverage gates, MCP smoke 68/68); `task test:integration` passed; full E2E 91 passed, 6 live skipped, 0 failed. Counts 35/48/48, 44/58/69, 59/73/86. Step 13 (agent task rerun) is deferred with EVAL-002. |
| 2026-10-28 | Phase 5 implementation (documents, prompts, release preparation for 0.11.0) on `fats/phase5` over base c6fa9dd | Checks passed; strict audit pending; release not published | DOC-001: README gained worked examples (`card_lists_compare` difference of a saved search and a deck, `card_list_query` curve and pips, `local_deck_validate` with `rules: "commander"`) with results recorded from a real run, and the inline category rules that replace `common-v1` (its four tag IDs, from history at ac84dc9^); `ReadmeExampleTests` binds every marked README request through `ToolCallPolicy.DiagnoseArguments`. CHANGELOG already held the full old-to-new table; it now says the inline rule set replaces `common-v1` with a rules example, and `Unreleased` became `0.11.0 - 2026-10-28` with an empty `Unreleased`. The naming test reads the 0.11.0 section. Counts rechecked 35/48/48, 44/58/69, 59/73/86. Docs swept: README, `docs/toolsets.md`, `docs/rewrite-guide.md`, `docs/architecture.md`, `docs/compatibility.md`, `llms.txt`, `src/MtgMcp.App/AGENTS.md`. PROMPT-001: `audit-deck`, `build-from-commander`, `compare-decks` (`WorkflowPrompts`, `WithPrompts<T>`, SDK 2.2.0); arguments must be UUIDs; always offered; text names only tools of the credential-free default selection and tells the agent to skip a hidden tool (a test checks every snake_case word against the registered surface). Capabilities resource reports `promptCount` 3 (the SADD did not require it; the old value 0 would have been false); prompt list sorted and no list-changed claim. Version 0.11.0 in the project file, `server.json`, and three version tests. Checks: `git diff --check` clean; `task ci` passed (1,332 tests, MCP smoke 71/71; line coverage App 95.46%, Archidekt 92.26%, Core 99.82%, Decks 94.63%, OnCurve 98.62%, Playgroup 96.71%, Scryfall 95.82%, Spellbook 95.45%, Statistics 96.38%); `task test:integration` 99 and 62 passed; full E2E 94 passed, 6 live skipped, 0 failed; `task pack VERSION=0.11.0` and `task release:tool-smoke VERSION=0.11.0` passed (71/71 against the installed package). Phase 5 is not marked complete. Publishing (tag, push, NuGet, GitHub release, MCP Registry) is held for the owner. |
| 2026-10-28 | Phase 5 strict audit repairs on `fats/phase5` (on top of c8eb0b6) | Repair done; checks recorded here; release not published | Three of five combined reviews failed (correctness, tests, ponytail); every finding was fixed. Prompt argument errors (missing or not a UUID) are `McpProtocolException` with `InvalidParams` and a message naming the argument; a missing argument is caught by a get-prompt filter because the SDK fails it before the prompt runs (E2E asserts code and message). `ReadmeExampleTests` also runs `FindUnknownArgument`, with a negative control. Prompt steps fixed: the audit does not call `local_deck_get` with `include`; the curve query is `groupBy manaValue`, `quantity`, `where manaValue gt 0` (the query has no way to exclude only lands, so mana value 0 is left out and the agent says so), and pips and sources are a separate ungrouped query, in `audit-deck` and `compare-decks`. `promptCount` comes from `WorkflowPrompts.PromptCount`; the credential-free tool set in the test comes from the toolset descriptors; the outgoing filter is run on a real response in a test; the Taskfile and release-script smoke filters are compared by an architecture test; stale names and comments fixed; README and CHANGELOG wording fixed (examples text, em dash, prompt IDs and how to get them, read-only note, `primaryPriority`). Not done: asserting `ListChanged` is not true over E2E, because the client reports it true for tools too, so the check cannot show the filter's effect; the unit test covers the filter. Checks: `git diff --check` clean; `task ci` passed (1,336 tests, MCP smoke 71/71, line coverage App 95.44%, Archidekt 92.26%, Core 99.82%, Decks 94.63%, OnCurve 98.62%, Playgroup 96.71%, Scryfall 95.82%, Spellbook 95.45%, Statistics 96.38%); `task test:integration` 99 and 62 passed; full E2E 94 passed, 6 live skipped, 0 failed; `task pack VERSION=0.11.0` and `task release:tool-smoke VERSION=0.11.0` passed (71/71). Phase 5 is not marked complete; publishing is held for the owner. |
| 2026-10-28 | Phase 5 pass 2 repair: capabilities on the pinned protocol, on `fats/phase5` (on top of 2fc7a0d) | Repair done; checks recorded here; release not published | A raw stdio probe found that the pinned protocol 2026-07-28 has no initialize handshake: capabilities come from `server/discover`, whose result has `supportedVersions` and keeps `serverInfo` in `_meta`. `RemoveUnsupportedImplicitCapabilities` matched only a top-level `serverInfo`, so it never fired, and `logging` and the `listChanged` claims for tools, prompts, and resources were still advertised. This means the phase 1 and phase 3 claims of no logging and no changing tool list were not true on the pinned protocol until now. The filter now matches both shapes and removes `logging` and `listChanged` for tools, prompts, and resources (nothing sends resource list-changed notifications, and the lists are fixed per session). Tests: a unit test with the discover JSON shape; E2E asserts `Prompts`, `Tools`, and `Resources` `ListChanged` are not true and `Logging` is null through the SDK client. Raw discover result after the fix: `capabilities` is `{"prompts":{},"resources":{}}` (no tools in that probe because of `--toolsets none`). Low notes: the colors queries in `audit-deck` and `compare-decks` now include the `commander` zone; a present but non-string prompt argument gets the "must be a UUID" message; README and `docs/compatibility.md` wrapping, CHANGELOG fix line. Checks: `git diff --check` clean; `task ci` passed (1,337 tests, MCP smoke 71/71, App line coverage 95.39%, all others 92.26% or higher); `task test:integration` 99 and 62 passed; full E2E 94 passed, 6 live skipped, 0 failed; `task pack VERSION=0.11.0` and `task release:tool-smoke VERSION=0.11.0` passed (71/71). Phase 5 is not marked complete; publishing is held for the owner. |
| 2026-10-28 | Phase 5 delivery and strict audit | Passed; release 0.11.0 prepared, not published | Implemented on `fats/phase5` (c8eb0b6), repaired (2fc7a0d, c66b8db). Audit pass 1 (14 domains in five combined reviews): code, abstraction, dead code, and visual code passed; docs, plain language, and PLC alignment passed; correctness, tests, and ponytail failed on medium findings (prompt argument errors hidden as internal errors, README example test missing unknown top-level fields, curve query mixing lands), all fixed with the low notes. Pass 2 (correctness, tests, ponytail): passed. A parent stdio probe then found the capability filter never fired on the pinned protocol's `server/discover` result; fixed in c66b8db and checked by the parent against the diff and a raw probe. Parent checks at c66b8db: `task ci` passed (MCP smoke 71/71); `task test:integration` passed; full E2E 94 passed, 6 live skipped, 0 failed. DOC-001 and PROMPT-001 pass. The phase exit also needs the 0.11.0 release published; tagging and publishing are held for the owner. |
| 2026-10-28 | Phase 6 implementation (caller-ruled simulation, release preparation for 0.12.0) on `fats/phase6` over base 9d279de | Checks passed; strict audit pending; release not published | Steps 1 to 9 except the optional rename and the T-row rerun. **Fixtures first:** 16 runs of the original estimator (colored targets, next-turn and not-modeled lands, play and draw, a 7-card library, a 500-card library, 100 samples) were captured at 9d279de before any change (`ec3f5a4`), plus the old App fixture deck (82 of 100). **Engine** (`MtgMcp.OnCurve`, kept its name): `SimulationPlanner` checks every bound and engine rule with a JSON path, `SimulationTrial` plays one game, and `SimulationEngine` runs the samples and reports; it adds draw-cards (SIM-007), the free first mulligan (SIM-008), `sampleHands` (SIM-009), and group `produces` with `can-cast` on the one exact payment matcher, now extended for hybrid and Phyrexian symbols and for mana already spent (SIM-010). The old calculator, trial runner, request validator, and fingerprint were deleted. **App:** `stats_simulate` (toolset `stats`, read-only in every mode) with deck and group resolution (entry or category IDs over the main zone, overlapping groups refused, category entries outside the main zone listed as skipped), `ResultSize.Measure` cuts (sample hands, then the preset's land list) with stated omitted counts, `OperationFailure.Forward`, untrusted-text notes, and a request-wide work limit of 50,000,000 (samples x turns x deck cards; every number is bounded before the product, so nothing overflows). The `on-curve` preset expands to ordinary rules and reads installed card data through `CardListResolver` and `ScryfallCardFaces`. `local_deck_on_curve_estimate` and its seven App files were removed. **SIM-004:** all 16 captures and the App fixture deck replay to identical success counts through card data, preset expansion, request mapping, and the engine (`StatisticsSimulatePresetTests`); the engine benchmark also returns the recorded 796. **SIM-002:** fixed-seed golden values (engine: hits 6365 and 2625, fingerprint a78e31...; App: `Fixtures/simulate-golden.json`, a full result) plus the SplitMix64 vectors; rates and bounds are rounded to six places; the generator, shuffle, and tie-breaks use no runtime-dependent hashing or ordering. Windows was run here; `.github/workflows/ci.yml` runs the same tests on `ubuntu-latest` through `task coverage:report`, and that Linux run was not made in this session. **Required fields and unknown fields:** input records use `required` members, and `ToolCallPolicy` now reports a missing nested field as `Required field 'x' is missing at 'path'.` (required-field cases for every field, kind last in every union, unknown kinds including nested ones, and unknown properties at 21 depths, all in `StatisticsSimulateBindingTests`; E2E binds through the real server). **Counts:** unchanged with every toolset on: 35/48/48, 44/58/69, 59/73/86 (read-only/local/remote). By toolset, `decks` has 19 tools (10 read-only + 9 local writes, was 20) and `stats` has 10 (was 9). **Docs:** README (new section with a recorded real result, 6365 of 10,000), `docs/toolsets.md`, `docs/architecture.md`, `docs/design-goals.md`, `docs/north-star.md`, `docs/performance-ratchet.md`, `docs/rewrite-guide.md`, `docs/versioning.md` (the exception now names 0.12.0), `docs/potential-features.md`, `llms.txt`, `AGENTS.md`, the server instructions (the stats job line names `stats_simulate`; still under 2,000 characters), and CHANGELOG `0.12.0 - 2026-10-28` with the breaking removal and an old-to-new argument table. The prompts do not name the removed tool. The SADD gained an "As Built (phase 6)" section that lists the deviations: the `preset` argument, `manaFromByEntry`, the `sampleCards` legend and `manaAvailable`/`manaSpent`, the optional mulligan block, unspent-mana `mana-at-least`, `any` without `C`, the description label `Seeded sampling:` (the NAME-002 test was extended), and the bounds. **Rename:** `MtgMcp.OnCurve` to `MtgMcp.Simulation` is deferred: it touches the solution, the project and test project files, the coverage script and settings, lock files, the Taskfile, and the smoke filters, and its only benefit is a name; the task names `coverage:oncurve` and `benchmark:oncurve` stay. **Checks:** `git diff --check` clean; `task ci` passed from PowerShell (1,483 non-live tests, 74 MCP smoke tests; line coverage App 95.56%, Archidekt 92.26%, Core 99.82%, Decks 94.63%, OnCurve 98.48%, Playgroup 96.71%, Scryfall 95.82%, Spellbook 95.45%, Statistics 96.38%); `task test:integration` passed (24, 41, 151, 35, 99, and 62 tests); full E2E 97 passed, 6 live skipped, 0 failed; `task coverage:oncurve` passed (98.48% line, 97.33% branch); `task benchmark:oncurve` ran 10,000 samples in 330.9 ms (the removed estimator took 481.0 ms for the same input at 9d279de); `task pack VERSION=0.12.0` and `task release:tool-smoke VERSION=0.12.0` passed (74/74). Two coverage runs had E2E handshake failures that passed on rerun; the cause was CPU load from a slow test (the replay test built 16 card-data installs), which was fixed. The T-row rerun (T16, T17, T21, T29, T36) is deferred with EVAL-002: Not run (deferred). Phase 6 is not marked complete; tagging and publishing 0.12.0 are held for the owner. |

## Agent Task Results

Filled in by EVAL-002. The baseline is deferred to the owner because no agent-eval harness is available in the implementation session. Held-out tasks are reported in their own rows.

| Run | Build | Tasks | Correct (median of 3) | Tool calls | Tool-result tokens | Invalid-parameter errors |
| --- | --- | --- | --- | --- | --- | --- |
| Baseline | `0.10.0` | Tuning tasks | Not run | | | |
| Phase 6 rerun | `0.12.0` | T16, T17, T21, T29, T36 | Not run (deferred) | | | |
| Baseline | `0.10.0` | Held-out tasks | Not run | | | |
| Phase 4 rerun | phase 4 build | T11, T18 to T20, T22 to T28, T30 to T35, T37 | Not run (deferred) | | | |

## Result Size Measurements

Phase 1 (SIZE-001), measured once on 2026-10-27 by a live run against real
Scryfall data and a local deck. The one-off test was then deleted, so these
numbers are a record, not a repeatable check. Sizes are UTF-8 bytes of the
structured result, which equal characters for this ASCII JSON.

| Tool and request | Size | Over 40,000? |
| --- | --- | --- |
| `scryfall_search`, default `standard` format, 25 rows (`set:dmu t:creature`) | 123,956 | Yes, about 31,000 tokens, over Claude Code's default 25,000-token output cap |
| `deck_get`, 100 entries, no `include` | 16,033 | No |
| Phase 2 (fixture, not live): `scryfall_search`, 25 recorded-shape cards, `standard` | 135,567 | Yes (reported, not truncated) |
| Phase 2 (fixture): `scryfall_card_collection`, 25 rows, `standard` | 149,612 | Yes (reported, not truncated) |
| Phase 2 (fixture): `scryfall_cards_by_tag`, 25 cards, `standard` | 157,303 | Yes (reported, not truncated) |
| Phase 2 (fixture): `scryfall_search`, 25 cards, `concise` | 22,082 | No |
| Phase 2 (fixture): `scryfall_search`, `concise`, `pageSize` 100 | 37,841 (43 cards, then `truncated` and `nextCursor`) | No |
| Phase 2 (fixture): `deck_get`, 100 entries, `include` cards and tags, `pageSize` 100 | 37,029 (40 entries, then `truncated` and `nextCursor`) | No |

Not measured in phase 1: tools that need Archidekt or Playgroup credentials,
and `scryfall_cards_by_tag`, which needs installed card data. Phase 2 added
`concise`, paging, and size cuts to the card tools and `deck_get`; its rows are
fixture sizes (no live Scryfall run). The `deck_get` row, and the page checks in
the App tests, use the server's own serializer options; the Scryfall test rows
use a local serializer set up like it. Archidekt and Playgroup tools are
unchanged and still unmeasured.

The `scryfall_search` result is today's default output, which the owner chose
to keep (2026-10-25). Under SIZE-001 it is reported, not truncated, and its
description must recommend `concise`. Because it exceeds what Claude Code
returns to the model by default, the owner should confirm whether the default
page size or format should change in phase 2.

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
