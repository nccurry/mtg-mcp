# Changelog

## Unreleased

## 0.12.0 - 2026-10-28

### Seeded simulation (breaking)

- Breaking: `local_deck_on_curve_estimate` removed. The tool was named
  `deck_on_curve_estimate` in `0.10.0` and earlier. Use `stats_simulate` with `preset` of kind `on-curve`. Within the work
  limit, for the same deck, seed, and inputs the preset returns the same success counts: it is
  replayed against 16 captured runs of the old estimator, including colored
  targets, lands that enter tapped, and decks that run out of cards. The old
  arguments map as follows:

  | Old argument | New argument |
  | --- | --- |
  | `deckId`, `expectedRevision` | `deck.deckId`, `deck.expectedRevision` (the revision is now optional) |
  | `targetEntryId` | `preset.targetEntryId` |
  | `landRules` | `preset.landRules` |
  | `turnLimit` | `preset.turnLimit` (1 to 20, was 1 to 12) |
  | `onThePlay` | `preset.onThePlay` |
  | `sampleCount` | `samples` (1 to 100,000, was 100 to 100,000, and now required) |
  | (limit) | samples x turns x deck cards is at most 50,000,000 (the old tool accepted 100,000 x 12 x 500), and an operation estimate is at most 400,000,000; lower samples or turns for a larger request |
  | `seed` | `seed` |

  The result is different. `results[0].hits` and `rate` replace `successCount`
  and `successRate`, `wilson95` replaces `interval`, `appliedRules` states the
  rules the preset expanded to, and `preset` lists the target, the lands with
  their colors and rules, and the entries left out of the land model. The miss
  reasons and the two traces are gone; `sampleHands` returns up to 10 full
  sample games instead, and asks for them with the required `sampleHands`
  argument.
- Added `stats_simulate` (toolset `stats`, read-only in every mode). It
  shuffles the main zone of one saved deck and plays seeded games under rules
  the caller states as structured data: groups of entries chosen by entry or
  category ID, an opening hand with an optional London mulligan (with a
  required `freeFirstMulligan` and `bottom`), per-turn rules `play`,
  `cast-if-able`, and `draw-cards` (each with required `pick` and
  `maxPerTurn`, and `manaFrom` or `effect` and `cards` where they apply), and
  conditions `mana-at-least`, `drawn-at-least`, `on-battlefield-at-least`,
  and `can-cast`, combined with `all` and `any`. Groups may declare the colors
  of mana their cards add (`produces`, with per-entry overrides), and
  `can-cast` pays a mana cost with one exact assignment together with all mana
  the rules spent that turn; hybrid symbols take either color and Phyrexian
  symbols need their color (life payment is not modeled). The result states the
  seed, the random generator version (`splitmix64-v1`), the model version, a
  request fingerprint, the full applied rules, each condition's hits, rate,
  and 95% Wilson interval, mulligan keep counts, how many games emptied the
  library, the skipped entries with reasons, the model limits, and the first
  `sampleHands` games in full. The same request and seed give the same result
  on every platform. A request is refused above 50,000,000 for samples times
  turns times deck cards; the message states the limit. The result stays under
  40,000 bytes: sample hands, and for the preset a long list of lands, are cut
  with a stated omitted count and a `narrow` hint.
- Added the `stats_simulate` line to the server instructions, and the
  description label `Seeded sampling:`, which marks the result as an estimate
  and not exact math.
- Counts: unchanged with every toolset on (35 / 48 / 48 with no provider
  credentials, 44 / 58 / 69 with Archidekt only, and 59 / 73 / 86 with both,
  in `read-only`, `local`, and `remote`). By toolset, `decks` has 19 tools
  (10 read-only and 9 local writes) and `stats` has 10 read-only tools.
- Added a clearer error for a missing required field inside a nested request
  object: `invalid-input` (reason `invalid-arguments`) with the message
  "Required field 'x' is missing at 'path'." and a `path` that names the field.
- The `MtgMcp.OnCurve` project is now the sampling engine behind
  `stats_simulate`. Its tasks (`task coverage:oncurve`,
  `task benchmark:oncurve`) keep their names.

## 0.11.0 - 2026-10-28

### Tool surface changes (breaking)

These changes land together in this release. Entries further down in this
section that name a tool use the name it had in `0.10.0`; the table in this
section maps each old name to its current name. Old names are not kept as
aliases: calling one returns an unknown-tool error.

