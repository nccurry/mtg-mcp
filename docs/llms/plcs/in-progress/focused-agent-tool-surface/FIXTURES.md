# Fixtures: Focused Agent Tool Surface

## Old-to-New Tool Map

Annotation key: R = read-only, LW = local write, RW = remote write,
D = destructive.

**Names in this file.** Every table, task, and case uses today's working
names (for example `deck_get`, `scryfall_corpus_sync`). Phase 3 applies the
[final names](#final-tool-names). Tests written in phase 3 or later use the
final names, including in expected error messages: a case that says
"naming `scryfall_corpus_sync`" means the message names
`scryfall_card_data_sync` once phase 3 has landed.

### decks (29 tools today, 19 after phase 3, 20 after phase 4, 19 after phase 6)

| Today | After |
| --- | --- |
| `deck_list`, `deck_get`, `deck_validate` (R) | unchanged; `deck_get` gains `include`; `deck_validate` gains `rules` |
| `deck_export_bundle`, `deck_interchange_formats`, `deck_backup_list`, `deck_import_preview`, `deck_identity_reconcile_preview` (R) | unchanged |
| `deck_category_rules_preview` (R) | unchanged, now includes validation; `common-v1` preset removed |
| `deck_category_rules_validate` (R) | removed; use `deck_category_rules_preview` |
| `deck_on_curve_estimate` (R) | unchanged until phase 6, then removed; use `stats_simulate` preset `on-curve` |
| `deck_create`, `deck_apply_changes`, `deck_backup_create`, `deck_import_create` (LW) | unchanged |
| `deck_delete`, `deck_backup_restore`, `deck_backup_delete`, `deck_category_rules_apply`, `deck_identity_reconcile_apply` (LW, D) | unchanged |
| — (new in phase 4) | `deck_duplicates_preview` (R) |
| `deck_update` | `deck_apply_changes` with `update-metadata` |
| `deck_entry_add`, `deck_entry_update`, `deck_entry_remove` | `deck_apply_changes` with `add-entry`, `update-entry`, `remove-entry` |
| `deck_category_create`, `deck_category_update`, `deck_category_delete` | `deck_apply_changes` with `add-category`, `update-category`, `remove-category` |
| `deck_category_assign`, `deck_category_unassign` | `deck_apply_changes` with `assign-category`, `unassign-category` |

### scryfall (18 tools today, 13 after phase 3, 15 after phase 4)

| Today | After |
| --- | --- |
| `scryfall_search`, `scryfall_autocomplete`, `scryfall_tag_search`, `scryfall_cards_by_tag` (R) | unchanged; new descriptions |
| `scryfall_card_get`, `scryfall_card_collection` (R) | `scryfall_cards_get` |
| `scryfall_card_prints`, `scryfall_card_rulings` (R) | `scryfall_card_details` |
| `scryfall_sets`, `scryfall_catalog`, `scryfall_bulk_metadata` (R) | `scryfall_reference_get` |
| `scryfall_snapshot_list`, `scryfall_snapshot_get` (R) | `scryfall_search_snapshots_get` |
| `scryfall_corpus_status` (R), `scryfall_corpus_sync` (LW) | unchanged |
| `scryfall_corpus_rollback`, `scryfall_corpus_delete`, `scryfall_snapshot_delete` (LW, D) | unchanged |
| — (new in phase 4) | `card_lists_compare` (R, OpenWorld) |
| — (new in phase 4) | `card_list_query` (R, OpenWorld) |

### stats (8 today, 9 after phase 4, 10 after phase 6)

All eight `stats_*` tools stay, with new descriptions. Phase 4 adds
`stats_castability` (R). Phase 6 adds `stats_simulate` (R).

### archidekt (23 today, 21 after)

| Today | After |
| --- | --- |
| `archidekt_folder_list`, `archidekt_folder_get` (R) | `archidekt_folders_get` |
| `archidekt_snapshot_list`, `archidekt_snapshot_get` (R) | `archidekt_snapshots_get` |
| All other Archidekt tools | unchanged; conflict results gain current state |

### playgroup (16 today, 17 after phase 4)

The 16 existing tools are frozen. Phase 4 adds `playgroup_games_stats_get`
(R, OpenWorld).

### spellbook (3 today, 4 after phase 4)

The 3 existing tools are unchanged and now on by default. Phase 4 adds
`spellbook_deck_bracket_facts_get` (R, OpenWorld).

## Final Tool Names

Phase 3 applies these renames ([naming rule](SADD.md#tool-names)). Tools not
listed keep their working name.

| Working name | Final name |
| --- | --- |
| `deck_list` | `local_deck_list` |
| `deck_get` | `local_deck_get` |
| `deck_validate` | `local_deck_validate` |
| `deck_export_bundle` | `local_deck_export_bundle` |
| `deck_interchange_formats` | `local_deck_interchange_formats` |
| `deck_backup_list`, `deck_backup_create`, `deck_backup_restore`, `deck_backup_delete` | `local_deck_backup_list`, `local_deck_backup_create`, `local_deck_backup_restore`, `local_deck_backup_delete` |
| `deck_import_preview`, `deck_import_create` | `local_deck_import_preview`, `local_deck_import_create` |
| `deck_category_rules_preview`, `deck_category_rules_apply` | `local_deck_category_rules_preview`, `local_deck_category_rules_apply` |
| `deck_identity_reconcile_preview`, `deck_identity_reconcile_apply` | `local_deck_identity_reconcile_preview`, `local_deck_identity_reconcile_apply` |
| `deck_create`, `deck_apply_changes`, `deck_delete` | `local_deck_create`, `local_deck_apply_changes`, `local_deck_delete` |
| `deck_on_curve_estimate` | removed in phase 6; until then `local_deck_on_curve_estimate` |
| `deck_duplicates_preview` | `local_deck_duplicates_preview` |
| `stats_deck_summary` | `stats_local_deck_summary` |
| `scryfall_corpus_status`, `scryfall_corpus_sync` | `scryfall_card_data_status`, `scryfall_card_data_sync` |
| `scryfall_corpus_rollback`, `scryfall_corpus_delete` | `scryfall_card_data_rollback`, `scryfall_card_data_delete` |
| `scryfall_tag_search` | `scryfall_tags_search` |
| `scryfall_cards_by_tag` | `scryfall_installed_cards_by_tag` |
| `scryfall_snapshot_delete` | `scryfall_search_snapshot_delete` |

**Naming exceptions (NAME-001).** These final names keep the source prefix
but not the full `<source>_<object>_<action>` shape: `scryfall_search`,
`scryfall_autocomplete`, `card_lists_compare`, `card_list_query`, the eight
existing `stats_*` names other than `stats_local_deck_summary`,
`stats_castability`, and `stats_simulate`. The architecture test reads this
list.

## Count Check

The six toolsets are the same as today; only their tools change.

| Toolset | read | local write | remote write |
| --- | --- | --- | --- |
| `scryfall` after phase 3 | 9 | 4 | 0 |
| `scryfall` after phase 4 | 11 | 4 | 0 |
| `decks` after phase 3 | 10 | 9 | 0 |
| `decks` after phase 4 | 11 | 9 | 0 |
| `decks` after phase 6 | 10 | 9 | 0 |
| `stats` | 8, then 9 after phase 4, then 10 after phase 6 | 0 | 0 |
| `spellbook` | 3, then 4 after phase 4 | 0 | 0 |
| `archidekt` | 9 | 1 | 11 |
| `playgroup` | 14, then 15 after phase 4 | 0 | 2 |

| Selection (read-only / local / remote) | After phase 3 | After phase 4 or 6 |
| --- | --- | --- |
| Default, no credentials | 30 / 43 / 43 | 35 / 48 / 48 |
| Default, Archidekt credentials only | 39 / 53 / 64 | 44 / 58 / 69 |
| Default, both credentials (same as `all`) | 53 / 67 / 80 | 59 / 73 / 86 |

The default mode is `remote`, so a default start shows the last column: 48
tools without credentials and 86 with both. Phase 6 adds `stats_simulate`
and removes `deck_on_curve_estimate`, so its counts equal phase 4's.

Today, for comparison: the default selection is `decks`, `scryfall`, and
`stats` in `local` mode (55 tools), and `all` is 61 / 84 / 97.

## Agent Task Set

Each task has an answer that can be checked from fixtures or a fixed deck.
Archidekt tasks run against a fake Archidekt server, never the real service.
Run each task three times per build and record the median.

| ID | Task | Check |
| --- | --- | --- |
| T01 | Find all green sorceries with mana value 2 that search for a land and are legal in Commander. | Name set matches the fixture query result. |
| T02 | Find every card tagged as ramp in Scryfall's oracle tags with mana value 3 or less in Golgari colors. | Name set matches fixture for `otag:ramp mv<=3 id<=bg`. |
| T03 | List every card in deck D1 with its oracle tags, grouped by tag. | Matches `deck_get` with `include: ["tags"]`. |
| T04 | What is the chance of at least one of 10 ramp cards in an opening 7 from 99? | Matches exact hypergeometric value to 6 places. |
| T05 | Turn-by-turn chance of having 4 lands by turn 4 on the draw for D1. | Matches `stats_turn_table`. |
| T06 | Get rulings for "Dockside Extortionist". | Ruling count and dates match fixture. |
| T07 | Find the cheapest printing of "Sol Ring" with a USD price. | Matches fixture prints. |
| T08 | Add 3 named cards to deck D2 and put them in category "Draw". | Final deck state matches expected revision content. |
| T09 | Remove all cards in category "Cut" from D2. | Final state matches. |
| T10 | Export D1 for Moxfield. | Bundle equals fixture. |
| T11 | Which cards in D1 are not legal in Commander? | Before phase 4: matches the `legalities` field in `deck_get` card views (today's `deck_validate` checks structure only). From phase 4: matches the legality fact of `deck_validate` with `rules: "commander"`. |
| T12 | Find 20 black instant-speed removal spells under mana value 3 and return only names and costs. | Name set matches; token use recorded. |
| T13 | Build a new deck from a commander plus 10 searched cards and upload it to Archidekt as a private deck. | Fake server receives the expected deck contents. |
| T14 | Download Archidekt deck A1, replace 2 named cards, and upload the change. | Fake server ends with the expected contents; local revision matches. |
| T15 | Upload a change to A1 after the fake server changed it remotely. | Agent sees `conflict` with current state and repeats the preview once. |
| T16 | Chance of casting D1's commander on turn 4 using lands plus 2-mana ramp (phase 6 only). | Matches fixed-seed `stats_simulate` result. |
| T17 | Same question as T16 with a London mulligan to 6 if fewer than 2 lands (phase 6 only). | Matches fixed-seed result. |
| T18 | Find cards with oracle tag "multiplayer" that are not already in Archidekt deck A1 (phase 4 only). | Matches `card_lists_compare` `difference` over fixtures; no card list appears in a tool request. |
| T19 | List cards that appear in both decks D1 and D2 (phase 4 only). | Matches fixture intersection. |
| T20 | Find red removal or burn cards under mana value 2, combining two tag searches, minus cards in D1 (phase 4 only). | Matches nested `compare` fixture. |
| T21 | How often does D1 miss a land drop by turn 4 when it plays 3 cheap card-draw spells as soon as it can (phase 6 only)? | Matches fixed-seed result using `draw-cards`. |
| T22 | Show D1's mana curve split by creature and non-creature (phase 4 only). | Matches `card_list_query` grouped by `manaValue` and `cardTypes`. |
| T23 | Count D1's cards by oracle tag for ramp, draw, removal, and board wipe, and list the cards in each (phase 4 only). | Matches grouped query with rows. |
| T24 | Compare D1's green pips with its green sources (phase 4 only). | Matches `pips` and `sources` metrics. |
| T25 | Total D1's price, and list the 10 most expensive cards with their cheapest printing price (phase 4 only). | Matches `sum:priceUsd` and sorted rows. |
| T26 | Check D1 against Commander rules and list any problems (phase 4 only). | Matches `deck_validate` rule facts fixture. |
| T27 | Find 5 cards that share at least one oracle tag with "Cultivate", are in D1's color identity, and are not in D1 (phase 4 only). | Matches tag lookup, search, and `card_lists_compare` fixtures. |
| T28 | What changed in D1 since its last backup (phase 4 only)? | Matches `card_lists_compare` `symmetric-difference` with a `deck-backup` source. |
| T29 | Show 3 example opening hands for D1 with a free first mulligan (phase 6 only). | Hands match `sampleHands` for the fixed seed. |
| T30 | For D1, which spells are least likely to be castable on curve with its lands as sources (phase 4 only)? | Matches `stats_castability` rows for the fixture source groups. |
| T31 | Is D3's commander pair legal (phase 4 only)? | Matches the `pairing` fact for the fixture partners. |
| T32 | How many Game Changers, mass land denial cards, extra-turn cards, and two-card combos does D1 have (phase 4 only)? | Matches the Spellbook bracket facts fixture; no bracket is named. |
| T33 | Which deck has the highest win rate in playgroup G1 over the last 90 days, with game counts (phase 4 only)? | Matches `playgroup_games_stats_get` fixture rows; the answer cites game counts. |
| T34 | How many lands in D1 say they enter tapped (phase 4 only)? | Matches `card_list_query` with `oracleText` contains "enters tapped" and `cardTypes` contains `Land`. |
| T35 | Which cards in D1 are not available on MTG Arena, and what is D1's total Cardmarket price (phase 4 only)? | Matches `games` filter and `sum:priceEur`. |
| T36 | Chance of casting D1's commander (`{2}{B}{G}`) on turn 4 with its lands' real colors (phase 6 only). | Matches fixed-seed `stats_simulate` with `produces` and `can-cast`. |
| T37 | Archidekt deck A2 has cards in both playing categories and Maybeboard. Keep one copy of each card, preferring Commander, then the playing categories, then Sideboard, then Maybeboard, and upload the result (phase 4 only). | Fake server ends with one copy per card; the agent called `deck_duplicates_preview`, `deck_apply_changes`, and the push pair, and resolved any `ambiguous` card by asking again. |

Held-out tasks (EVAL-003): T04, T06, T07, T10, and T12, which all run on the
`0.10.0` baseline, plus T27 and T33 from phase 4. They are never used while
rewriting descriptions; their results are reported in their own rows.

Record per run: correct (yes/no), tool calls, total tool-result tokens, and
invalid-parameter errors.

## Simulation Cases

| Case | Input | Expected |
| --- | --- | --- |
| S01 | Fixed deck of 40 lands and 59 other cards, `play` lands with `maxPerTurn` 1, 4 turns, seed `0000000000000001`. | Same hits on every run and platform. |
| S02 | S01 with a condition on an unknown group. | `invalid-simulation-group`. |
| S03 | Samples, turns, and deck size above the work limit. | `simulation-too-large` with the limit in the message. |
| S04 | A card listed in two groups. | `overlapping-groups` naming the entry. |
| S05 | Replay of each saved `deck_on_curve_estimate` fixture through the `on-curve` preset. | Identical hit counts and seed. |
| S06 | `keepWhen` never true, `maxMulligans` 1. | Every sample is counted in `keptAtLimit`; one card is bottomed per sample. |
| S07 | Two `cast-if-able` rules both affordable; listed order A then B with mana for only one. | A always fires; B never fires that turn. |
| S08 | `maxPerTurn` 2 on a `play` rule with 3 qualifying cards. | Exactly 2 move per turn. |
| S09 | `manaFrom: "next-turn"` ramp cast on turn 2. | Its mana counts on turn 3, not turn 2. |
| S10 | `pick: "listed-order"` on an `entry-ids` group. | The first listed card in hand always moves first. |
| S11 | `pick: "listed-order"` on a `category-ids` group. | `invalid-input`. |
| S12 | Mulligan to 6 with `preferGroups: ["ramp2"]` and one ramp card in hand. | The ramp card is bottomed. |
| S13 | Request missing `maxPerTurn`. | `invalid-input` naming the field. |
| S14 | `draw-cards` with `effect: "once"`, `cards` 2, listed before a `play` lands rule; no land in hand before the draw. | A land drawn by the rule is played the same turn. |
| S15 | Same as S14 with the `play` rule listed before `draw-cards`. | A land drawn by the rule is not played until the next turn. |
| S16 | `draw-cards` with `effect: "each-turn"`, `cards` 1, cast on turn 2. | From turn 3 on, two cards are drawn at each draw step. |
| S17 | Deck small enough that `each-turn` draws empty the library before the last turn. | Drawing stops; the sample is counted in `libraryEmpty`. |
| S18 | `freeFirstMulligan: true`, one mulligan taken. | Kept hand has 7 cards; nothing bottomed. With `false`, 1 card bottomed. |
| S19 | `sampleHands` 3 with a fixed seed; rerun with `sampleHands` 0. | The 3 returned samples match the first 3 counted samples; condition hits are identical in both runs. |
| S20 | Lands group with `produces.default: ["G"]`; `can-cast` `{G}{G}` on turn 2 with two Forests in play. | Hit. With one Forest and one generic-only source, miss. |
| S21 | `byEntry` gives one dual `["B","G"]`; `can-cast` `{B}{G}` with a Forest and the dual in play. | Hit: the dual pays B, the Forest pays G. |
| S22 | `can-cast` `{W/U}` with one Island in play. | Hit (hybrid accepts either color). |
| S23 | `can-cast` `{G/P}` with no green source. | Miss; `limits` lists Phyrexian life payment as not modeled. |
| S24 | Replay of a saved `deck_on_curve_estimate` fixture whose target has a colored cost. | Identical hit counts through the `on-curve` preset. |
| S25 | `play` with `pick: "target-first"` and `targetCost` `{1}{U}{U}`; hand holds a Forest, an Island, and a U/G dual whose rule is `next-turn`. | The Island is played (it covers a colored symbol now); with two Islands already in play, ties fall to same-turn, then lowest entry ID, as `target-first-v1` does. |
| S26 | `cast-if-able` spends 2 generic from a Forest and an Island; then `can-cast` `{G}` the same turn with a third source that makes only U. | Hit only if one joint assignment pays both: the Forest pays `{G}` and the other two pay the 2 generic. |

## Card Set Cases

| Case | Input | Expected |
| --- | --- | --- |
| C01 | Left {A, B, C}, right {B, C, D}, each operation, `matchBy: "oracle"`. | union {A, B, C, D}; intersection {B, C}; difference {A}; symmetric difference {A, D}. |
| C02 | Left has printing P1 of card X; right has printing P2 of X. | `oracle`: X matches; `printing`: X does not match. |
| C03 | Right is a deck with one entry that has no Scryfall identity. | Entry listed in `unmatched` with `no-scryfall-identity`; counts include it under `unmatched`. |
| C04 | Nested `compare` 4 levels deep. | `invalid-input` stating the 3-level limit. |
| C05 | `archidekt-deck` source with no Archidekt credentials. | `invalid-input` with reason `credentials-missing`. |
| C06 | Fake Archidekt deck changes between page 1 and page 2. | Page 2 returns `conflict` with the new state marker. |
| C07 | Local deck source with a stale `expectedRevision`. | `conflict` with the current revision. |
| C08 | Same request twice over fixtures. | Identical pages and fingerprint. |
| C09 | `deck-backup` source for a deck that changed after the backup. | Backup content is used; the live database is unchanged (checked by fingerprint). |
| C10 | `archidekt-snapshot` source from the fake server. | Snapshot content is used; no state marker is needed. |
| C11 | `deck` source with `--toolsets scryfall` (credentials present). | `invalid-input` with reason `toolset-not-selected`. |
| C12 | Page 2 requested with a cursor from a different request. | `invalid-input`; no data returned. |
| C13 | Nested request with 301 `cards` IDs in total. | `invalid-input` stating the 300 limit. |
| C14 | Backup made with an older `decks.db` schema version. | `unsupported` with `backup-schema-mismatch` naming both versions; the backup file is unchanged. |
| C15 | `deck-backup` source for a deck that is not in the backup. | `not-found` with `deck-not-in-backup`. |
| C16 | `deck` source without `expectedRevision`. | Current revision used and echoed in `sources`; deck change between pages returns `conflict`. |

## Card List Query Cases

| Case | Input | Expected |
| --- | --- | --- |
| Q01 | Deck fixture grouped by `manaValue` with `quantity`. | One group per mana value; totals equal deck size. |
| Q02 | Group by `colors` with a two-color card. | The card counts in both color groups; `multiValued: true`. |
| Q03 | Group by `tags` with an untagged card. | The card is in the `none` group. |
| Q04 | `pips` over `{1}{G}{G}`, `{W/U}`, `{G/P}`, `{X}{R}`. | G 3 (2 plus the Phyrexian symbol), W 1, U 1, R 1; `hybrid` 1; `phyrexian` 1; X not counted. |
| Q05 | `sources` over Forest, a G/U dual, a five-color land, a fetch land, and a green mana rock. | G 4, U 2, `anyColor` 1, `noProducedMana` 1. |
| Q06 | `sum:priceUsd` with one card missing a price. | Sum excludes it; `missingValues` reports 1. |
| Q07 | `where` with each operator. | Matching cards only. |
| Q08 | Sorted rows by `priceUsd` desc, paged. | Stable order across pages, ties broken by name then Oracle ID. |
| Q09 | Card data not installed. | `not-cached` naming `scryfall_corpus_sync`. |
| Q10 | Unknown field in `groupBy`. | `invalid-input` listing allowed fields. |
| Q11 | `where` `oracleText` contains "ENTERS TAPPED" over a deck with a tapped dual and an MDFC whose back face says it. | Both match (case-insensitive, every face). `oracleText` in `groupBy` returns `invalid-input`. |
| Q12 | `where` `legalIn` contains `oathbreaker`; `bannedIn` contains `commander`. | Only cards with those Scryfall legality values. |
| Q13 | `where` `games` contains `arena`; group by `reserved`. | Correct groups; cards without `games` data counted in `missingValues`. |
| Q14 | `sum:priceEur` and `sum:priceTix` with missing values. | Exact decimal sums; `missingValues` per field. |

## Deck Rule Cases

| Case | Input | Expected |
| --- | --- | --- |
| R01 | 100-card deck, one commander, all in identity, all singletons, nothing banned. | Every fact `passed: true`. |
| R02 | 99-card deck. | Size fact fails with the count. |
| R03 | Main entry with a hybrid symbol outside the commander's identity. | Identity fact fails naming the entry. |
| R04 | Two copies of a nonbasic; 20 Forests; 10 Relentless Rats. | Singleton fact fails only for the nonbasic. |
| R05 | A banned card. | Legality fact fails naming it. |
| R06 | Four Game Changers. | Count 4 with the list; no `passed` flag and no bracket. |
| R07 | Two commanders. | Both listed with their text and a `pairing` fact (cases R10 to R15 cover the rules). |
| R08 | Card data not installed. | `not-cached` naming `scryfall_corpus_sync`. |
| R09 | One entry whose installed data lacks `game_changer`. | Game Changer fact has `status: "unknown"` naming that entry; the count covers the other entries. |
| R10 | Two commanders with plain Partner. | `pairing` passed. |
| R11 | A "Partner with" commander and the card it names; then the same card with a different Partner commander. | First passes; second fails naming the required partner. |
| R12 | Two Friends forever commanders; a Friends forever commander with a plain Partner. | First passes; second fails. |
| R13 | A Choose a Background commander and a Background; the same commander with a non-Background enchantment. | First passes; second fails. |
| R14 | A Doctor's companion commander and a Time Lord Doctor. | `pairing` passed. |
| R15 | A commander whose keyword is a Partner variant the table does not list. | `status: "unknown"` with the keyword; no pass or fail. |
| R16 | Lurrus in a `companion` zone; main deck has a 3-mana-value permanent. | `companion` fails naming the entry. |
| R17 | Jegantha in a `companion` zone; no card repeats a mana symbol. | `companion` passed. |
| R18 | Zirda in a `companion` zone. | `status: "not-checked"` with the restriction text and reason. |
| R19 | A Choose a Background commander and a legendary Background in the `commander` zone. | Commander eligibility passes for both; `pairing` passed. |
| R20 | Seven copies of Seven Dwarves; then eight. | Singleton passes with seven and fails with eight, naming the card and its limit of 7. |
| R21 | Lutri in a `companion` zone with Relentless Rats in the deck; then Kaheera with a Changeling creature; then a companion outside the commanders' identity. | Lutri fails naming Relentless Rats; Kaheera passes; the identity check fails naming the companion. |

## Input Binding Cases

| Case | Input | Expected |
| --- | --- | --- |
| X01 | `deck_apply_changes` with one change `{"entry":{...},"kind":"update-entry"}` (`kind` last). | Same result as the same change with `kind` first. |
| X02 | A change with `"kind":"rename-entry"` (not a union value). | `invalid-input`, reason `invalid-arguments`, path `changes[0].kind`, message listing allowed kinds; `isError: true`. |
| X03 | A change whose `quantity` is a string. | `invalid-input` with path `changes[0].entry.quantity`; never "An error occurred invoking". |

## Archidekt Category Diff Cases

| Case | Input | Expected |
| --- | --- | --- |
| A01 | Remote entry categories `["Maybeboard","Sideboard","Removal"]`, local `["Maybeboard","Removal","Sideboard"]`, same primary. | No entry operation planned. |
| A02 | Same set, different primary category. | One entry update planned. |
| A03 | Fake server: pull a deck, apply a no-op local edit, then push preview. | Zero entry operations; predicted provider requests cover only deck-level checks. |
| A04 | Fake server: pull, add category "Protection" and two entries locally, push apply, add 10 unrelated sideboard entries, push preview. | Exactly the 10 sideboard entry-adds; no remove or re-add of the two pushed entries; no category change. |
| A05 | After A04's first push, read the local deck's binding baseline. | The two entries and the category map to the provider IDs the fake server assigned. |
| A06 | Fake server returns a created row that matches no planned create exactly. | Push apply reports it in `unlinked`; the local row stays unbound; nothing is guessed. |
| A07 | After A06, run push preview. | `conflict` with reason `unlinked-remote-rows` naming the row and telling the agent to pull; no create is planned again. |

## Deck Duplicate Cases

| Case | Input | Expected |
| --- | --- | --- |
| D01 | Swords to Plowshares as one entry in `main` with category Removal and one with category Maybeboard; `keepPriority` Removal, then Maybeboard; `matchBy: oracle`. | Removal entry kept; one `remove-entry` for the Maybeboard entry. |
| D02 | Two printings of Sol Ring in two entries. | Duplicate under `oracle` and `name`; not a duplicate under `printing`. |
| D03 | Two copies of a card, both in category Ramp, in separate entries. | Card in `ambiguous` with both entries; no change planned. |
| D04 | One copy in a listed group and one in an unlisted group. | Listed-group copy kept; the other removed. |
| D05 | One entry with quantity 3 and no other copies. | `update-entry` setting quantity 1. |
| D06 | 30 Forests and 2 Relentless Rats entries, with `exempt: commander-singleton-exceptions`, then with `none`. | Skipped with the exemption; reported without it. |
| D08 | Seven Dwarves in 8 entries with `exempt: commander-singleton-exceptions`; then `matchBy: oracle` with card data not installed. | Seven copies kept and one removed; then `not-cached` naming the sync tool. |
| D07 | Fake Archidekt deck pulled, `changes` applied with `deck_apply_changes`, then `archidekt_push_preview`. | Push preview has only the planned removals and quantity updates; no category-order updates (ARCH-001). |

## Castability Cases

| Case | Input | Expected |
| --- | --- | --- |
| K01 | 99-card raw deck, 40 sources all `["G"]`, one spell `{1}{G}{G}`, `turn: mana-value`, on the play, `requireLandDrops: false`. | One row at turn 3 whose exact probability equals `stats_mana_availability` with draw count 9 and `MaximumUsableSources` 3. |
| K02 | A 20-card deck small enough to enumerate, with two source groups and one cost, `requireLandDrops: true`, on the play and on the draw. | Exact conditional values equal brute-force enumeration of P(payable and at least T sources seen) / P(at least T sources seen). |
| K03 | Two spells sharing `{2}{B}`. | One row listing both cards. |
| K04 | A spell with no mana cost, a spell with `{X}{R}`, a spell with `{W/U}`, and a spell with `{G/P}`. | All four in `skipped` with reasons; no row. |
| K05 | Three spells with different probabilities. | Rows sorted by probability, then name; no `passed` field and no target. |
| K06 | Distinct costs times source groups above the work limit. | `invalid-input` stating the limit. |
| K07 | `scryfall-snapshot` source; then a `deck` source with card data not installed. | `invalid-input` naming the allowed source kinds; then `not-cached` naming the sync tool. |

## Spellbook Bracket Facts Cases

| Case | Input | Expected |
| --- | --- | --- |
| B01 | Fake `/estimate-bracket` response with a Game Changer, a mass land denial card, an extra-turn card, and one two-card combo. | Per-card flags and the combo's `definitelyTwoCard`, `speed`, and `manaNeeded` are returned. |
| B02 | The B01 response includes `bracketTag: "Spicy"`. | No result field contains the tag or its value. |
| B03 | Same request twice within the cache window. | Second call reports `cached`; no second HTTP request. |
| B04 | Fake server returns 503. | The existing Spellbook provider failure kind, with `isError: true`. |

## Playgroup Statistics Cases

| Case | Input | Expected |
| --- | --- | --- |
| P01 | 12 fixture games over 2 pages, `groupBy: deck`. | One row per deck with exact games, wins, win rate, and `baselineRate`. |
| P02 | Same games, `groupBy: player`; then `groupBy: commander`. | Correct totals per player, rows sorted by key; `commander` returns `invalid-input` listing `deck` and `player`. |
| P03 | `from` and `to` that include 5 of the 12 games, which arrive in no date order. | Rows total 5 games; `gamesOutsideRange` is 7. |
| P04 | `maxGames: 8` with 12 games, two of which have no winner, and a date range. | `gamesRead` 8, `limitReached` true, skipped games listed with `no-winner`, and a note that the range may be incomplete. |
| P05 | `from` after `to`. | `invalid-input`. |
