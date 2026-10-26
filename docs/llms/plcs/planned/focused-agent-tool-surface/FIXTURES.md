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

### decks (29 tools today, 19 after phase 3, 18 after phase 6)

| Today | After |
| --- | --- |
| `deck_list`, `deck_get`, `deck_validate` (R) | unchanged; `deck_get` gains `include`; `deck_validate` gains `rules` |
| `deck_export_bundle`, `deck_interchange_formats`, `deck_backup_list`, `deck_import_preview`, `deck_identity_reconcile_preview` (R) | unchanged |
| `deck_category_rules_preview` (R) | unchanged, now includes validation; `common-v1` preset removed |
| `deck_category_rules_validate` (R) | removed; use `deck_category_rules_preview` |
| `deck_on_curve_estimate` (R) | unchanged until phase 6, then removed; use `stats_simulate` preset `on-curve` |
| `deck_create`, `deck_apply_changes`, `deck_backup_create`, `deck_import_create` (LW) | unchanged |
| `deck_delete`, `deck_backup_restore`, `deck_backup_delete`, `deck_category_rules_apply`, `deck_identity_reconcile_apply` (LW, D) | unchanged |
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

### stats (8 today, 9 after phase 6)

All eight `stats_*` tools stay, with new descriptions. Phase 6 adds
`stats_simulate`.

### archidekt (23 today, 21 after)

| Today | After |
| --- | --- |
| `archidekt_folder_list`, `archidekt_folder_get` (R) | `archidekt_folders_get` |
| `archidekt_snapshot_list`, `archidekt_snapshot_get` (R) | `archidekt_snapshots_get` |
| All other Archidekt tools | unchanged; conflict results gain current state |

### playgroup (16, frozen)

No changes in this packet.

### spellbook (3)

Unchanged; now on by default.

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
| `stats_deck_summary` | `stats_local_deck_summary` |
| `scryfall_corpus_status`, `scryfall_corpus_sync` | `scryfall_card_data_status`, `scryfall_card_data_sync` |
| `scryfall_corpus_rollback`, `scryfall_corpus_delete` | `scryfall_card_data_rollback`, `scryfall_card_data_delete` |
| `scryfall_tag_search` | `scryfall_tags_search` |
| `scryfall_cards_by_tag` | `scryfall_installed_cards_by_tag` |
| `scryfall_snapshot_delete` | `scryfall_search_snapshot_delete` |

## Count Check

The six toolsets are the same as today; only their tools change.

| Toolset | read | local write | remote write |
| --- | --- | --- | --- |
| `scryfall` after phase 3 | 9 | 4 | 0 |
| `scryfall` after phase 4 | 11 | 4 | 0 |
| `decks` after phase 3 | 10 | 9 | 0 |
| `decks` after phase 6 | 9 | 9 | 0 |
| `stats` | 8, then 9 after phase 6 | 0 | 0 |
| `spellbook` | 3 | 0 | 0 |
| `archidekt` | 9 | 1 | 11 |
| `playgroup` | 14 | 0 | 2 |

| Selection (read-only / local / remote) | After phase 3 | After phase 4 or 6 |
| --- | --- | --- |
| Default, no credentials | 30 / 43 / 43 | 32 / 45 / 45 |
| Default, Archidekt credentials only | 39 / 53 / 64 | 41 / 55 / 66 |
| Default, both credentials (same as `all`) | 53 / 67 / 80 | 55 / 69 / 82 |

The default mode is `remote`, so a default start shows the last column: 45
tools without credentials and 82 with both.

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

## Deck Rule Cases

| Case | Input | Expected |
| --- | --- | --- |
| R01 | 100-card deck, one commander, all in identity, all singletons, nothing banned. | Every fact `passed: true`. |
| R02 | 99-card deck. | Size fact fails with the count. |
| R03 | Main entry with a hybrid symbol outside the commander's identity. | Identity fact fails naming the entry. |
| R04 | Two copies of a nonbasic; 20 Forests; 10 Relentless Rats. | Singleton fact fails only for the nonbasic. |
| R05 | A banned card. | Legality fact fails naming it. |
| R06 | Four Game Changers. | Count 4 with the list; no `passed` flag and no bracket. |
| R07 | Two commanders. | Both listed with their text; no pairing verdict. |
| R08 | Card data not installed. | `not-cached` naming `scryfall_corpus_sync`. |
| R09 | One entry whose installed data lacks `game_changer`. | Game Changer fact has `status: "unknown"` naming that entry; the count covers the other entries. |