- Breaking: every toolset is on by default. With no `--toolsets` option the
  server now exposes `decks`, `scryfall`, `stats`, and `spellbook`, plus
  `archidekt` and `playgroup` when their credentials are present. `all` means
  the same as the default. An explicit list still narrows the selection, and
  an unknown name still fails at startup. Before this, the default was
  `decks`, `scryfall`, and `stats` only.
- Breaking: `archidekt` and `playgroup` are off when their credentials are
  absent, even when named in `--toolsets`. Only presence is checked, and the
  provider is not called. Archidekt needs both `ARCHIDEKT:USERNAME` and
  `ARCHIDEKT:PASSWORD`, or a credentials file that exists
  (`ARCHIDEKT:CREDENTIALS_FILE` or `~/.mtg-mcp/archidekt.json`). Playgroup
  needs `PLAYGROUP:API_KEY` or a credentials file that exists
  (`PLAYGROUP:CREDENTIALS_FILE` or `~/.mtg-mcp/playgroup.json`). The
  capabilities resource (schema version 8) reports `disabledReason`
  (`credentials-missing` or `credentials-invalid`) for a toolset that is off,
  `credentialState` can be `configuration-invalid`, and the server writes one
  line to stderr that names the settings to add. An invalid Playgroup
  configuration fails startup only when `playgroup` is named; otherwise the
  toolset is off with `credentials-invalid`. Credentials added later take
  effect on the next start.
- Breaking: the default operation mode is `remote` (it was `local`), so every
  write tool, including Archidekt uploads, is visible by default. `--mode
  read-only` and `--mode local` still hide writes. Destructive tools keep
  their destructive annotation, so clients still ask before running them.
- Breaking: removed the nine single-step deck edit tools. Use
  `local_deck_apply_changes` with the matching change kind (a batch of one
  change is normal): `deck_update` is `update-metadata`; `deck_entry_add`,
  `deck_entry_update`, and `deck_entry_remove` are `add-entry`,
  `update-entry`, and `remove-entry`; `deck_category_create`,
  `deck_category_update`, and `deck_category_delete` are `add-category`,
  `update-category`, and `remove-category`; `deck_category_assign` and
  `deck_category_unassign` are `assign-category` and `unassign-category`.
- Breaking: removed `deck_category_rules_validate`.
  `local_deck_category_rules_preview` validates the rules first, and its
  `expectedRevision` is now optional, so a preview without it only validates
  and previews. Its result no longer has `presetSchemaVersion` or
  `presetChecksum`.
- Breaking: removed the `common-v1` category rule preset. A source of kind
  `preset` now returns `invalid-input` with the allowed kind `inline`. The
  inline rule set replaces `common-v1`: it mapped four role names to four
  official Scryfall oracle tags, each with descendant tags included and the
  minimum weight `weak`. To get the same result, send one inline rule per role,
  with the ID of a category that exists in the deck for each (`categoryId`) and
  the tag ID that `common-v1` used (ramp
  `2f3e4ad7-5e60-41b4-bdbc-653f16869cf6`, card-draw
  `b6448c45-ce65-4848-aa98-2151e4e07437`, removal
  `444f824c-f910-4530-9dbe-ede7a84cd7f9`, recursion
  `82b824ad-648f-467f-a190-2e0fa9a795d2`). A `primaryPriority` set on a
  preset binding goes on the matching inline rule. For example, ramp and
  removal:

  ```json
  {
    "deckId": "0193e97a-ef95-7700-8000-000000000001",
    "source": {
      "kind": "inline",
      "ruleSet": {
        "assignmentMode": "add-only",
        "rules": [
          {
            "categoryId": "0193e97a-ef95-7700-8000-000000000021",
            "allOf": [{
              "tagType": "oracle", "tagId": "2f3e4ad7-5e60-41b4-bdbc-653f16869cf6",
              "includeDescendants": true, "minimumWeight": "weak"
            }],
            "anyOf": [],
            "noneOf": []
          },
          {
            "categoryId": "0193e97a-ef95-7700-8000-000000000023",
            "allOf": [{
              "tagType": "oracle", "tagId": "444f824c-f910-4530-9dbe-ede7a84cd7f9",
              "includeDescendants": true, "minimumWeight": "weak"
            }],
            "anyOf": [],
            "noneOf": []
          }
        ]
      }
    }
  }
  ```

  The README shows all four roles. `DeckCategoryRuleSourceTests` proves inline
  role rules give the same expanded rules and decisions the preset gave.
