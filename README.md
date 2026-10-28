# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` gives an LLM grounded Magic: The Gathering card, deck, provider, and
statistical evidence. The LLM makes deckbuilding decisions.

The `0.11.0` tool surface builds on the `0.9.0` stdio server design. Every toolset is
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
| Local decks and interchange | 20 | Yes | Local |
| Scryfall evidence | 15 | Yes | Local cache and card data |
| Exact statistics | 9 | Yes | No |
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

`remote` is the default. Use `--mode=local` to hide Archidekt and Playgroup writes, or
`--mode=read-only` to hide every write. A Scryfall cache miss can need a local write to record
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
- exact Scryfall identity reconciliation;
- tag-rule grouping with repeatable results; and
- on-curve estimates based on card data and caller-supplied land rules.

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
is cut to stay under 40,000 bytes and then sets `truncated`, `narrow`, and
`nextCursor`. `fields` narrows the card fields; it uses the same names as the
Scryfall card tools.

Use `local_deck_category_rules_preview` and `local_deck_category_rules_apply` to
evaluate caller-owned tag rules. The preview validates the rules first; rules
are always inline, and the MCP does not decide what a category means. See
[Category rules without a preset](#category-rules-without-a-preset) for what
replaced the removed `common-v1` preset.

### Estimate mana on curve

Use `local_deck_on_curve_estimate` to sample how often one mainboard card can be cast
by a chosen turn. Supply the saved deck ID and current revision, the target
entry ID, one rule for each eligible land, a turn limit, and optionally a seed
and sample count. The result includes the exact installed Scryfall facts used,
the caller rules, a replay seed, a 95% Wilson interval, miss counts, and up to
two sample traces.

Version 1 models only simple printed costs and caller-described single-faced
lands. It does not read card text, infer land behavior, model other mana
sources, choose plays, rate the deck, or predict real games.

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

## Query Scryfall

The `scryfall` toolset supports search, card lookup (`scryfall_cards_get`, one or
many cards), printings and rulings (`scryfall_card_details`), sets, catalogs, and
bulk metadata (`scryfall_reference_get`), autocomplete, tag evidence,
immutable search snapshots (`scryfall_search_snapshots_get`), and explicit
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
| `standard` (default) | Every card field this server returns. New fields are added, never removed. A page of 25 rows can pass 40,000 bytes: use `concise` or a smaller `pageSize`. |
| `concise` | A small set of fields for comparing many cards: IDs, name, cost, type, colors, color identity, produced mana, keywords, oracle text, power, toughness, loyalty, the formats the card is legal in, USD price, cheapest USD price among installed English printings, rarity, set, oracle tag slugs, Game Changer flag, and EDHREC rank. `fields` picks other names, including `prices.usdFoil`, `prices.usdEtched`, `prices.eur`, `prices.eurFoil`, `prices.tix`, `legalitiesAll`, `games`, `reserved`, and `layout`. |
| `full` | `standard` plus the raw provider objects. Pages are limited to 25 rows. |

In `concise`, `tags` and `cheapestPriceUsd` come from installed card data and are
`null`, with the reason in `limits`, when it is not installed. A `concise` page
is cut to stay under 40,000 bytes and then sets `truncated`, `narrow`, and
`nextCursor`. `includeRaw` was replaced by `responseFormat: "full"` on these
three tools.

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
`preset` now returns `invalid-input`. The preset mapped four role names to four
official Scryfall oracle tags, each with its descendant tags and the minimum
weight `weak`. The same rules sent inline are one rule per role, with the ID of a
category that exists in the deck in `categoryId`, the tag's ID in `tagId`, and
`includeDescendants` on. If you set a `primaryPriority` on a preset binding, set
it on the matching inline rule. The four tag IDs below are the ones `common-v1`
used:

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

The `stats` toolset provides nine read-only tools for:

- univariate and multivariate hypergeometric probability;
- probability-by-turn tables;
- caller-defined mana-source availability;
- package and combo assembly;
- explicit mulligan schedules;
- minimum-copy and minimum-source solving;
- deterministic deck composition summaries; and
- exact on-curve castability of each spell in a deck (`stats_castability`).

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
It stops on `403` and `429`. Ambiguous mutations are not retried.

Archidekt may renumber category positions and the order of an entry's
secondary categories. Content equality therefore compares category identity,
membership, flags, and primary assignment, not the provider-controlled numeric
rank or the order of secondary categories.

After a verified push, the new baseline links each entry and category the push
created to the row Archidekt assigned, so the next preview plans only real
changes. If a created entry cannot be matched exactly, `archidekt_push_apply`
lists it in `unlinkedLocalIds`; the next push removes and re-adds that row
instead of duplicating it.

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
a prompt costs nothing to list. When the session hides a named tool (a narrower
`--toolsets`, or `read-only` mode, which hides write tools such as
`local_deck_create`, `local_deck_apply_changes`, and `scryfall_card_data_sync`), the prompt
tells the agent to say which step it skipped and carry on. Every prompt says
that the user and agent make every judgment; the server only supplies facts.

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
task benchmark:oncurve
task surface:report
task coverage
task pack
task smoke:process
task smoke:mcp
task release:tool-smoke
```

Normal tests are deterministic and offline. Live tests require explicit opt-in.
`task benchmark:oncurve` runs the separate fixed 99-card Release measurement.
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
| `MtgMcp.OnCurve` | Pure repeatable sampled cast-by-turn calculation |
| `MtgMcp.App` | MCP host, configuration, composition, and schemas |

Read [North Star](docs/north-star.md), [Design Goals](docs/design-goals.md), and
the [rewrite guide](docs/rewrite-guide.md) for durable constraints.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
