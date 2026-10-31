# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` gives an LLM grounded Magic: The Gathering card, deck, provider, and
statistical evidence. The LLM makes deckbuilding decisions.

The `0.12.0` tool surface builds on the `0.9.0` stdio server design. Every toolset is
on by default. With Archidekt and Playgroup credentials, the server exposes 86
tools in `remote` mode (the default), 73 in `local` mode, and 59 in `read-only`
mode. Without credentials it exposes 48, 48, and 35, because the Archidekt and
Playgroup toolsets turn on only when their credentials are present. It also
exposes one `mtg://server/capabilities` resource and three prompts (see
[Start a workflow with a prompt](#start-a-workflow-with-a-prompt)). It does not
migrate `0.8.x` data or tool schemas.

## Status

The evidence-first rewrite is complete. `0.9.0` is the first stable release of
the new server.

| Feature | Tools | Default | Storage or writes |
| --- | ---: | --- | --- |
| Local decks and interchange | 19 | Yes | Local |
| Scryfall evidence | 15 | Yes | Local cache and card data |
| Exact statistics and seeded sampling | 10 | Yes | No |
| Archidekt | 21 | When credentials are present | Remote |
| Playgroup | 17 | When credentials are present | Remote |
| Commander Spellbook evidence | 4 | Yes | Local cache only |

The packaged acceptance run before the Spellbook addition made 88 safe tool
calls. It uses saved test data for the two Scryfall card-data download tools and
the two Playgroup write tools because a live cleanup route is unavailable.
Scryfall rollback needs Scryfall to publish another data version; until then,
the check reports that it is waiting. Commander Spellbook has its own limited,
read-only live check.
See [live acceptance](docs/llms/plcs/completed/rewrite-stabilization-cutover/LIVE_ACCEPTANCE.md).

## Start the server

Bootstrap the repository, then run the MCP smoke test:

```powershell
.\bootstrap.ps1
task smoke:mcp
```

On Linux or macOS:

```bash
./bootstrap.sh
task smoke:mcp
```

Bootstrap installs Mise when needed. Mise installs Task and PowerShell from
`mise.toml` and the .NET SDK from `global.json` in an isolated tool directory.
After bootstrap, run `task <command>` normally. The Taskfile invokes Mise for
.NET; if a fresh shell cannot find Task, activate Mise or temporarily use
`mise exec -- task <command>`.

Committed Mise and NuGet lock files keep setup repeatable. Version pins live in
`mise.toml`, `global.json`, `Directory.Packages.props`, and `dotnet-tools.json`.
Use `task deps:check` to inspect available updates. Use `task deps:update` only
when you intend to review the related pin and lock-file changes.

Use `mtg-mcp` as the MCP command. The default invocation is equivalent to:

```text
mtg-mcp --mode=remote --toolsets=default
```

Use `--smoke` for a one-shot configuration check. It does not start an MCP
session.

`task install` also starts and stops an isolated, read-only server with the
Scryfall toolset enabled. That catches packaged SQLite and Scryfall startup
problems that `--smoke` cannot see.

## Choose a mode

Modes control authority.

| Mode | Reads | Local writes | Remote writes |
| --- | --- | --- | --- |
| `read-only` | Yes | No | No |
| `local` | Yes | Yes | No |
| `remote` | Yes | Yes | Yes |

`remote` is the default and gives full read and write access, so start the
server with no mode setting. Use `--mode=local` to hide Archidekt and Playgroup writes (which also hides
`archidekt_push_apply`), or `--mode=read-only` to hide every write. In Claude Code, the destructive
remote tools (including `archidekt_push_apply`) ask you to approve each call, so full access does not mean unattended
changes to Archidekt. A Scryfall cache miss can need a local write to record
the response and rate-limit timing. In `read-only`, that miss returns
`local-write-required` before any HTTP request.

Commander Spellbook reads are available in all three modes. They never change a
deck or make a write request to the source. Successful responses use a short-lived
local cache so repeated exact requests do not create needless provider traffic.

## Choose toolsets

Toolsets control which tools the session can see. They do not grant permission
to write.

| Value | Result |
| --- | --- |
| `default`, `all`, or omitted | Enable every toolset whose credentials are present |
| `none` | Expose no tools |
| Comma-separated list | Enable the exact named toolsets, still subject to credentials |

Available toolsets are `decks`, `scryfall`, `stats`, `archidekt`, `playgroup`,
and `spellbook`. Selection is fixed for the MCP session. `archidekt` and
`playgroup` are off when their credentials are absent, even when named; see
[Toolsets](docs/toolsets.md) for the exact rules.

| Selection | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| Default or `all`, no credentials | 35 | 48 | 48 |
| Default or `all`, Archidekt credentials only | 44 | 58 | 69 |
| Default or `all`, both credentials | 59 | 73 | 86 |
| `none` | 0 | 0 | 0 |

Read `mtg://server/capabilities` to see the active mode, toolsets, why a toolset
is off (`disabledReason`), tool counts, credential state, package version, and
local data state. It does not contact a provider. The server also sends short
instructions at start that name the first tool for each job.

## Configure the server

Pass configuration on the command line, in environment variables, or in an
optional `mtg-mcp.json` in the working directory. Command-line values win over
environment values. Environment values win over JSON.

| Setting | Command line | Environment | JSON key |
| --- | --- | --- | --- |
| Operation mode | `--mode` | `MTGMCP__MODE` | `MODE` |
| Toolsets | `--toolsets` | `MTGMCP__TOOLSETS` | `TOOLSETS` |
| Data root | `--data-dir` | `MTGMCP__DATA_DIR` | `DATA_DIR` |
| Scryfall TTL | `--scryfall-ttl-hours` | `MTGMCP__SCRYFALL_TTL_HOURS` | `SCRYFALL_TTL_HOURS` |
| Commander Spellbook TTL | `--spellbook-ttl-minutes` | `MTGMCP__SPELLBOOK__TTL_MINUTES` | `SPELLBOOK_TTL_MINUTES` |

The default data root is the platform application-data directory under
`mtg-mcp/v0.9`. All MCP processes that use the same root reuse `decks.db` and
`scryfall.db`. Reads do not create the root.

### Configure Archidekt

The `archidekt` toolset turns on when you set both `MTGMCP__ARCHIDEKT__USERNAME` and
`MTGMCP__ARCHIDEKT__PASSWORD`, or create:

```text
~/.mtg-mcp/archidekt.json
```

The provider origin is fixed. Authentication output never contains credentials,
account identity, or a credential path.

### Configure Playgroup

The `playgroup` toolset turns on when you set `MTGMCP__PLAYGROUP__API_KEY`, or create:

```text
~/.mtg-mcp/playgroup.json
```

The file contains one `apiKey` property. The provider origin is fixed to the
official public API.

## Work with local decks

The `decks` toolset supports:

- revisioned deck, entry, zone, category, and provider-binding changes, made
  through one atomic ordered batch tool, `local_deck_apply_changes` (a batch of
  one change is normal);
- guarded backup, restore, and delete;
- native JSON and generic text import and export;
- manual import and export files for Archidekt and Moxfield;
- exact Scryfall identity reconciliation; and
- tag-rule grouping with repeatable results.

Every change to an existing deck requires `expectedRevision`. The local store
is not tied to one import format. Validation checks structure. With `rules: "commander"` it also
reports Commander rule facts from installed card data. It gives no legal or illegal verdict, and it
says nothing about card quality or strategic fit.

Use `local_deck_identity_reconcile_preview` before apply. Resolution uses printing ID,
set and collector number, Oracle ID, then exact name. It never uses fuzzy
matching or selects an arbitrary printing.

Use `local_deck_get` for the stored deck. Pass `include` with `cards`, `tags`, or both
to add, for each entry, concise Scryfall card fields (the entry's exact
printing) and official oracle tag slugs, read from installed card data and
returned in pages of entries with `nextCursor`. Each entry then carries
`cardData`: `found`, `not-installed` (run `scryfall_card_data_sync`), or
`unresolved` (no exact Scryfall identity, or not in the installed data). A page
is cut to stay under 40,000 UTF-8 bytes and then sets `truncated`, `narrow`, and
`nextCursor`. `fields` narrows the card fields; it uses the same names as the
Scryfall card tools. A deck too large for one result pages the same way even
without `include`, so read every page (pass `nextCursor` back as `cursor`; `pageSize` sets the page
length) before you decide what a deck holds. Every page carries deck-wide totals: `entryCount`,
`cardCount` (counting quantities), and `cardsByZone`. A cut page's `narrow` says it is one page of the deck:
"This page shows N of M entries; the deck is not missing the rest. Follow nextCursor." Compare the totals
with the entries you hold before you add a card you think is missing. The summary of every tool that writes a deck
carries the same totals (`cardsByZone` with the entry count).

The tools that write a deck (`local_deck_apply_changes`, `local_deck_create`,
`local_deck_import_create`, `local_deck_identity_reconcile_apply`, and
`local_deck_category_rules_apply`) return a summary: the deck ID, the new and
previous revision, the entry count, counts of each kind of change, the IDs of the
entries and categories that changed (the first 100 in all, with `truncated` and
`narrow` when there are more; the change counts are always exact), and `issues` for
anything refused, skipped, ambiguous, or unresolved (the first 30, with `issueCount` exact). Pass `returnDeck: true` to get the whole deck as well; for a deck of a few hundred
entries that result can be too large for the client to show inline, so read the deck with
`local_deck_get` instead.

An entry added with only a card name resolves its card identity when it is added,
from installed card data and with the same exact-name rule as identity
reconciliation. An exact name gives the Oracle ID; a card played in a deck is preferred over a token,
emblem, or art card of the same name. A name with a set code and collector number also gives the
printing. A double-faced card typed by one face takes Scryfall's full name, such as
`Delver of Secrets // Insectile Aberration`. A name that fits two different cards (two Oracle IDs) is
not resolved and is listed in `issues` as `ambiguous`, with the count. A name that does not resolve is
still added, left unresolved, and listed in `issues`, so one typo never fails a batch. When card data is not
installed, the entries are added as given and one note in `issues` says resolution was skipped. The note
says a push to Archidekt does not need card data: a push uses Archidekt's default printing for an entry with
no printing set (see [Synchronize Archidekt](#synchronize-archidekt)). Run `scryfall_card_data_sync` and then
`local_deck_identity_reconcile_preview` only to fill in the card identity. Entries added by name therefore
need no `local_deck_identity_reconcile_*` step before an Archidekt push.

Use `local_deck_category_rules_preview` and `local_deck_category_rules_apply` to
evaluate caller-owned tag rules. The preview validates the rules first; rules
are always inline, and the MCP does not decide what a category means. See
[Category rules without a preset](#category-rules-without-a-preset) for what
replaced the removed `common-v1` preset.

## Sample games and estimate mana on curve

`stats_simulate` replaces `local_deck_on_curve_estimate`, which was removed in
`0.12.0`. It is in the `stats` toolset. Use its `on-curve` preset to sample how
often one main-zone card can be cast by a chosen turn: give the saved deck, the
target entry ID, one rule for each eligible single-faced land, a turn limit, and
play or draw. The preset expands to ordinary rules, which the result echoes in
`appliedRules`, and, within the work limit below, it replays the removed tool's success counts for the same
deck, seed, and inputs. It needs installed card data for the target's cost and
the lands' colors, and it models only lands as mana, one land a turn.

For anything else, state the rules yourself. `stats_simulate` shuffles the main
zone of one saved deck and plays seeded games under groups, opening-hand and
per-turn rules, and conditions that you write as structured data. It applies
only the fixed engine rules in its description, such as the order of rules each
turn, the London mulligan with an optional free first mulligan, `draw-cards`
that draw at once or every turn, group `produces` colors, and `can-cast`
checks that pay a mana cost together with all mana already spent that turn. It
chooses no strategy: every pick, limit, and mulligan rule is a field you supply.
The same request and seed give the same result on every platform. A request is
limited to samples x turns x deck cards of 50,000,000 (the removed tool accepted more: lower samples or turns for a larger run) and to an operation estimate of 400,000,000, and the result states
the seed, the applied rules, each condition's hits and 95% Wilson interval,
mulligan keep counts, how many games emptied the library, and up to 10 full
sample games. This example asks how often a 99-card deck with 40 land cards has
four mana on turn 4 when it plays one land a turn:

<!-- example: stats_simulate -->
```json
{
  "deck": { "deckId": "0193e97a-ef95-7700-8000-000000000001" },
  "groups": [
    { "id": "lands", "select": { "kind": "category-ids", "ids": ["0193e97a-ef95-7700-8000-000000000021"] } }
  ],
  "openingHand": { "size": 7, "onThePlay": true },
  "eachTurn": [
    { "kind": "play", "group": "lands", "pick": "random", "maxPerTurn": 1, "manaFrom": "same-turn" }
  ],
  "turns": 4,
  "conditions": [
    { "id": "four-mana-turn-4", "check": { "kind": "mana-at-least", "amount": 4, "turn": 4 } }
  ],
  "samples": 10000,
  "sampleHands": 0,
  "seed": "0000000000000001"
}
```

A real run of this request, against a saved deck with 40 land cards and 59
other cards, returned `hits` 6365 of 10,000 (`rate` 0.6365, `wilson95` 0.627022
to 0.645873). Another seed gives a slightly different count. The result does
not say whether that rate is good enough; that is your judgment.

## Import and export decks

Use `local_deck_interchange_formats` to inspect preservation limits.

| Format | Notes |
| --- | --- |
| `mtg-mcp-json-v1` | Lossless native format |
| `generic-text-v1` | Quantity, name, zone headings, and printing hints |
| `archidekt-text-v1` | Manual import plus native/category companions |
| `moxfield-bulk-edit-v1` | Bulk Edit text plus native/tag companions |

These provider export files do not contact a provider. Excluded entries remain
in the native companion file and are omitted from provider text.

### Large results

Results are cut to fit the client; see [Output control](docs/output-control.md). Claude Code saves a tool
result of more than 50,000 characters to a file, so the server aims for 40,000 UTF-8 bytes.
These tools page their rows, so read every page before you decide what a result holds. Each
sets `truncated` and `narrow` when it left rows out, and names the argument that reads the next page:
`local_deck_identity_reconcile_preview`, `local_deck_import_preview`, and
`local_deck_category_rules_preview` take `offset` and return `nextOffset`; their fingerprints and apply
tokens cover the whole preview, so the apply tool takes the guards of any page and still applies everything.
On later pages pass `expectedPreviewFingerprint` (the `previewFingerprint`, or for the import preview the
`fingerprint`, of the first page) to be refused if the preview changed in between. The first page of the
import preview also carries the diagnostics, unresolved identities, categories, and provider bindings, each cut to
fit with its exact count beside it; the saved synchronization snapshots of a native file are counted, not returned.
A paged deck cuts its name to 1,000 UTF-8 bytes (`nameTruncated`) and its description to 8,000 on the first page only
(`descriptionTruncated`, `descriptionLength`). A deck write summary cuts the name the same way.
`local_deck_export_bundle` lists every artifact (file name, media type, total length in UTF-16 code units, and the
SHA-256 of the whole content) and returns one chunk of one artifact's content of at most 30,000 UTF-8 bytes of
JSON text; pass `artifact` and `offset` (the previous `chunk.nextOffset`, a count of UTF-16 code units) to read on.
The chunks joined in order are the exact file. The field-preservation report comes only with the chunk at
offset 0. The Scryfall card tools cut a page of cards, in any `responseFormat`, to the size limit and return
`nextCursor` for the rest; they also report `returnedCount`, `requestedCount`, and `cutReason` (see
[Query Scryfall](#query-scryfall)).

## Query Scryfall

The `scryfall` toolset supports search, card lookup (`scryfall_cards_get`, one or
many cards), printings and rulings (`scryfall_card_details`), sets, catalogs, and
bulk metadata (`scryfall_reference_get`), autocomplete, tag evidence,
saved request snapshots (`scryfall_request_snapshots_get`), and explicit
card-data download operations.

The local card-data store contains All Cards, Rulings, Oracle Tags, and Art
Tags. Card-data downloads are explicit. They never run at startup or in the
background.

### Optional: install full Scryfall card data

A full install contains card records, rulings, Oracle tags, and art tags. It
does not contain card images. It is useful for local tag and card-data
workflows; ordinary card lookups can use the provider and small local cache
without it.

The download is multi-gigabyte and an update needs temporary working space.
Start it only through the explicit full-data tools in `local` or `remote` mode.
The public names `scryfall_card_data_status`, `scryfall_card_data_sync`,
`scryfall_card_data_rollback`, and `scryfall_card_data_delete` are existing API
names for inspecting and managing that installed card data.

Freshness policies are `default`, `cache-only`, and `refresh`. The default TTL
is 24 hours. Immutable snapshots do not expire.

`scryfall_cards_get` accepts 150 ordered lookups (pass one to read one card). It uses local card-data
hits first, deduplicates provider misses, and sends provider batches of at most
75. Results use stable cursor pagination.

The card tools `scryfall_search`, `scryfall_cards_get`, and
`scryfall_installed_cards_by_tag` take `responseFormat`:

| Value | Result |
| --- | --- |
| `standard` (default) | Every card field this server returns. New fields are added, never removed. |
| `concise` | A small set of fields for comparing many cards: IDs, name, cost, type, colors, color identity, produced mana, keywords, oracle text, power, toughness, loyalty, the formats the card is legal in, USD price, cheapest USD price among installed English printings, rarity, set, oracle tag slugs, Game Changer flag, and EDHREC rank. `fields` picks other names, including `prices.usdFoil`, `prices.usdEtched`, `prices.eur`, `prices.eurFoil`, `prices.tix`, `legalitiesAll`, `games`, `reserved`, and `layout`. |
| `full` | `standard` plus the raw provider objects. Pages are limited to 25 rows. |

In `concise`, `tags` and `cheapestPriceUsd` come from installed card data and are
`null`, with the reason in `limits`, when it is not installed. Every format is
cut to stay under 40,000 UTF-8 bytes and then sets `truncated`, `narrow`, and
`nextCursor`, so a `standard` or `full` page can hold fewer cards than
`pageSize`; use `concise` or `fields` to fit more. `includeRaw` was replaced by
`responseFormat: "full"` on these three tools.

`pageSize` is a maximum. Every page of these three tools, and of `card_lists_compare`, reports
`returnedCount` (the cards on the page) and `requestedCount` (the cards the page would hold without the size
limit). A page that was cut also reports `cutReason: "result-size-limit"` and a `narrow` hint with the numbers:
the kept and requested counts, the average bytes per card on the page, where `nextCursor` continues (no card is
skipped), and that leaving `oracleText` out of `fields` fits more cards in the concise format. `oracleText`
is usually the largest field. A cut page with `nextCursor` is still a page of one query, so read on with the
cursor.

`edhrecRank` (and `order: "edhrec"`) counts how many decks include the card in any slot. It is not a rank of
how often a card is played as commander, so it cannot answer "which commanders are most played". The server has
no commander popularity rank and no EDHREC source.

For arbitrary queries, Scryfall remains the source of truth. The cache reuses
only the exact same request. Card facts and community tags stay separate kinds
of information.

## Read Commander Spellbook evidence

The `spellbook` toolset has four tools that return Commander Spellbook
data:

- `spellbook_variant_search` returns one bounded page for an exact Commander
  Spellbook query.
- `spellbook_variant_get` returns one exact source variant by ID.
- `spellbook_deck_combos_find` sends only the `commander` and `main` entries
  from one exact saved deck revision, then reports the entries it skipped.
- `spellbook_deck_bracket_facts_get` sends the same entries and returns the
  Game Changer, mass land denial, extra turn, banned, and combo flags that
  Commander Spellbook reports. It drops the provider bracket verdict.

The tools preserve Commander Spellbook JSON and name the source, request,
retrieval time, cache state, checksum, and limits. They do not rank results,
infer card roles, or recommend a combo or card change. The cache lasts 15
minutes by default; configure it with a whole number from 1 through 1,440
minutes.

## Work with card lists and deck rule facts

These tools return facts only. They never say a deck is legal or illegal.

- `card_lists_compare` applies one operation (union, intersection, difference, or symmetric difference)
  to two card lists. A list is a saved `scryfall_search`, up to 150 Scryfall or Oracle IDs, a local
  deck, a deck backup, an Archidekt deck or snapshot, or another comparison.
- `card_list_query` filters, groups, and totals any one card list by named
  fields, with a cursor for large results.
- `local_deck_validate` takes an optional `rules: "commander"` and then adds
  Commander facts from installed Scryfall data: deck size, commander
  eligibility, color identity, copy limits, format legality, Game Changers,
  commander pairing, and companion restrictions.
- `local_deck_duplicates_preview` finds repeated cards in a deck and plans the
  removals for a caller-chosen keep order; it changes nothing.
- `playgroup_games_stats_get` totals Playgroup games by deck or player.

### Worked examples

Each example shows the arguments you send and the shape of what comes back.
The results are trimmed to the fields that matter and use a small deck
("Golgari Ramp": Kodama of the East Tree as commander, ten Forest, Llanowar
Elves, Sol Ring, and Rhystic Study) with card data installed. A test binds every
request below to the real tool schema, so these requests cannot drift from the
tool schemas.

**Search results that are not in my deck.** Run `scryfall_search` first. It
saves the request as a snapshot and returns `snapshot.snapshotId`. Then ask for
the left list minus the right list:

<!-- example: card_lists_compare -->
```json
{
  "left": { "kind": "scryfall-snapshot", "snapshotId": "0193e97a-ef95-7700-8000-0000000000a1" },
  "right": { "kind": "deck", "deckId": "0193e97a-ef95-7700-8000-000000000001" },
  "operation": "difference",
  "responseFormat": "concise"
}
```

The saved search held five ramp cards and the deck holds two of them, so three
remain. Only some fields of each card are shown here:

```json
{
  "kind": "success",
  "data": {
    "counts": { "left": 5, "right": 5, "result": 3, "unmatched": 0 },
    "page": {
      "items": [
        { "name": "Arcane Signet", "manaCost": "{2}", "typeLine": "Artifact" },
        { "name": "Birds of Paradise", "manaCost": "{G}", "typeLine": "Creature — Bird" },
        { "name": "Cultivate", "manaCost": "{2}{G}", "typeLine": "Sorcery" }
      ],
      "totalCount": 3
    },
    "unmatched": [],
    "sources": [
      { "side": "left", "kind": "scryfall-snapshot", "entries": 5 },
      { "side": "right", "kind": "deck", "revision": 1, "entries": 5 }
    ],
    "responseFormat": "concise"
  }
}
```

Deck entries that have no Scryfall card (a home-made proxy, for example) are
listed in `unmatched` and never guessed. `matchBy` defaults to `oracle`, so two
printings of one card count as the same card.

**Mana curve and pips.** Ask `card_list_query` for the deck, grouped by mana
value, with the copy count, colored pips, and color sources of each group:

<!-- example: card_list_query -->
```json
{
  "source": { "kind": "deck", "deckId": "0193e97a-ef95-7700-8000-000000000001", "zones": ["main", "commander"] },
  "groupBy": ["manaValue"],
  "metrics": ["quantity", "pips", "sources"]
}
```

```json
{
  "kind": "success",
  "data": {
    "matched": { "cards": 5, "quantity": 14 },
    "groups": [
      { "key": { "manaValue": 0 }, "quantity": 10, "pips": {}, "sources": { "G": 10 } },
      { "key": { "manaValue": 1 }, "quantity": 2, "pips": { "G": 1 }, "sources": { "G": 1, "C": 1 } },
      { "key": { "manaValue": 3 }, "quantity": 1, "pips": { "U": 1 }, "sources": {} },
      { "key": { "manaValue": 6 }, "quantity": 1, "pips": { "G": 2 }, "sources": {} }
    ],
    "unmatched": [],
    "limits": [
      "Sources count the colors Scryfall lists as produced mana. Whether a land enters tapped is not modeled, ...",
      "Pips count printed mana symbols only; cost changes and alternate costs are not modeled."
    ]
  }
}
```

Drop `groupBy` to get one total for the whole deck. Add `where` to filter (for
example creatures only) or `rows` to list the cards themselves.

**Commander rule facts.** Call `local_deck_validate` with `rules: "commander"`:

<!-- example: local_deck_validate -->
```json
{ "deckId": "0193e97a-ef95-7700-8000-000000000001", "rules": "commander" }
```

The result keeps the structural report and adds `rules`. Each fact has a
`passed` flag or a `status`, and names the entries involved. Here the deck has
14 cards instead of 100 and holds one blue card in a green commander's deck:

```json
{
  "kind": "success",
  "data": {
    "isStructurallyValid": true,
    "issues": [],
    "rules": {
      "rules": "commander",
      "deckSize": { "passed": false, "count": 14, "expected": 100 },
      "commanders": { "passed": true, "count": 1, "items": [
        { "name": "Kodama of the East Tree", "eligible": true, "via": "legendary-creature" } ] },
      "colorIdentity": { "passed": false, "identity": ["G"], "offIdentity": [
        { "name": "Rhystic Study", "colorIdentity": ["U"] } ] },
      "singleton": { "passed": true, "breaks": [] },
      "legality": { "passed": true, "issues": [] },
      "gameChangers": { "status": "known", "count": 1, "entries": [ { "name": "Rhystic Study" } ] },
      "companions": [],
      "unresolved": [],
      "limits": ["Facts cover entries whose Scryfall card was found; the others are in unresolved."]
    }
  }
}
```

A `passed: false` is a fact about the deck against one rule, not a verdict. You
and the user decide what to change.

### Category rules without a preset

`local_deck_category_rules_preview` and `local_deck_category_rules_apply` take
inline rules only. The `common-v1` preset was removed, and a source of kind
`preset` now returns `invalid-input`. The preset mapped four role names to
four official Scryfall oracle tags, each with its descendant tags and the
minimum weight `weak`. The same rules sent inline are one rule per role, with
the ID of a category that exists in the deck in `categoryId`, the tag's ID in
`tagId`, and `includeDescendants` on. If you set a `primaryPriority` on a
preset binding, set it on the matching inline rule. The four tag IDs below are
the ones `common-v1` used:

| Role | Oracle tag ID |
| --- | --- |
| ramp | `2f3e4ad7-5e60-41b4-bdbc-653f16869cf6` |
| card-draw | `b6448c45-ce65-4848-aa98-2151e4e07437` |
| removal | `444f824c-f910-4530-9dbe-ede7a84cd7f9` |
| recursion | `82b824ad-648f-467f-a190-2e0fa9a795d2` |

<!-- example: local_deck_category_rules_preview -->
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
          "categoryId": "0193e97a-ef95-7700-8000-000000000022",
          "allOf": [{
            "tagType": "oracle", "tagId": "b6448c45-ce65-4848-aa98-2151e4e07437",
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
        },
        {
          "categoryId": "0193e97a-ef95-7700-8000-000000000024",
          "allOf": [{
            "tagType": "oracle", "tagId": "82b824ad-648f-467f-a190-2e0fa9a795d2",
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

Add `expectedRevision` when you mean to apply the preview afterwards. Tags are
resolved against installed card data, so run `scryfall_card_data_sync` first.
Use `scryfall_tags_search` to find other tags, then use their `exactSlug`
instead of `tagId` if you prefer.

## Calculate exact statistics

The `stats` toolset provides ten read-only tools for:

- univariate and multivariate hypergeometric probability;
- probability-by-turn tables;
- caller-defined mana-source availability;
- package and combo assembly;
- explicit mulligan schedules;
- minimum-copy and minimum-source solving;
- deterministic deck composition summaries;
- exact on-curve castability of each spell in a deck (`stats_castability`); and
- seeded sampling of rules you state (`stats_simulate`), which is an estimate and says so.

Every probability returns an exact reduced fraction and a stable decimal. The
caller supplies the population, groups, turn draws, mana capabilities, and keep
rules. The MCP does not infer roles or whether a result is good, and it gives no legality verdict.

Deck-backed statistics can select entries by entry ID, zone name, or category
ID. This lets an LLM use existing Archidekt groups or deterministic categories
without hiding the selected cards.

## Synchronize Archidekt

The `archidekt` toolset turns on when Archidekt credentials are present. Use
`remote` mode (the default) for remote writes.

The adapter supports owned deck list/get/create/delete, pull, push, diff,
folders, and named snapshots. Apply operations require current revisions,
fingerprints, and preview evidence. The MCP never chooses a conflict winner.

Requests start at least two seconds apart per configured account. The adapter
allows at most 30 starts in 60 seconds and 150 requests per tool invocation.
It stops on `403` and `429`. Ambiguous mutations are not retried. A push of more
than about 140 operations needs more than 150 requests, so `archidekt_push_apply`
cannot run it in one call; push the change in smaller edits.

Results are cut to fit the client. Claude Code saves a tool result longer than
50,000 characters to a file and shows the agent only the path, so these tools aim
for 40,000 UTF-8 bytes and page their rows. `archidekt_deck_get` leads with a summary (see below) and
pages entries with `offset` and `nextOffset`;
`archidekt_push_preview` and `archidekt_pull_preview` lead with a summary (operations by kind,
differences, conflicts) and page with `operationOffset` and `differenceOffset`; the guards and the
preview fingerprint cover the whole preview, so the apply tool needs no page.
`archidekt_sync_diff` (`differenceOffset`), `archidekt_snapshot_restore_preview`
(`operationOffset`, `differenceOffset`), `archidekt_folders_get` (`offset`), and
`archidekt_snapshots_get` with `kind` `get` (`offset`, over the saved deck) page
the same way. When `truncated` is true, call again with the next offset in
`narrow`. A preview with two lists (operations and differences) has one offset for each; give
the later pages `expectedPreviewFingerprint`, the `previewFingerprint` of the first page, so a
preview that changed in between is refused instead of paged out of line.
`archidekt_deck_get` leaves out unknown provider fields over 8,000 bytes and sets
`extensionsOmitted`; they cannot be read through this tool.

`archidekt_deck_get` and `archidekt_snapshots_get` (kind `get`) take:

- `zones`: any of `commander`, `main`, `sideboard`, `maybeboard` (the names `card_list_query` uses). Omit it to read
  every zone. `offset` and `nextOffset` count the chosen entries, and `shownEntryCount` is how many the zones hold; a
  bad offset is refused with `invalid-offset` naming that count.
- `responseFormat`: `concise` (the default) returns `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`,
  `collectorNumber`, `oracleId`, and `inDeck`. `full` (and `standard`, the same) returns every entry field (provider
  IDs, `finish`, `language`, `sortOrder`, and the rest) plus `inDeck`. A cut page's `narrow` says to pass
  `responseFormat: "full"` for every field.

Entries come ordered by zone (commander, main, sideboard, maybeboard), then card name, then relation ID, so pages of
separate reads line up. `inDeck` is false when the entry's first category is not included in the deck on Archidekt
(`includedInDeck: false`); Archidekt does not count such an entry toward the deck, and this server reads it into the
`maybeboard` zone. The leading summary describes the whole deck whatever `zones` asks for: entry, card, and category
counts, `cardsByZone`, and facts that need no verdict:

- `duplicates`: cards held more than once across commander and main, matched by Oracle ID (by name when there is none).
  Basic lands and entries that are not in the deck are skipped. The list is capped, and `duplicatesOmitted` counts
  the groups left out.
- `repeatsOutsideMain`: cards held more than once inside the sideboard or maybeboard (for example the same card four
  times in the sideboard), as separate groups, with `repeatsOutsideMainOmitted` when capped.
- `size`, for a Commander deck only: `format`, `expected` (100), and `mainAndCommander` (the cards in the commander
  and main zones that are in the deck). It is a fact about the count, not a statement that the deck is right.

No card data is needed for any of this. `archidekt_snapshots_get` with kind `get` returns the saved deck in the same
shape and takes the same two arguments.

When `archidekt_push_apply` is not available (the mode is below `remote`, or
credentials are missing), the push preview says so in `applyNotice`, with
`applyAvailable: false`, and tells the agent what to ask the user: start the server in
`remote` mode (the default), or set up Archidekt credentials. The pull preview does the same for
`archidekt_pull_apply` based on the mode alone (local or remote mode is enough): a pull reads a public deck without
credentials, and a private deck also needs them.

Archidekt may renumber category positions and the order of an entry's
secondary categories. Content equality therefore compares category identity,
membership, flags, and primary assignment, not the provider-controlled numeric
rank or the order of secondary categories.

After a verified push, the new baseline links each entry and category the push
created to the row Archidekt assigned, so the next preview plans only real
changes. If a created entry cannot be matched exactly, `archidekt_push_apply`
lists it in `unlinkedLocalIds`; the next push removes and re-adds that row
instead of duplicating it. If the verification finds a difference
(`verification-mismatch`), the baseline still records the verified remote state
and links created entries, but the local deck is not marked synchronized: what
Archidekt did not take stays visible as a local change: a pull keeps leftover additions and conflicts on any other
leftover change, and the next push plans only that difference.

Category names are case-sensitive, as on Archidekt: `Sideboard` and `SIDEBOARD`
are two categories. A push compares names exactly. Archidekt keeps no card zone
of its own; it comes from the categories (a category named Commander, Sideboard
or Maybeboard, or the commander category; otherwise main). An entry whose primary (first) category has
`includedInDeck: false` is not in the deck on Archidekt, so it is read into `maybeboard` instead of `main`; a
category with no flag counts as included. Pulls, deck reads, and the push check all use this one rule, and all
look at the primary category first. A deck pulled by `0.13.0` still reads as unchanged: the next pull shows each
such entry's move to `maybeboard` as an ordinary change from Archidekt, a push before that pull plans nothing for
it and writes the zone `maybeboard` into those local entries, and a card added locally before the pull does not
make those entries show as local changes. A push that would send a card whose zone differs from that is refused
with `entry-zone-contradicts-category`, naming the cards.

A difference where the local deck and Archidekt changed the same field to the
same value is `converged`: no conflict, no operation, and the next apply
advances the baseline. Entry order numbers (`sortOrder`) and category rank are
not compared as remote changes, because Archidekt renumbers them; a local edit
to them, or to a category color, still counts as a local change.

A push adds a card by name with no card data and no printing set to Archidekt's first listed printing of that
name: the first exact-name match in Archidekt's card search. The adapter finds a printing with one search per
distinct name and set, asks Archidekt for an exact name match (a plain name search also lists longer names that
contain it, such as Aftershock for Shock), and follows result pages within the 150-request budget, at most 12
pages (300 printings) for one search. An entry with a set code is searched within that set (Archidekt's
`edition` filter), so a printing of a card with hundreds of printings, such as a basic land, is found. It looks
for the entry's printing ID first, then its set code and collector number (the set code is Archidekt's
`edition.editioncode`; a set code without a collector number takes the first printing in that set), and an
entry with no printing takes the first listed match, narrowed by Oracle ID when the entry has one. An entry that
names a printing ID that Archidekt does not list stays unresolved; it never takes the first printing instead.
`archidekt_push_preview` does the same search, so the preview and the apply agree: each add's `summary` names
the printing it will use (the form is "Archidekt printing SET NUMBER (first of 18 Archidekt printings of this
name; no printing was set)"). A name-only add for a foil or etched entry takes the first listed printing, even
when that printing lacks the finish. The searches make a preview slower (about two seconds per distinct name).
When a name has no match in Archidekt, when an entry's own printing (its printing ID or set code) is not on
Archidekt, or when the lookups would pass the 150-request budget, the preview sets `applyAvailable: false` and
its `applyNotice` names the cards (or the limit); fix the name, or clear or correct the printing on the entry,
then preview again. `archidekt_push_apply` resolves every add before its first write, so a card it cannot place
refuses the whole push with no writes.

A push removes every Archidekt entry the local deck lacks. A push that removes more than 10 entries, or 3 or more
entries that are more than 10 percent of the Archidekt entries, is refused until `confirmRemovals` (on both
`archidekt_push_preview` and `archidekt_push_apply`) equals the exact removal count; the preview's `applyNotice`
names the count and `applyAvailable` is false until then. A local deck that holds only part of the Archidekt deck
would otherwise delete the rest. Run `archidekt_pull_preview` first when that is not what you mean. Removing one or
two entries needs no confirmation, even from a small deck, and neither does removing up to 10 entries that are no
more than a tenth of the deck. The apply checks this, and that the local and Archidekt decks are the ones
previewed, before it searches for any printing, so a refused apply spends no search requests.

If Archidekt changed since the last sync, `archidekt_push_preview` sets `hasConflicts`, lists no operations, and
sets `applyAvailable: false` with an `applyNotice` that says `archidekt_push_apply` would refuse the push (the
count of changed rows; see the differences with `remote-*` states). The mode and credentials notice, when it applies,
comes first. A push of such a deck would lay the local deck over Archidekt and undo the Archidekt edits, so
`archidekt_push_apply` returns `push-conflict` with no writes. Run `archidekt_pull_preview` first.

A pull keeps what you added locally. When every local change since the last sync is an added card or category, the
pull preview is not a conflict. It shows a `local-keep` operation ("Keep N cards added locally since the last sync;
push them afterwards."), and `archidekt_pull_apply` replaces the rest from Archidekt, then adds the kept cards, their
categories (created by exact name when missing; a card in a category Archidekt renamed moves to the new
name), and their category assignments. The new baseline leaves the kept
cards as `local-added`, so the next push adds exactly those. A pull is refused (`pull-conflict`, with the reason in
the preview's `applyNotice`) when a kept card is a card Archidekt also added, which would leave two entries for the
card, or when a kept card's categories would put it in another zone. Any other local change (a card changed or
removed, a category changed) still needs `discardLocalChanges`.

To take the Archidekt state over every local change, call `archidekt_pull_preview` with `discardLocalChanges` set to
true and `localDeckId`, then `archidekt_pull_apply` with the same flag in the request. The preview says what is
thrown away: "N cards added, N changed, N removed (categories: N added, N changed, N removed)", and lists the rows
with `local-*` states in `differences`. Without the flag nothing is discarded, and the flag needs an existing local
deck (`discard-needs-local-deck`). If Archidekt changed too, a push is refused as well, so `discardLocalChanges` is the
way out when a pull cannot keep your changes; it drops the local edits, including cards added locally.

How a push reports what happened (`archidekt_push_apply`, field `outcome` and the status of each operation):

- `applied`: every operation was sent and the check afterwards matched.
- `not-applied`: nothing was applied. An operation that stopped the push before anything was sent is `not-sent`
  ("Nothing was sent to Archidekt for this operation."), and the operations after it are `not-attempted`. A
  `429` on a write is `not-sent` (Archidekt did not process it) and is never retried automatically.
- `partial`: some operations were applied, or one failed after its write was sent (`unknown`: it may or may not
  have been applied). The rest are `not-sent` or `not-attempted`.
- `applied-unverified`: the writes were sent, but Archidekt could not be read afterwards (for example it returned
  `429`). The applied statuses are kept, the local baseline is not updated, and the `notice` says to run
  `archidekt_pull_preview` before pushing again.
- `verification-mismatch`: the check after the writes found a difference. It reads once more after about a second
  (Archidekt can lag behind its own writes), and `differences` lists the rows that still differ (up to 25).

Reads that get a `429` wait for `Retry-After` (at most about 10 seconds) and retry up to twice.

A deck whose synchronization baseline an earlier version recorded did not store
category colors or ordering, so the server cannot tell whether they changed. When
such a deck differs from its baseline with no row to show why, the diff has a
`/localOnly/content` row with state `local-changed-unverified`. A pull of it is
refused until `discardLocalChanges` is set; push keeps the local content and is
not blocked by the row (but is refused if Archidekt also changed). Either one
writes a current baseline, and the row is gone. A deck 0.10.0 pulled or pushed that you have
not edited since is read as unchanged, so it gets no row.

## Read Playgroup evidence

The `playgroup` toolset turns on when a Playgroup key is present. It exposes tools
for the pinned Public API 1.0.0.
All modes include 15 safe read tools. `remote` adds two write tools.

Results preserve provider JSON and include operation, API version, retrieval
time, checksums, and limitations. The adapter does not rank decks or infer
quality.

Request starts are at least 250 milliseconds apart. Reads have bounded retry.
Writes are single-attempt. Live acceptance does not invoke writes because the
public API provides no cleanup operation.

## Start a workflow with a prompt

The server offers three MCP prompts. A prompt is a ready-made first message that
lists the steps of a common workflow. It costs no model context until you run
it, and it carries IDs only, never free text, so a client that splits prompt
arguments on spaces still works.

| Prompt | Arguments | What the agent is asked to do |
| --- | --- | --- |
| `audit-deck` | `deckId` | Read the deck; report Commander rule facts, curve (without lands), pips, color sources, castability, and bracket facts; then ask what to change. |
| `build-from-commander` | `scryfallId` | Read the commander; ask for budget, bracket, and plan; search by tags; build a local deck in batches. |
| `compare-decks` | `deckIdA`, `deckIdB` | Run `card_lists_compare` and `card_list_query` on both decks and report the differences. |

Each argument is a UUID (the local deck ID, or the Scryfall card ID of the
commander). A missing or different value is refused with an invalid-params
error that names the argument. To get an ID, ask the agent to list your local
decks (`local_deck_list`) or to look up a card by its exact name
(`scryfall_cards_get`), then copy the ID from the result.

In Claude Code, MCP prompts appear as slash commands named
`/mcp__<server>__<prompt>`, where `<server>` is the name you gave the server
when you added it. With a server named `mtg-mcp`:

```text
/mcp__mtg-mcp__audit-deck 0193e97a-ef95-7700-8000-000000000001
/mcp__mtg-mcp__compare-decks 0193e97a-ef95-7700-8000-000000000001 0193e97a-ef95-7700-8000-000000000002
/mcp__mtg-mcp__build-from-commander 4a2c8f1e-6b1d-4e35-9c0e-5f2f3a8d7b10
```

Other clients list them under their own prompt or command menu.

The prompts name only tools of the default selection that needs no provider
credentials (the `decks`, `scryfall`, `stats`, and `spellbook` toolsets).
Prompts are always offered, because registration is fixed for the session and
a prompt costs nothing to list. When the session hides a named tool (a
narrower `--toolsets`, or `read-only` mode, which hides write tools such as
`local_deck_create`, `local_deck_apply_changes`, and
`scryfall_card_data_sync`), the prompt tells the agent to say which step it
skipped and carry on. Every prompt says that the user and agent make every
judgment; the server only supplies facts.

## Errors

A failed tool call sets the MCP `isError` flag and keeps the structured body
(`kind`, `reasonCode`, `message`). Check `isError` first, then read `kind`.
A call whose arguments cannot be read returns `kind: "invalid-input"`, reason
`invalid-arguments`, and a top-level `path` to the rejected value, for example
`changes[0].entryDraft.quantity`. A union's `kind` property (such as a deck
change's `add-entry`) may appear anywhere in its object. A `conflict` caused by
stale state includes `current`, the current local revision, remote fingerprint,
or both, to preview again against.

Every tool rejects an argument it does not declare, including a misspelled field
inside a nested object such as an entry draft, with `invalid-input` and the
field name in `path`. Input schemas set `additionalProperties: false`.

Text written by other people (Archidekt deck and category names, Playgroup names
and notes, Commander Spellbook descriptions) is untrusted data. The tools that
return it say so in their descriptions, and the server never acts on it. The
destructive Archidekt remote writes ask Claude Code to confirm each call.

## Evidence and safety

The server labels these kinds of result separately:

- provider facts;
- provider evidence;
- exact derivations;
- parser classifications;
- heuristics; and
- sampled estimates.

Stable `0.9.0` contains no advisor prompts, intent inference, weak-card
judgments, replacement recommendations, blended quality scores, or strategic
simulation. The three workflow prompts only list tool steps and ask the user
for decisions; they hold no recommendation logic. Deferred work is listed in [Potential Features](docs/potential-features.md).

## Develop and verify

Use Task as the command menu:

```bash
task --list
task lint
task test
task benchmark:simulation
task surface:report
task coverage
task pack
task smoke:process
task smoke:mcp
task release:tool-smoke
```

Normal tests are deterministic and offline. Live tests require explicit opt-in.
A test calls every tool in `remote` mode against a 300-entry deck, a 300-card Archidekt deck, and
large card fixtures, and fails when any result passes 50,000 characters, the point at which Claude Code
stops showing a result inline, or when a tool that should page never pages. A tool that cannot be driven
offline (Playgroup, Commander Spellbook, and some Archidekt writes) is listed in the test with the reason.
`task benchmark:simulation` runs the separate fixed 99-card Release measurement.
Each production assembly must maintain at least 90 percent line coverage.

## Architecture

| Project | Responsible for |
| --- | --- |
| `MtgMcp.Core` | Provider-neutral contracts and evidence |
| `MtgMcp.Decks` | Local SQLite decks and interchange |
| `MtgMcp.Scryfall` | Official transport, card data, snapshots, and pacing |
| `MtgMcp.Archidekt` | Observed provider contract and synchronization |
| `MtgMcp.Playgroup` | Pinned official API evidence |
| `MtgMcp.Spellbook` | Bounded Commander Spellbook source evidence and cache |
| `MtgMcp.Statistics` | BCL-only exact calculations |
| `MtgMcp.Simulation` | Pure seeded sampling engine behind `stats_simulate` and its on-curve preset |
| `MtgMcp.App` | MCP host, configuration, composition, and schemas |

Read [North Star](docs/north-star.md), [Design Goals](docs/design-goals.md), and
the [rewrite guide](docs/rewrite-guide.md) for durable constraints.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