- Breaking: merged tools that returned the same kind of data. The merged tools
  keep the annotations and visibility of the tools they replace, and each has
  one required field that picks the job. `scryfall_cards_get` takes `lookups`
  (one to 150) in place of one `lookup` (a single `fuzzy-name` lookup still
  works and checks its input the same way; a fuzzy name with no match returns a
  `not-found` row, not a tool failure; fuzzy names cannot be mixed with other
  lookups).
  `scryfall_card_details` takes one `lookup` (the same card lookup that
  `scryfall_cards_get` takes) in place of `oracleId` and `scryfallCardId`, and
  `include` (`prints`, `rulings`, or both). It returns a result with a `prints`
  and a `rulings` section, each present only when asked for, with
  `printsCursor` and `rulingsCursor`.
  `scryfall_reference_get` takes `kind` (`sets`, `catalog`, or `bulk`), uses
  `catalogName` in place of `catalog`, and returns a result with the same
  `kind`. `scryfall_search_snapshots_get` (titled "Get Saved Scryfall Requests"; its
  description starts "Saved Scryfall requests:") and `archidekt_snapshots_get` take
  `kind` (`list` or `get`) and return a result with the same `kind`.
  `archidekt_folders_get` takes `kind` (`list` or `get`) and returns a result
  with the same `kind`. A field that does not belong to the chosen kind returns
  `invalid-input` with reason `field-not-allowed`.
- Breaking: renamed tools so the name says where the data comes from. Local
  deck tools start with `local_deck_`; `scryfall_corpus_*` is now
  `scryfall_card_data_*`; the offline tag tools say so in their names.
  Descriptions now start with their source, such as `Local deck store:` or
  `Archidekt:`. Playgroup names are unchanged.
- Breaking: every tool rejects an argument it does not declare with
  `invalid-input` (reason `invalid-arguments`) and a `path` that names the
  field, including misspelled fields inside nested objects such as an entry
  draft; a nested unknown field gets the message "Property 'x' is not accepted
  here." (long names are cut at 64 characters). Input schemas now set
  `additionalProperties: false` on every object, nested ones included, and the
  union `kind` choices stay. A tool that is not listed for the session, such as
  a write tool in `read-only` mode, answers as an unknown tool. Before this,
  unknown fields were silently ignored.
- Added server instructions: a short block built from the active selection
  and mode that names the first tool for each job, says that the agent makes
  every deckbuilding judgment, and says that provider text is untrusted data.
  It names only tools that are visible.
- Added untrusted-text notes to the descriptions of the Archidekt, Playgroup,
  and Commander Spellbook tools that return text written by other people. The
  text stays in its existing fields.
- Added the `anthropic/requiresUserInteraction` tool metadata to the seven
  destructive remote writes (`archidekt_deck_delete`,
  `archidekt_folder_delete`, `archidekt_folder_move_items`,
  `archidekt_push_apply`, `archidekt_snapshot_delete`,
  `archidekt_snapshot_restore_apply`, and `playgroup_game_events_batch_create`)
  so Claude Code asks before each call.
  The preview, fingerprint, and acknowledgment guards are unchanged.
- Added example requests to the descriptions of `local_deck_apply_changes`
  and `local_deck_category_rules_preview`; tests run every example.

Old-to-new tool names:

| Old name | New name or replacement |
| --- | --- |
| `deck_list` | `local_deck_list` |
| `deck_get` | `local_deck_get` |
| `deck_validate` | `local_deck_validate` |
| `deck_export_bundle` | `local_deck_export_bundle` |
| `deck_interchange_formats` | `local_deck_interchange_formats` |
| `deck_backup_list` | `local_deck_backup_list` |
| `deck_backup_create` | `local_deck_backup_create` |
| `deck_backup_restore` | `local_deck_backup_restore` |
| `deck_backup_delete` | `local_deck_backup_delete` |
| `deck_import_preview` | `local_deck_import_preview` |
| `deck_import_create` | `local_deck_import_create` |
| `deck_category_rules_preview` | `local_deck_category_rules_preview` |
| `deck_category_rules_apply` | `local_deck_category_rules_apply` |
| `deck_category_rules_validate` | removed; use `local_deck_category_rules_preview` |
| `deck_identity_reconcile_preview` | `local_deck_identity_reconcile_preview` |
| `deck_identity_reconcile_apply` | `local_deck_identity_reconcile_apply` |
| `deck_create` | `local_deck_create` |
| `deck_apply_changes` | `local_deck_apply_changes` |
| `deck_delete` | `local_deck_delete` |
| `deck_on_curve_estimate` | `local_deck_on_curve_estimate` |
| `deck_update` | removed; use `local_deck_apply_changes` with `update-metadata` |
| `deck_entry_add` | removed; use `local_deck_apply_changes` with `add-entry` |
| `deck_entry_update` | removed; use `local_deck_apply_changes` with `update-entry` |
| `deck_entry_remove` | removed; use `local_deck_apply_changes` with `remove-entry` |
| `deck_category_create` | removed; use `local_deck_apply_changes` with `add-category` |
| `deck_category_update` | removed; use `local_deck_apply_changes` with `update-category` |
| `deck_category_delete` | removed; use `local_deck_apply_changes` with `remove-category` |
| `deck_category_assign` | removed; use `local_deck_apply_changes` with `assign-category` |
| `deck_category_unassign` | removed; use `local_deck_apply_changes` with `unassign-category` |
| `stats_deck_summary` | `stats_local_deck_summary` |
| `scryfall_corpus_status` | `scryfall_card_data_status` |
| `scryfall_corpus_sync` | `scryfall_card_data_sync` |
| `scryfall_corpus_rollback` | `scryfall_card_data_rollback` |
| `scryfall_corpus_delete` | `scryfall_card_data_delete` |
| `scryfall_tag_search` | `scryfall_tags_search` |
| `scryfall_cards_by_tag` | `scryfall_installed_cards_by_tag` |
| `scryfall_snapshot_delete` | `scryfall_search_snapshot_delete` |
| `scryfall_card_get` | merged into `scryfall_cards_get` |
| `scryfall_card_collection` | merged into `scryfall_cards_get` |
| `scryfall_card_prints` | merged into `scryfall_card_details` (`include: ["prints"]`) |
| `scryfall_card_rulings` | merged into `scryfall_card_details` (`include: ["rulings"]`) |
| `scryfall_sets` | merged into `scryfall_reference_get` (`kind: "sets"`) |
| `scryfall_catalog` | merged into `scryfall_reference_get` (`kind: "catalog"`) |
| `scryfall_bulk_metadata` | merged into `scryfall_reference_get` (`kind: "bulk"`) |
| `scryfall_snapshot_list` | merged into `scryfall_search_snapshots_get` (`kind: "list"`) |
| `scryfall_snapshot_get` | merged into `scryfall_search_snapshots_get` (`kind: "get"`) |
| `archidekt_folder_list` | merged into `archidekt_folders_get` (`kind: "list"`) |
| `archidekt_folder_get` | merged into `archidekt_folders_get` (`kind: "get"`) |
| `archidekt_snapshot_list` | merged into `archidekt_snapshots_get` (`kind: "list"`) |
| `archidekt_snapshot_get` | merged into `archidekt_snapshots_get` (`kind: "get"`) |

### Card list tools and Commander rule facts

- Added `card_lists_compare` and `card_list_query` (Scryfall toolset). They
  compare, filter, group, and total card lists. A list is a saved
  `scryfall_search` (`scryfall-snapshot`), up to 150 Scryfall or Oracle IDs
  (`cards`), a local deck (`deck`), a deck as it was in a local backup
  (`deck-backup`), an Archidekt deck or snapshot (`archidekt-deck`,
  `archidekt-snapshot`), or another comparison (`compare`). Comparing applies
  one operation: union, intersection, difference, or symmetric difference.
  Archidekt sources need Archidekt credentials.
- Added the optional `rules` input to `local_deck_validate`. With
  `rules: "commander"` it reports deck size, commander eligibility, color
  identity, copy limits, format legality, Game Changers, commander pairing,
  and companion restrictions from installed Scryfall data. The facts carry no
  legal or illegal verdict. Calls without `rules` return what they did before.
- Added `local_deck_duplicates_preview`, which plans removals of repeated cards
  for a caller-chosen keep order and changes nothing.
- Added `stats_castability`: the exact chance that each spell in a deck can be
  cast on curve, for source groups and turns the caller chooses.
- Added `spellbook_deck_bracket_facts_get`: the Commander Spellbook flags for a
  local deck (Game Changer, mass land denial, extra turn, combos), without the
  provider's bracket verdict.
- Added `playgroup_games_stats_get`: win totals and the pod-size baseline by
  deck or player, with rates rounded to six places.
- Counts: with every toolset on and both credentials present, 86 tools in
  `remote` mode (73 in `local`, 59 in `read-only`). With Archidekt credentials
  only, 69, 58, and 44. Without credentials, 48 in `remote` and `local` mode
  and 35 in `read-only` mode.
- Added three MCP prompts, offered in every selection: `audit-deck` (takes
  `deckId`), `build-from-commander` (takes `scryfallId`), and `compare-decks`
  (takes `deckIdA` and `deckIdB`). Each is a ready-made first message that lists
  the steps of a workflow, names only tools of the default selection that needs
  no credentials, and says that the user and agent make every judgment. An
  argument must be a UUID, so values never contain spaces. Claude Code lists
  them as `/mcp__<server>__audit-deck` and so on. The capabilities resource now
  reports `promptCount` 3 (it was 0). A prompt with a missing or invalid
  argument returns an invalid-params error that names the argument. The README
  says how to find the IDs.
- Added worked examples to the README for "search results not in my deck"
  (`card_lists_compare`), "mana curve and pips" (`card_list_query`), Commander
  rule facts (`local_deck_validate` with `rules: "commander"`), and the inline
  category rules that replace `common-v1`. A test binds each request to the
  tool's input schema.
- Logged a warning when the SDK matches a tool the server's catalog does not
  hold, and made every `kind` choice of an input that accepts several shapes carry a generated description.
- Fixed the capabilities the server advertises under the pinned protocol
  (`2026-07-28`). The server removes the `logging` capability and the
  `listChanged` claims for tools, prompts, and resources, because it sends no
  such notifications and its lists are fixed for the session. The removal only
  matched the initialize result, which the pinned protocol does not use, so
  `server/discover` still advertised them. It now matches both.

### Earlier changes in this release

- Changed failed tool calls to set the MCP `isError` flag. Before this, every
  failure looked like a success to a client. Migration: clients that treated
  any non-error result as success should check `isError` and then the
  structured `kind` (`success` is the only kind without the flag). The
  structured body and text block are unchanged.
- Fixed input the server cannot read: it now returns `kind: "invalid-input"`,
  reason `invalid-arguments`, and a top-level `path` to the rejected value
  (for example `changes[0].entryDraft.quantity`), instead of a generic
  "An error occurred invoking" message. An unknown union `kind` lists the
  allowed kinds.
- Fixed `deck_apply_changes` and every other union input to accept the `kind`
  property in any position.
- Added `current` (the current local revision, remote fingerprint, or both) to
  stale-state `conflict` results from deck, statistics, Spellbook, on-curve,
  identity, categorization, and Archidekt operations, so a caller can preview
  again and retry.
- Fixed Archidekt push previews counting a reordered list of an entry's
  secondary categories as a change; the order is now ignored.
- Fixed Archidekt push apply so entries and categories it creates are linked in
  the new baseline; the next push preview plans only real changes. An entry
  that cannot be matched exactly is listed in the new `unlinkedLocalIds`
  field of the push result, and the next push removes and re-adds that row.
- Added `responseFormat` (`concise`, `standard`, `full`) and `fields` to
  `scryfall_search`, `scryfall_card_get`, `scryfall_card_collection`, and
  `scryfall_cards_by_tag`. The default is `standard`, which keeps every field
  of the old output. `concise` returns a small set of fields for comparing many
  cards; `fields` picks other concise fields by name, including extra prices,
  `legalitiesAll`, `games`, `reserved`, and `layout`. A `concise` page is cut
  to stay under 40,000 bytes, then sets `truncated`, `narrow`, and a
  `nextCursor` for the rest. In `concise`, `tags` (official oracle tag slugs)
  and `cheapestPriceUsd` (lowest USD price among installed English printings)
  come from installed card data and are `null`, with the reason in `limits`,
  when it is not installed. Results now also carry `responseFormat`.
- Breaking: removed `includeRaw` from those four tools. Migration: send
  `responseFormat: "full"` instead of `includeRaw: true`; it returns the same
  raw provider objects with the same 25-row page cap. `includeRaw` on
  `scryfall_card_prints`, `scryfall_card_rulings`, `scryfall_sets`,
  `scryfall_snapshot_get`, and `scryfall_tag_search` is unchanged.
- Added fields to every `standard` card: `rarity`, `power`, `toughness`,
  `loyalty`, `layout`, `games`, `reserved`, and `gameChanger` (`null` when
  Scryfall omits it). Each card face also gains `power`, `toughness`,
  `loyalty`, and `oracleId`.
- Added `include` (`cards`, `tags`), `fields`, `cursor`, and `pageSize` to
  `deck_get`. With `include`, the result holds a page of entries (25 by
  default, at most 100, cut to stay under 40,000 bytes) with
  `cardData` (`found`, `not-installed`, or `unresolved`), concise `card`
  fields, and oracle `tags` for each entry, plus `totalEntries` and
  `nextCursor`. An empty `include` list is rejected. Without `include` the
  result is the same as before.
- Changed tool schemas to publish a JSON Schema `enum` for parameters with a
  fixed set of values (cache policy, search order, interchange format IDs,
  Archidekt format and visibility, and others). Accepted values are unchanged.
- Changed descriptions: each `stats_*` tool lists the fields of `request` and
  one runnable example; `scryfall_search`, `scryfall_tag_search`, and
  `scryfall_cards_by_tag` explain tag filtering with `otag:`, and
  `scryfall_search`, `scryfall_card_collection`, and `scryfall_cards_by_tag` warn that
  a `standard` page can exceed 40,000 bytes.

## 0.10.0 - 2026-10-25

- Added the opt-in Commander Spellbook adapter with three read-only evidence
  tools: `spellbook_variant_search`, `spellbook_variant_get`, and
  `spellbook_deck_combos_find`. Each call makes at most one upstream request,
  is paced across processes that share a data root, and caches successful
  responses in `spellbook.db` for 15 minutes by default. Configure the cache
  with `--spellbook-ttl-minutes`, `MTGMCP__SPELLBOOK__TTL_MINUTES`, or
  `SPELLBOOK_TTL_MINUTES`.
- Changed the server to require MCP protocol `2026-07-28`. Older
  initialize-based clients are no longer supported.
- Fixed deck grouping from installed official Scryfall tags: exact source IDs,
  all-parent ancestry, strict rule validation, and an in-place `common-v1`
  correction.
- Fixed Archidekt sync to exclude new maybeboard cards from deck and price
  totals, keep category assignment order, and ignore volatile view counts in
  remote-state guards.
- Moved the toolchain to mise with reproducible package locks and SHA-pinned
  GitHub Actions, and updated Microsoft.Data.Sqlite, SQLitePCLRaw, and
  analyzers.
- Added the read-only `deck_on_curve_estimate` tool. It samples one bounded
  land-only cast-by-turn model for a saved deck using direct installed Scryfall
  facts and caller-supplied land rules. Results include source facts, replay
  details, uncertainty, miss counts, and clear limits; they do not recommend a
  deck change or predict real games.
- Added the Core-only `MtgMcp.OnCurve` calculation project, offline replay and
  MCP process tests, and the separate `task benchmark:oncurve` Release
  measurement for a fixed sanitized 99-card fixture.
- Fixed full Scryfall card-data imports to read current official bulk metadata
  and accept supported tag and ruling records that refer to a card row not
  present in the installed data.
- Fixed self-contained packages to include the native SQLite runtime. `task
  install` now starts a read-only Scryfall-enabled server after installation to
  catch packaged startup failures.
- Fixed the Task and release test filters for .NET 11 and xUnit MTP so a
  focused check cannot report success after running zero tests.

## 0.9.0 - 2026-07-12

- Removed the audited legacy product implementation from the isolated rewrite
  branch while retaining ordinary Git history and released artifacts.
- Reduced the production solution to dependency-light `MtgMcp.Core` and a
  minimal resources-only `MtgMcp.App` stdio server with no provider surface.
- Rebuilt focused unit, architecture, and process tests and reconciled task,
  CI, coverage, package, release, and smoke wiring with the foundation project
  set.
- Added closed operation-result and evidence-descriptor unions with stable,
  case-specific JSON contracts.
- Added `read-only`, `local`, and `remote` mode enforcement; JSON, environment,
  and command-line configuration; a non-creating `mtg-mcp/v0.9` data root; and
  sanitized legacy-data and startup boundaries.
- Added official MCP initialization and the deterministic
  `mtg://server/capabilities` resource with zero prompts and exact static
  capability-tool counts.
- Split the one-shot process probe from official-client MCP smoke coverage,
  added installed-package MCP validation, and made the App project version the
  default for package and release automation.
- Added immutable format-neutral deck contracts, revisioned SQLite persistence,
  stable pagination, transactional entry/category mutations, and
  provider-neutral synchronization bindings.
- Added guarded opaque deck backup creation, inventory, restore, rollback, and
  deletion with integrity, schema, and database-fingerprint checks.
- Added the exact nineteen-tool local `deck_*` surface, read-only mode filtering,
  official-client schema/annotation tests, and representative MCP workflows.
- Added four offline `deck_*` interchange tools for lossless native JSON,
  generic text, fingerprint-guarded import creation, and checksummed artifact
  bundles; dated UI acceptance makes Archidekt and Moxfield formats available
  with provider-applied and companion-only fields reported separately.
- Added static startup-selected capability toolsets with `default`, `all`,
  `none`, and explicit profiles, exact mode intersection, schema-v3 capability
  metadata, and no dynamic tool-list advertisement.
- Added `MtgMcp.Scryfall` with eighteen typed tools for official API evidence,
  exact-request snapshot replay, an explicit four-dataset card-data lifecycle, and
  separately labeled Oracle/art community tags in one `scryfall.db`.
- Added configurable 24-hour evidence reuse, current-plus-previous card-data
  generations, cross-process SQLite leases and conservative 500-millisecond
  pacing, fail-fast blocking responses, bounded retry, and offline/live
  acceptance suites.
- Added the opt-in 23-tool `archidekt` capability for fresh deck, folder, and
  named-snapshot evidence; guarded pull/push/restore; private deck lifecycle;
  and verified empty-folder/snapshot cleanup.
- Added strict redacted credential loading, two-second process-wide per-account
  pacing, a rolling 30-request/minute ceiling, composed 150-request tool
  budgets, bounded read retry, fail-fast `403`/`429`, and no ambiguous write
  retry. The provider origin is fixed to Archidekt. A disposable authenticated
  lifecycle verifies current provider routes and leaves no remote test state.
- Treat Archidekt's post-mutation category rank as provider-normalized evidence
  so snapshot restore does not report a false mismatch when the provider
  renumbers otherwise identical categories.
- Added content-addressed immutable snapshot payloads, schema-checksum
  validation, abandoned-staging recovery, alias and face-art tag joins,
  face-aware exact-name matching, and provider-timestamped price/rank freshness.
- Added compact normalized provider output by default, opt-in lossless raw
  objects, and compact snapshot members with stable ordinals and checksums.
- Expanded collection resolution to 150 ordered identities with local-first
  deduplication, atomic 75-identifier provider batching, and evidence-bound
  cursor pages that never reacquire during continuation.
- Added a bounded official-client Red/White Weenies workflow that proves local
  deck creation, structural validation, batch card resolution, evidence
  persistence, and immutable replay without a full card-data download.
- Added the opt-in 16-tool Playgroup Public API 1.0.0 capability: all 15
  documented operations plus redacted auth status, lossless provider evidence,
  explicit unsupported deck updates, exact 14/14/16 mode visibility, and no
  cross-provider ranking or hydration.
- Added fixed-origin bearer isolation, process-wide 250-millisecond pacing,
  bounded GET retry and `Retry-After` handling, single-attempt remote writes,
  exact OpenAPI drift checks, fixture-only write proof, and an opt-in `/me`
  live-read test that cannot mutate provider state.
- Added strict Playgroup API-key loading from the standard user-profile
  `.mtg-mcp/playgroup.json` file, with explicit configuration precedence,
  redacted failures, and deterministic test-process isolation.
- Bounded the provider's multi-megabyte all-commander turn-damage response and
  changed its MCP tool to return only the exact caller-selected commander row
  with full-source checksum evidence.
- Made Windows release cleanup fall back to extended-length filesystem paths
  for deeply nested installed-tool artifacts.
- Added exact-only, evidence-bound deck identity reconciliation preview/apply,
  raising the deck toolset to 25 tools and the complete surface to 47/69/82 by
  mode without adding legality or fuzzy-resolution behavior.
- Added eight default-enabled, read-only `stats_*` tools for exact
  hypergeometric and multivariate probability, explicit turn and mulligan
  schedules, caller-declared mana/package allocation, inverse counts, and
  deterministic deck summaries. The current surfaces are 30/51/51 for
  `default` and 55/77/90 for `all`.
- Added `MtgMcp.Statistics` as a BCL-only project with reduced `BigInteger`
  rationals, invariant 12-place displays, a request-wide work budget, and
  independent exhaustive combinatorial oracles. No format or legality rule,
  provider lookup, sampling fallback, or recommendation entered the surface.
- Replaced the flat batch-change input with an eleven-variant closed schema,
  described every public MCP input, and upgraded capability metadata to schema
  6 with separate implementation and credential states and no provider I/O.
- Split Scryfall card data, snapshots, storage, and pacing into clear owners, and
  Archidekt deck, folder, snapshot, transport, mapping, and synchronization
  ownership behind behavior-preserving public facades.
- Added three deterministic `deck_*` categorization tools with explicit inline
  rules or the transparent `common-v1` preset, evidence-bound preview/apply,
  and no inferred category meaning.
- Completed the stable surface at 28 deck, 18 Scryfall, 8 statistics, 23
  Archidekt, and 16 Playgroup tools. The default profile exposes 32/54/54 tools
  by mode; the complete profile exposes 57/80/93.
- Fixed Archidekt synchronization for provider-renumbered categories, explicit
  primary-category submission, category-array reordering, and duplicate
  provider relations while preserving multiplicity.
- Passed packaged method acceptance for all 93 tools with 88 live passes, two
  fixture-backed Scryfall lifecycle operations, one provider-generation-pending
  rollback, and two fixture-only Playgroup writes. Archidekt cleanup and zero
  Playgroup writes were verified.
- Replaced active legacy architecture, adapter, and toolset documentation with
  concise task-oriented `0.9.0` guidance.

## 0.8.0 - 2026-06-28

- Audited package boundaries, release metadata, docs, and local build coverage
  for the refactor release.
- Added MCP host observability with redacted per-tool completion logs,
  OpenTelemetry-ready tool-call metrics/traces, `logging/setLevel` handling,
  and `mcpLoggingLevel` in server diagnostics.
- Added report-only performance ratchet output for release review.
- Fixed local release smoke and install validation to use the repo-pinned .NET
  runtime when validating the packaged tool.
- Added observability, MCP compatibility, performance ratchet, and 1.0 readiness documentation.
- Replaced persisted deck edit operations and several closed-set statuses with
  typed union/enum models while preserving legacy JSON strings and flat plan
  operation payloads.
- Removed unsupported Reddit discussion and Spicerack source integrations from
  registration, CLI auth help, docs, and surface tests.
- Added MCP protocol conformance improvements: object-root typed tools now
  advertise titles and output schemas with structured content, tool validation
  errors return a coded structured payload, `workspace_list` and
  `deck_plan_list` use cursor-paged envelopes, and `mtg://workspaces` is
  discoverable as a resource.
- Unified MCP `detailLevel` parsing across presenters while keeping legacy
  `compact` and `includeWorkspace` inputs during the deprecation window.
- Added method-level MCP toolset and operation-mode filtering so deployments
  advertise only selected tools that can run in the configured mode.
- Added report-only MCP surface metrics, README surface parity coverage, a
  pre-1.0 versioning/deprecation policy.
- Documented the full registered MCP tool/resource/prompt surface in the README.
- Added explicit ramp-evaluator applicability metadata to `deck_evaluate_card`
  and labeled non-ramp cards with `not-applicable` instead of relying on a bare
  zero score.
- Added `RngKind` metadata to the heuristic goldfish result family, labeled as
  `system-random` to distinguish it from the Stats Lab deterministic RNG.
- Aligned default app assembly/package version metadata with `server.json` so
  local smoke output reports the intended pre-release version unless release
  tasks override it.
- Initial MCP server scaffold with Scryfall research, local workspaces, Archidekt writeback, checkpoints, prompts, resources, Taskfile workflows, and tests.
