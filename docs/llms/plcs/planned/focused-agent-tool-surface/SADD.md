# Design: Focused Agent Tool Surface

## Chosen Design in Brief

Keep the current architecture. Toolset manifests in `MtgMcp.App` already list
the exact tool names for each toolset (for example
`src/MtgMcp.App/Statistics/StatisticsToolsetManifest.cs:40-55`). This packet
changes those lists, merges a small number of tool methods, adds typed choice
parameters, adds one card projection with more card facts, adds a card view
to `deck_get`, adds two tools that compare and analyze card lists by
reference, adds Commander rule facts to `deck_validate`, and turns the on-curve
sampler into a general sampler that runs caller-stated rules.

No new layer, plugin system, or tool router is added.

**Names in this packet.** The design text uses today's working names (for
example `deck_get`, `scryfall_corpus_sync`) so it can be read against current
code. Phase 3 renames tools to the final names in
[FIXTURES](FIXTURES.md#final-tool-names). From phase 3 on, all code, tests,
fixtures, descriptions, and error messages use the final names.

## Main Workflow

The surface is shaped around one flow. Each step names the tool an agent uses.

| Step | Tool | Toolset |
| --- | --- | --- |
| Find cards by kind, including tags | `scryfall_search` with Scryfall syntax such as `otag:ramp t:sorcery mv<=2 id<=g f:commander` | `scryfall` |
| Read full details for a few cards | `scryfall_cards_get`, `scryfall_card_details` | `scryfall` |
| See a deck's cards with fields and tags | `deck_get` with `include: ["cards", "tags"]` | `decks` |
| Combine or compare card lists, such as "search results not already in my deck" | `card_lists_compare` | `scryfall` |
| Curve, types, role counts, pips, sources, prices, sorted card tables | `card_list_query` | `scryfall` |
| Commander size, identity, singleton, legality, Game Changer facts | `deck_validate` with `rules: "commander"` | `decks` |
| Run exact math or sampling on a deck | `stats_*`, `stats_simulate` | `stats` |
| Build or edit a local deck | `deck_create`, `deck_apply_changes` | `decks` |
| Download from Archidekt | `archidekt_deck_list`, `archidekt_pull_preview`, `archidekt_pull_apply` | `archidekt` |
| Upload to Archidekt | `archidekt_deck_create` (empty shell, binds the local deck), then `archidekt_push_preview`, `archidekt_push_apply` | `archidekt` |

Archidekt uploads always go through a local deck: `archidekt_deck_create`
creates an empty shell, and "contents require a separate push preview/apply"
(`src/MtgMcp.App/Archidekt/ArchidektRemoteWriteTools.cs:37`). Deck writes
and Archidekt tools are both on by default when Archidekt credentials are
configured (see [Toolsets and Modes](#toolsets-and-modes)), so the round trip needs no
configuration beyond the credentials.

## Merge Rule

Two tools may become one only when all of these hold:

1. They have the same `ReadOnly`, `Destructive`, `Idempotent`, and
   `OpenWorld` values.
2. They are visible in the same operation modes.
3. One required input field picks what the call does, such as `kind` or
   `by`. Other fields are required or allowed only for the chosen value, and
   the schema says so. No optional field silently changes the behavior.
4. The result is one type for the whole tool: either the same shape for every
   choice, or a union with a `kind` field that matches the input choice.

Reason: clients use annotations to decide when to ask the user, and operation
modes filter whole tools. A tool that both lists backups and deletes them
would need `Destructive = true` and would disappear in `read-only` mode.
Rules 3 and 4 keep merged tools easy to call and easy to read: the agent
always says which job it wants, and the result says which job it did.

SURF-003 checks all four rules for every merged tool: equal annotations and
modes, one required choice field with conditional fields, and a result union
keyed the same way.

Preview/apply pairs stay separate for the same reason.

## Toolsets and Modes

Owner decisions on 2026-10-25: every toolset is on by default, toolsets that
need credentials are off until their credentials are configured, every tool
is a deckbuilding tool (there is no admin toolset), and read-only is an
opt-in mode.

| Toolset | On by default | Purpose |
| --- | --- | --- |
| `scryfall` | Yes | Card search, lookup, details, tags, card list comparison and query, reference data, and installed card data upkeep. |
| `stats` | Yes | Exact calculations and `stats_simulate`. |
| `decks` | Yes | Local decks: read, check, export, build, edit, back up, import, categorize, and reconcile. |
| `spellbook` | Yes | Commander Spellbook combo data. Needs no credentials. |
| `archidekt` | When Archidekt credentials are configured | Archidekt decks, folders, snapshots, and sync. |
| `playgroup` | When a Playgroup API key is configured | Frozen. No changes in this packet. |

The toolsets are the same six as today. No toolset is added or removed.

**Credential gating.** At startup, the server checks the credential sources
the configuration loader already reads
(`src/MtgMcp.App/Configuration/FoundationConfigurationLoader.cs:173-219`). It
checks only that credentials are present, not that they work; it does not
call the provider and does not read secret values into logs.

| Toolset | Counts as present when any of these holds |
| --- | --- |
| `archidekt` | Both `ARCHIDEKT:USERNAME` and `ARCHIDEKT:PASSWORD` are non-empty; or `ARCHIDEKT:CREDENTIALS_FILE` names a file that exists; or `~/.mtg-mcp/archidekt.json` exists. |
| `playgroup` | `PLAYGROUP:API_KEY` is non-empty; or `PLAYGROUP:CREDENTIALS_FILE` names a file that exists; or `~/.mtg-mcp/playgroup.json` exists. |

Only a username, or only a password, does not count. A file counts if it
exists, even if its contents are wrong; a bad file is reported when a tool
first loads it, as today, and the `*_auth_status` tools show the state.

A toolset without credentials is off, whether it was selected by default or
by name. The capabilities resource reports it as off with the reason
`credentials-missing`, and the server logs one line to stderr naming the
settings above. Today an invalid Playgroup configuration fails startup
(`FoundationConfigurationLoader.cs:191-196`); that stays true only when
`playgroup` is named in `--toolsets`. Under the default selection the toolset
is turned off with the reason `credentials-invalid` instead, so one bad
setting does not stop the whole server. Credentials added later take effect
on the next start, because the tool list is fixed for a session.

**Selection.** With no `--toolsets` option, the selection is every toolset
whose credentials are present. An explicit list still narrows it. `all` means
the same as the default. Unknown names still fail at startup.

**Modes.** The default mode becomes `remote`, so every write tool is visible,
including Archidekt uploads. `read-only` hides every write tool; `local` hides
only provider writes. Both stay available through `--mode`. Destructive tools
keep `Destructive = true`, so clients still ask before running them.

`scryfall_corpus_rollback`, `scryfall_corpus_delete`, and
`scryfall_snapshot_delete` stay in `scryfall`; they manage the installed card
data that card tools read.

### Alternatives Considered

- **Smaller default with opt-in toolsets.** Saves context and reduces wrong
  tool picks, but the owner judged context cost acceptable and asked for clear
  names instead (see [Tool Names](#tool-names)). Rejected.
- **Split `deck-edit` and `scryfall-admin` toolsets.** Read-only mode already
  hides writes, so the split added a concept without adding control.
  Rejected.
- **Turn on provider toolsets without credentials.** Their tools would only
  return "not configured". Rejected.
- **Check credentials by calling the provider at startup.** Slower startup and
  network use before any request; the existing `*_auth_status` tools already
  do this on demand. Rejected.
- **Rename `scryfall` to `cards`.** Breaks saved configurations for little
  gain. Rejected.
- **Dynamic toolsets that the agent turns on at run time.** Saves context, but
  needs list-changed notifications and client support that the repo
  deliberately avoids. Rejected for now.
- **Expose reference data only as MCP resources.** Client support for reading
  resources varies, so an agent might lose access. Rejected; tools stay the
  main path.

## Tool Names

With every toolset on, several tools work on similar things: a local deck, an
Archidekt deck, and a Playgroup deck. Names must say which one.

**Naming rule.** `<source>_<object>_<action>`, all lower case with
underscores:

| Prefix | Means |
| --- | --- |
| `local_deck_` | The local deck store on this machine. |
| `scryfall_` | Scryfall card data, live or installed. |
| `card_` | Any card list given by reference (search results, decks, snapshots). |
| `stats_` | Exact calculation or sampling. |
| `archidekt_` | The user's Archidekt account. |
| `playgroup_` | Playgroup (frozen; names unchanged). |
| `spellbook_` | Commander Spellbook. |

The action is a plain verb such as `get`, `list`, `search`, `create`,
`update`, `delete`, `preview`, `apply`, `compare`, `query`, `sync`, or
`status`. Names avoid internal words such as "corpus". Each description's
first words name the source, for example "Local deck store: ..." or
"Archidekt: ...".

**Renames.** Phase 3 applies these, in the same breaking release as the
merges. The rest of this packet uses the working names; the full old-to-final
list is in [FIXTURES](FIXTURES.md#final-tool-names).

- `deck_*` → `local_deck_*`, so `deck_get` cannot be mistaken for
  `archidekt_deck_get` or `playgroup_deck_get`.
- `stats_deck_summary` → `stats_local_deck_summary`, because it reads only
  local decks.
- `scryfall_corpus_*` → `scryfall_card_data_*` ("corpus" is internal jargon
  for the installed card data).
- `scryfall_tag_search` → `scryfall_tags_search` and `scryfall_cards_by_tag` →
  `scryfall_installed_cards_by_tag`, so the offline tool says it is offline.
- `scryfall_snapshot_delete` → `scryfall_search_snapshot_delete`, matching
  `scryfall_search_snapshots_get`.

Playgroup names such as `playgroup_playgroup_games_list` repeat a word, but
Playgroup is frozen, so they stay.

An architecture test checks every name against the prefix list and a 64
character limit, and checks that no two tools share a name after their prefix
is removed unless their descriptions name different sources.

## Card Search and Tags

`scryfall_search` already sends the query to Scryfall unchanged
(`src/MtgMcp.App/Scryfall/ScryfallReadTools.cs:41`), so Scryfall's own
`otag:` (oracle tag) and `atag:` (art tag) filters work today. This packet
makes that the documented main path:

- The `scryfall_search` description states that tag, type, color identity,
  mana value, format, and price filters all go in `query`, with two short
  examples.
- `scryfall_tag_search` is described as "find the exact tag slug to use in
  `otag:`", which is its useful job for an agent.
- `scryfall_cards_by_tag` is described as the offline path over installed card
  data, for when Scryfall is unreachable or a stable snapshot is wanted.

Sorting stays Scryfall's job (`order`, `direction`). The server does not add
its own ranking.

## Deck Card View

`deck_get` gains an optional `include` list:

| Value | Adds per entry |
| --- | --- |
| `cards` | The `concise` card fields for the entry's exact printing, from installed card data. |
| `tags` | Official oracle tag slugs for the entry's card, from installed card data. |

If card data is not installed, the entry carries `cardData: "not-installed"`
and the result points to `scryfall_corpus_sync`. Entries without an exact
Scryfall identity carry `cardData: "unresolved"`. The server reports these
states; it does not guess a card.

This gives the agent one call for "my deck's cards with their fields and
tags" next to the entry IDs it needs for edits.

**Job split between similar tools.** Each description says which tool to use:

| Tool | Job |
| --- | --- |
| `deck_get` | The deck's stored state: entries, zones, categories, revision, and the IDs needed to edit it. `include` adds card fields for convenience. |
| `card_list_query` | Analysis of any card list: filter, group, count, sum, sort, pips, and sources. |
| `stats_deck_summary` | Exact counts by zone, category, and printing, plus numbers the caller supplies, without reading card data. Its description points to `card_list_query` for card-data analysis. |

`DeckCategoryRuleResolver` already reads installed tag data from App, so this
uses the same `ScryfallService` access and adds no new project dependency.

## Card List Comparison

`card_lists_compare` applies one exact set operation to two card lists. The
agent passes references to lists, not the lists themselves, so large results
never pass through the agent's context.

This works because the server already holds the lists: every
`scryfall_search` fetches all provider pages and returns a snapshot
reference with its page (`src/MtgMcp.Scryfall/ScryfallModels.cs:183-186`),
and local decks are addressed by ID and revision.

### Request

```json
{
  "left":  { "kind": "scryfall-snapshot", "snapshotId": "..." },
  "right": { "kind": "archidekt-deck", "archidektDeckId": "123456", "zones": ["main", "commander"] },
  "operation": "difference",
  "matchBy": "oracle",
  "responseFormat": "concise",
  "pageSize": 50
}
```

| Field | Values |
| --- | --- |
| `left`, `right` | A list source (below). |
| `operation` | `union`, `intersection`, `difference` (left minus right), `symmetric-difference` (in exactly one list). |
| `matchBy` | `oracle` (same card, any printing; default), `printing` (exact printing), `name` (exact normalized name). |
| `responseFormat`, `fields` | As for card results; default `standard`. |
| `cursor`, `pageSize` | Paging, 1 to 100, default 25. |

### List Sources

`card_lists_compare` and `card_list_query` share one list source type and one
resolver in App, so a new source kind works in both tools.

| Kind | Input | Owning toolset | Notes |
| --- | --- | --- | --- |
| `scryfall-snapshot` | `snapshotId` | `scryfall` | The full saved result of a search. Immutable. |
| `cards` | 1 to 150 Scryfall IDs or Oracle IDs | `scryfall` | A short list the agent already has. |
| `deck` | `deckId`, optional `expectedRevision`, optional `zones` | `decks` | A local deck. With `expectedRevision`, a different revision returns `conflict` with the current one. Without it, the current revision is used and echoed in the result. |
| `deck-backup` | `backupId`, `deckId`, optional `zones` | `decks` | One deck as it was in a local backup. See [Reading Backups](#reading-backups). |
| `archidekt-deck` | `archidektDeckId` (string), optional `zones` | `archidekt` | Read live from Archidekt through the existing adapter and pacing. |
| `archidekt-snapshot` | `archidektDeckId` (string), `snapshotId`, optional `zones` | `archidekt` | One saved Archidekt snapshot. Fixed, so no state marker is needed. |
| `compare` | A nested `card_lists_compare` request without paging fields | — | Chains operations, such as (A ∪ B) − deck. At most 3 levels deep and at most 300 `cards` IDs in the whole request. |

**One rule for every source:** a source is accepted only when its owning
toolset is on. Otherwise the result is `invalid-input` with reason
`toolset-not-selected` (the user left it out of `--toolsets`) or
`credentials-missing` (the toolset is gated off). This keeps
`--toolsets scryfall` from reading decks or Archidekt through the list tools.

### Reading Backups

A backup is a full copy of `decks.db`. The resolver opens the backup file
read-only and never runs migrations on it. It reads the backup's schema
version first:

- Same version as the live database: load the one deck.
- Older or newer version: return `unsupported` with reason
  `backup-schema-mismatch`, naming both versions. Supporting old versions is
  later work.
- Deck not in the backup: `not-found` with reason `deck-not-in-backup`.

The live database is never opened for writing by this path.

### Result

```json
{
  "counts": { "left": 812, "right": 99, "result": 761, "unmatched": 2 },
  "page": { "items": [], "nextCursor": "..." },
  "unmatched": [ { "side": "right", "entryId": "...", "name": "Custom Proxy", "reason": "no-scryfall-identity" } ],
  "sources": [
    { "side": "left", "kind": "scryfall-snapshot", "snapshotId": "...", "checksum": "..." },
    { "side": "right", "kind": "archidekt-deck", "archidektDeckId": "123456", "stateMarker": "..." }
  ],
  "fingerprint": "..."
}
```

### Rules

- Results are ordered by card name, then Oracle ID, so pages are stable.
- Each result card appears once. With `matchBy: "printing"` the printing is
  kept; with `oracle` or `name`, the result card comes from the left list when
  present, otherwise the right.
- Entries that cannot be matched, such as a deck entry with no Scryfall
  identity, are listed in `unmatched` and never guessed.
- Nothing is stored. To get the next page, the agent sends the same request
  again with `cursor`. The cursor is small: the request fingerprint, the page
  position, and each source's checksum, revision, or Archidekt state marker.
  The server recomputes the fingerprint from the resent request and rejects
  the cursor with `invalid-input` if it does not match, so an edited cursor
  cannot change the query. If an Archidekt deck changed between pages, the
  next page returns `conflict` with the new state marker. This avoids a new
  snapshot kind and any storage format change.
- Each page re-reads its sources. Local sources are cheap. Each Archidekt
  source costs one paced provider read per page, so descriptions suggest
  `pageSize` 100 for Archidekt sources, and `card_list_query` without `rows`
  needs only one page.
- The tool is read-only and idempotent. It is marked `OpenWorld = true`
  because the `archidekt-deck` source reads a provider.

### Placement

The tool lives in `MtgMcp.App` because it coordinates Scryfall, the deck
store, and Archidekt. The set operation itself is a pure function over
`(key, card)` lists with no I/O, so it is tested without infrastructure. It
belongs to the `scryfall` toolset because every result needs Scryfall card
data, even when the lists come from decks. The `card_` prefix says it works
on any card list. Deck and Archidekt sources still need their own toolsets on
(see the source rule above).

### Alternatives Considered

- **Pass full card lists in the request.** Simple, but the agent must copy
  hundreds of cards back into a call. Kept only as the small `cards` source.
- **Save each result as a new snapshot.** Allows chaining by ID, but adds a
  snapshot kind and a storage change. Nested `compare` sources give the same
  chaining with no stored state.
- **Four tools, one per operation.** Breaks the one-tool-per-job rule; the
  `operation` enum is clearer.
- **An `excludeDeckId` option on `scryfall_search`.** Covers one case only;
  the general tool covers it and more.
- **Keep a revision history table in `decks.db`.** Allows "compare with any
  earlier revision", but changes the storage format. Backups and Archidekt
  snapshots already hold earlier states, so they become list sources instead.

## Card List Query

`card_list_query` filters, groups, counts, sums, and sorts one card list using
installed card data. It answers deckbuilding questions such as the mana curve,
type mix, role counts by tag, tribal or keyword density, color pips, color
sources, deck price, and "cheapest or most expensive cards", in one call.

It reads the same [list sources](#list-sources) as `card_lists_compare`, so it
works on a local deck, an Archidekt deck or snapshot, a backup, a saved search,
or a comparison result.

### Request

```json
{
  "source": { "kind": "deck", "deckId": "...", "expectedRevision": 7, "zones": ["main", "commander"] },
  "where": [
    { "field": "typeLine", "op": "contains", "value": "Creature" },
    { "field": "manaValue", "op": "lte", "value": 3 }
  ],
  "groupBy": ["manaValue"],
  "metrics": ["quantity", "cards", "sum:priceUsd", "avg:manaValue", "pips", "sources"],
  "rows": { "fields": ["name", "manaValue", "priceUsd", "tags"],
            "sortBy": [ { "field": "priceUsd", "direction": "desc" } ],
            "pageSize": 25 }
}
```

| Part | Meaning |
| --- | --- |
| `where` | Filters, all of which must match. `op` is one of `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `in`, `contains`. For list fields (colors, tags, keywords, subtypes), `contains` means the list holds the value. |
| `groupBy` | Zero to two fields. A card with several values in a list field counts once in each group; the result says so with `multiValued: true`. Cards with no value go in a `none` group. |
| `metrics` | `quantity` (copies, from deck quantities; 1 for search results), `cards` (distinct cards), `sum:`, `avg:`, `min:`, `max:` over numeric fields, `pips`, and `sources`. |
| `rows` | Optional per-card rows with chosen fields, sorting, and paging. Omit it to get only grouped totals. |

Fields for `where`, `groupBy`, `sortBy`, and `rows.fields`: `name`,
`manaValue`, `typeLine`, `cardTypes`, `subtypes`, `supertypes`, `colors`,
`colorIdentity`, `keywords`, `tags`, `rarity`, `set`, `power`, `toughness`,
`loyalty`, `priceUsd`, `cheapestPriceUsd`, `edhrecRank`, `gameChanger`,
`producedMana`, and, for deck sources only, `zone`, `category`, and
`quantity`. Each field is an enum value in the schema.

### Mana Metrics

- **`pips`** counts colored mana symbols in mana costs, by color (W, U, B, R,
  G, C) and by mana value when `groupBy` includes it. A hybrid symbol such as
  `{W/U}` counts once for each of its colors and is also counted under
  `hybrid`. A Phyrexian symbol counts for its color and under `phyrexian`.
  Generic and `X` symbols are not pips. Copies multiply the count.
- **`sources`** counts copies of cards whose Scryfall `produced_mana` includes
  each color. Cards that list all five colors are also counted under
  `anyColor`. Lands with no `produced_mana` (such as fetch lands) are counted
  under `noProducedMana` and are not assigned colors. Whether a land enters
  tapped is not in Scryfall's data, so it is listed in `limits` as not
  modeled.

These are counts of printed card data. The agent decides which counted cards
are real sources and passes its own source groups to `stats_mana_availability`
or `stats_simulate`.

### Result

```json
{
  "source": { "kind": "deck", "deckId": "...", "revision": 7 },
  "matched": { "cards": 31, "quantity": 31 },
  "groups": [
    { "key": { "manaValue": 2 }, "quantity": 9, "cards": 9, "sum:priceUsd": "14.20",
      "avg:manaValue": "2", "pips": { "G": 7, "B": 4 }, "sources": { "G": 2 } }
  ],
  "multiValued": false,
  "missingValues": [ { "field": "priceUsd", "cards": 2 } ],
  "unmatched": [],
  "rows": { "items": [], "nextCursor": null },
  "limits": [ "Enters-tapped status is not modeled." ],
  "fingerprint": "..."
}
```

Money and averages are exact decimal strings, like the existing statistics
results. Cards missing a numeric value are left out of that sum or average
and counted in `missingValues`.

### Rules

- Card data comes from the installed `all_cards` data, which has every
  printing (`src/MtgMcp.Scryfall/ScryfallCardDataStore.cs:1313`). If it is
  not installed, the result is `not-cached` and names `scryfall_corpus_sync`.
- Deck entries use their exact printing. Entries with no Scryfall identity go
  in `unmatched`.
- Groups are ordered by key, rows by the requested sort and then by name and
  Oracle ID. The same request gives the same result.
- Paging and source markers work as in `card_lists_compare`; nothing is stored.
- Read-only and idempotent. `OpenWorld = true` because Archidekt sources read a
  provider.

### Placement

The filter, group, and metric code is a pure function in `MtgMcp.Scryfall`
over mapped cards and quantities, tested without storage. App resolves the
source and calls it. The tool belongs to the `scryfall` toolset for the same
reason as `card_lists_compare`: every answer needs card data. The source rule
applies here too.

### Alternatives Considered

- **Extend `stats_deck_summary`.** It is deliberately free of card data
  ("without provider lookup",
  `src/MtgMcp.App/Statistics/StatisticsReadTools.cs:181`) and only takes deck
  sources. A separate tool keeps that contract and works on any list.
- **Accept Scryfall query syntax in `where`.** Would mean writing a second
  Scryfall query parser. Agents can already filter with Scryfall syntax, then
  pass the saved search to `card_list_query` or intersect it with a deck.
- **Several fixed reports (curve, types, prices).** Each would be a new tool;
  one grouped query covers them all.

## Deck Rule Facts

`deck_validate` gains an optional `rules` value. With `rules: "commander"` it
adds these facts to its structural checks, using installed card data:

| Fact | How it is computed |
| --- | --- |
| Deck size | Sum of quantities in `commander` and `main`. |
| Commanders | Entries in the `commander` zone, and whether each card's type line and text make it eligible (legendary creature, or text "can be your commander"). |
| Color identity | Union of the commanders' `color_identity`, and every main entry whose `color_identity` is outside it. |
| Singleton | Card names with more than one copy, except basic lands and cards whose oracle text says "A deck can have any number of cards named". |
| Format legality | Each entry whose Scryfall `legalities.commander` is `banned` or `not_legal`. |
| Game Changers | Count and list of entries whose Scryfall `game_changer` field is true. This fact has no `passed` flag, because the allowed number depends on the bracket the agent chooses. If any entry's installed data lacks the field, the fact reports `status: "unknown"` with those entries, and the count covers only entries where it is known. |

Each fact except Game Changers has a `passed` flag computed by its stated
rule, plus the entries involved. The tool does not name a bracket, judge power level, or decide
whether a deck is "good". Partner and background pairing are reported as
"commanders: 2" with both cards' text; the agent judges whether the pair is
allowed. The `game_changer` field is not mapped today, so phase 2 adds it to
the card mapping.

This reverses the 0.9.0 rule of no legality behavior, by owner decision on
2026-10-25. Phase 4 step 1 records it in the rewrite umbrella.

## Deck Tools

`DeckChangeInput` already accepts `update-metadata`, `add-entry`,
`update-entry`, `remove-entry`, `add-category`, `update-category`,
`remove-category`, `assign-category`, `unassign-category`,
`upsert-provider-binding`, and `remove-provider-binding`
(`src/MtgMcp.App/Decks/DeckChangeInput.cs:11-21`). So these tools are removed
with no loss of capability: `deck_update`, `deck_entry_add`,
`deck_entry_update`, `deck_entry_remove`, `deck_category_create`,
`deck_category_update`, `deck_category_delete`, `deck_category_assign`,
`deck_category_unassign`.

`deck_category_rules_validate` is folded into
`deck_category_rules_preview`, which already validates before it previews.
The preview result gains the validation details.

The `deck_apply_changes` description gains one example per common change and
states that a single change is a normal use.

### Removal Review

| Item | Decision | Reason |
| --- | --- | --- |
| Nine single-step edit tools | Remove | Duplicates of `deck_apply_changes`. |
| `common-v1` category preset | Remove | It is a server-chosen mapping from four role names (ramp, card draw, removal, recursion) to four tags (`src/MtgMcp.App/Decks/DeckCategoryRuleResolver.cs:26-32`). That choice belongs to the agent, which can send the same rules inline. |
| Local backups | Keep in `decks` | They copy the whole `decks.db` file (`src/MtgMcp.App/Decks/DeckWriteTools.cs:314`), which neither deck revisions nor Archidekt snapshots replace. |
| Identity reconciliation | Keep in `decks` | Exact matching only, "without fuzzy matching" (`src/MtgMcp.App/Decks/DeckIdentityReconciliation.cs:708`). Needed before upload when entries lack exact IDs. |
| Interchange import and export | Keep in `decks` | Import is the path for decks from other sites; export is the path to them. |
| Corpus rollback and delete, snapshot delete | Keep in `scryfall` | They manage the installed card data that card tools read; read-only mode hides them. |
| Playgroup tools | Keep, frozen | Outside the main goal but in use. No redesign here. |

## Scryfall Tools

| New tool | Replaces | Input |
| --- | --- | --- |
| `scryfall_cards_get` | `scryfall_card_get`, `scryfall_card_collection` | `lookups`: 1 to 150 existing `ScryfallCardLookup` values; cursor and page size as today. |
| `scryfall_card_details` | `scryfall_card_prints`, `scryfall_card_rulings` | One lookup and required `include`: a non-empty set of `prints` and `rulings`. The result has one shape with a `prints` section and a `rulings` section, each present only when asked for. |
| `scryfall_reference_get` | `scryfall_sets`, `scryfall_catalog`, `scryfall_bulk_metadata` | Required `kind`: `sets`, `catalog`, or `bulk`; `catalogName` required only when `kind` is `catalog`. The result is a union with the same `kind`, holding today's sets, catalog, or bulk metadata result. |
| `scryfall_search_snapshots_get` | `scryfall_snapshot_list`, `scryfall_snapshot_get` | Required `kind`: `list` (with paging) or `get` (with required `snapshotId`). The result is a union with the same `kind`, holding today's list page or snapshot. |

`scryfall_search`, `scryfall_autocomplete`, `scryfall_tag_search`,
`scryfall_cards_by_tag`, `scryfall_corpus_status`, and `scryfall_corpus_sync`
keep their working names here; [Tool Names](#tool-names) lists the final
renames.

## Archidekt Tools

`archidekt_folders_get` replaces `archidekt_folder_list` and
`archidekt_folder_get`; `archidekt_snapshots_get` replaces `archidekt_snapshot_list`
and `archidekt_snapshot_get`. Both follow the merge rule: a required
`kind` of `list` or `get`, with the item ID required only for `get`, and a
result union with the same `kind`. Snapshot list rows stay metadata only, as
today ("without claiming list rows contain saved deck state",
`src/MtgMcp.App/Archidekt/ArchidektReadTools.cs:125`); only `get` returns
saved deck state. Preview/apply pairs stay.

A stale local revision or a changed remote deck already returns `conflict`.
Each such result must include the current local revision or remote state
marker in its data, so the agent can repeat the preview in one step
(ERR-001).

## Enum Schemas

Each choice parameter becomes a C# enum in the project that owns the
behavior, with `JsonStringEnumMemberName` values equal to today's strings.

```csharp
[JsonConverter(typeof(JsonStringEnumConverter<ScryfallFreshnessPolicy>))]
internal enum ScryfallFreshnessPolicy
{
    [JsonStringEnumMemberName("default")] Default,
    [JsonStringEnumMemberName("cache-only")] CacheOnly,
    [JsonStringEnumMemberName("refresh")] Refresh,
}
```

Expected schema:

```json
"freshnessPolicy": {
  "type": "string",
  "enum": ["default", "cache-only", "refresh"],
  "default": "default",
  "description": "Cache policy."
}
```

Phase 1 step 1 confirms SDK 2.2.0 produces this. If it does not, the
fallback is the `TransformSchemaNode` hook already used in
`StatisticsToolsetManifest`, applied by a shared App helper.

Known choice parameters today include `freshnessPolicy`, `unique`, `order`,
and `direction` in `ScryfallReadTools.cs`, plus choice strings in
`DeckCategorization.cs`, `DeckCategoryRuleResolver.cs`, and
`DeckIdentityReconciliation.cs`. The SCHEMA-001 test finds any that are
missed. Frozen Playgroup tools get enums only where they already have choice
strings; their names and inputs do not otherwise change.

## Statistics Descriptions

Each `stats_*` description keeps its first sentence and adds:

- the main request fields in one line; and
- one small example request as compact JSON.

Example for `stats_hypergeometric`:

> Calculates an exact draw probability from explicit population and event
> inputs. Fields: `population` (raw size or saved deck selection),
> `successGroup`, `drawCount`, `event`. Example:
> `{"population":{"kind":"raw","size":99},"successGroup":10,"drawCount":7,"event":{"kind":"at-least","count":1}}`

The example in each description is also a test fixture, so it cannot drift.
The exact field names are taken from the request types during phase 1; the
example above shows the intended shape only.

## Card Response Formats

`responseFormat` is an enum with three values, plus an optional `fields` list.

| Value | Content |
| --- | --- |
| `concise` | `scryfallId`, `oracleId`, `name`, `manaCost`, `manaValue`, `typeLine`, `colors`, `colorIdentity`, `producedMana`, `keywords`, `oracleText` (front face, then faces), `power`, `toughness`, `loyalty`, `legalities` (only formats the card is legal in), `prices.usd`, `cheapestPriceUsd` (lowest USD price across all installed printings of the card), `rarity`, `set`, `tags` (official oracle tag slugs from installed data), `gameChanger`, `edhrecRank`. |
| `standard` (default) | Today's mapped card result, unchanged. |
| `full` | Today's result with raw provider objects. Page size limit 25. |

The default stays `standard` by owner decision (2026-10-25), so existing
callers see no change. The `scryfall_search` description recommends `concise`
for broad filtering.

`fields` narrows `concise` to the listed field names and is only valid with
`responseFormat: "concise"`; with another format it returns `invalid-input`. `scryfallId`, `oracleId`,
and `name` are always included. Unknown names return `invalid-input` listing
the allowed names.

The projection is a pure function in `MtgMcp.Scryfall` from the mapped card
to the concise record, tested without network or storage. Snapshots and caches
still store full data; only the response changes. `deck_get` with
`include: ["cards"]` and `card_list_query` use the same projection.

`tags`, `cheapestPriceUsd`, and `gameChanger` come from installed data. When
it is not installed, those fields are `null` and the result's `limits` says
why. These fields make "find alternatives" two calls: read a card's tags, then
search for cards with the same tags in the deck's color identity.

`includeRaw` is removed from these tools because `full` replaces it.

## Simulation Model

`stats_simulate` runs caller-stated rules over shuffled copies of one saved
deck. Every choice the engine makes is either a rule of the game (draw one
card per turn, skip the first draw on the play) or a value in the request.
The result echoes the full rule set it applied, including any value that has
a default, so nothing is hidden.

### Request

```json
{
  "deck": { "deckId": "b1357522-...", "expectedRevision": 42 },
  "groups": [
    { "id": "lands", "select": { "kind": "category-ids", "ids": ["..."] } },
    { "id": "ramp2", "select": { "kind": "entry-ids", "ids": ["...", "..."] } }
  ],
  "openingHand": {
    "size": 7,
    "onThePlay": true,
    "mulligan": {
      "rule": "london",
      "freeFirstMulligan": true,
      "maxMulligans": 1,
      "keepWhen": { "kind": "count-between", "group": "lands", "min": 2, "max": 5 },
      "bottom": { "preferGroups": ["ramp2"], "otherwise": "random" }
    }
  },
  "eachTurn": [
    { "kind": "play", "group": "lands", "pick": "random", "maxPerTurn": 1,
      "addsMana": 1, "manaFrom": "same-turn" },
    { "kind": "cast-if-able", "group": "ramp2", "pick": "random", "maxPerTurn": 1,
      "cost": 2, "addsMana": 1, "manaFrom": "next-turn" }
  ],
  "turns": 4,
  "conditions": [
    { "id": "four-mana-turn-4", "check": { "kind": "mana-at-least", "amount": 4, "turn": 4 } },
    { "id": "ramp-by-turn-2", "check": { "kind": "drawn-at-least", "group": "ramp2", "count": 1, "byTurn": 2 } }
  ],
  "samples": 20000,
  "sampleHands": 3,
  "seed": "9f3c1a7e5b2d4c60"
}
```

### Result

```json
{
  "seed": "9f3c1a7e5b2d4c60",
  "randomVersion": "splitmix64-v1",
  "samples": 20000,
  "fingerprint": "...",
  "appliedRules": { "...": "the full normalized request rules" },
  "results": [
    { "id": "four-mana-turn-4", "hits": 14211, "rate": 0.7106, "wilson95": [0.7042, 0.7168] },
    { "id": "ramp-by-turn-2", "hits": 9874, "rate": 0.4937, "wilson95": [0.4868, 0.5006] }
  ],
  "mulligans": { "keptByRule": 18950, "keptAtLimit": 1050 },
  "libraryEmpty": 0,
  "sampleHands": [
    { "sample": 1, "hands": [ { "mulligan": 0, "entryIds": ["..."], "names": ["..."] } ],
      "kept": { "entryIds": ["..."], "bottomed": [] },
      "turns": [ { "turn": 1, "drawn": ["..."], "moved": [ { "rule": 0, "entryId": "..." } ], "mana": 1 } ] }
  ],
  "limits": [
    "Mana is generic; color requirements are not modeled.",
    "Only the stated groups and rules are applied."
  ]
}
```

The numbers above show shape only; they are not computed results.

### Engine Rules

These are fixed and stated in the tool description:

1. Groups must not overlap. A card in two groups returns
   `overlapping-groups` naming the entries. The agent splits groups itself.
2. Each turn: untap, draw (skipped on turn 1 on the play), then apply
   `eachTurn` rules strictly in the order listed. Each rule fires up to its
   `maxPerTurn` before the next rule runs.
3. `pick: "random"` chooses uniformly among the qualifying cards in hand
   using the seeded generator. `pick: "listed-order"` takes the first
   qualifying card in the order of the group's `entryIds` (only valid for
   `entry-ids` groups).
4. `manaFrom: "same-turn"` makes the added mana usable on the turn the card
   is played; `"next-turn"` makes it usable from the next turn.
5. London mulligan: draw `openingHand.size` cards, test `keepWhen`, and if
   it fails and mulligans remain, shuffle and draw `openingHand.size` again.
   After keeping, put cards on the bottom
   equal to the mulligan count: first from `preferGroups` in order (random
   within a group), then by `otherwise` (`random` only in the first version).
   When `maxMulligans` is used up, the last hand is kept and counted in
   `keptAtLimit`. With `freeFirstMulligan: true` (the Commander multiplayer
   rule), the first mulligan puts no card on the bottom; later ones count
   from one. `freeFirstMulligan` is required.
6. Cards in no group are drawn and held; they never move.
7. Cards drawn by a `draw-cards` rule are in hand at once, so rules listed
   after it in the same turn can use them; rules listed before it do not run
   again that turn. A card with `effect: "each-turn"` adds its extra draws at
   the draw step of every later turn, in addition to the normal draw. If the
   library runs out, drawing stops for that sample and the sample is counted
   in `libraryEmpty`.
8. `sampleHands` (0 to 10, required) returns the first that many samples in
   full: each hand drawn, the kept hand, bottomed cards, and each turn's draws,
   moved cards, and mana. These are the same seeded samples counted in the
   results, so the agent can inspect real examples.

### Rule and Check Kinds (first version)

| Kind | Where | Meaning |
| --- | --- | --- |
| `count-between` | `keepWhen` | Hand has between `min` and `max` cards from a group. |
| `all`, `any` | any check | Combine checks. |
| `play` | `eachTurn` | Move a card from the group in hand to the battlefield; no mana cost. |
| `cast-if-able` | `eachTurn` | If available mana is at least `cost`, move a card from the group to the battlefield and spend `cost`. |
| `draw-cards` | `eachTurn` | If available mana is at least `cost`, spend `cost` and draw `cards` cards. With `effect: "once"` the card leaves play (a spell such as Night's Whisper). With `effect: "each-turn"` it stays on the battlefield and draws `cards` more at each later draw step (a permanent such as Phyrexian Arena). |
| `mana-at-least` | condition | Mana available on `turn`, after that turn's rules, is at least `amount`. |
| `drawn-at-least` | condition | At least `count` group cards were seen by `byTurn`. |
| `on-battlefield-at-least` | condition | At least `count` group cards are on the battlefield by `byTurn`. |

Each kind is a closed JSON union entry, like the existing statistics unions.
`pick`, `maxPerTurn`, `manaFrom` (for mana rules), `effect` and `cards` (for
`draw-cards`), and `bottom` are required; the schema has no defaults for
them. Adding a kind later is a local change: one record, one
evaluator case, and fixtures.

### Engine Boundaries

- `MtgMcp.OnCurve` becomes the sampling engine. If the rename to
  `MtgMcp.Simulation` happens, it happens in one commit with no behavior
  change.
- The engine takes plain counts and rule records. It does not read the deck
  store or Scryfall. `MtgMcp.App` resolves the deck and groups, then calls the
  engine.
- The existing SplitMix64 generator, fingerprint, and Wilson interval code are
  reused.

### On-Curve Migration

`deck_on_curve_estimate` becomes the preset `on-curve`. The preset turns
today's inputs (target entry, land rules, turn limit, sample count, play or
draw, seed) into the equivalent groups, rules, and one condition, then calls
the same engine. The preset's rules appear in `appliedRules` like any other
request. SIM-004 replay fixtures prove identical hit counts.

## Error Paths

All failures stay typed `OperationResult` values with a reason code and a
message that says what to change. Examples:

- unknown group in a rule: `invalid-simulation-group`, naming the group;
- overlapping groups: `overlapping-groups`, naming the entries;
- work limit exceeded: `simulation-too-large`, stating the limit and the
  computed size;
- stale deck: `conflict`, with the current revision in the data (ERR-001);
- list source whose owning toolset is off: `invalid-input` with reason
  `toolset-not-selected` or `credentials-missing`;
- Archidekt deck or local deck changed between comparison pages: `conflict`,
  with the new state marker or revision;
- cursor that does not match the resent request: `invalid-input`;
- backup with a different schema version: `unsupported` with reason
  `backup-schema-mismatch`; deck missing from a backup: `not-found` with
  reason `deck-not-in-backup`;
- comparison nested more than 3 levels: `invalid-input` stating the limit;
- `card_list_query` with an unknown field or a metric on a non-numeric field:
  `invalid-input` listing allowed values;
- card data not installed for `card_list_query` or `deck_validate` rules:
  `not-cached`, naming `scryfall_corpus_sync`;
- unknown backup or snapshot ID: `not-found`.

## Test Design

- Architecture tests: exact tool names per toolset and mode, counts, the
  naming rule, the four merge rules, enum presence on choice parameters,
  unknown-property rejection, and the old-name map. The existing surface test
  in `tests/MtgMcp.Architecture.Tests/FoundationArchitectureTests.cs`
  hard-codes today's names (41 `deck_` or `scryfall_corpus` matches) and is
  updated in phase 3.
- Selection tests: default selection with no credentials; Archidekt with a
  username and password pair, with only one of them, with an override file,
  and with the default file; Playgroup with a key, an override file, the
  default file, and an invalid configuration under default and explicit
  selection; an explicit list naming a provider without credentials; `all`;
  and the default `remote` mode. The capabilities resource reports
  `credentials-missing` or `credentials-invalid`.
- App tests: each merged tool covers every lookup or include path with fake
  services; each `deck_apply_changes` change kind; `deck_get` includes with
  installed, missing, and unresolved card data; description examples run.
- Scryfall tests: projection unit tests, `fields` narrowing, and the size
  budget.
- Card set tests: the pure set function for each operation and `matchBy`
  value; App tests for each source kind with fake services, nesting, paging
  stability, unmatched entries, the Archidekt change-between-pages conflict,
  cursor mismatch, the source toolset rule, and backup and Archidekt
  snapshot sources, including a backup with an older schema and a deck
  missing from a backup.
- Card list query tests: the pure filter, group, and metric function for each
  operator, multi-valued groups, `none` groups, missing values, pips (hybrid,
  Phyrexian, generic, X), and sources (any color, no produced mana); App tests
  for each source kind and paging.
- Deck rule tests: each fact with passing and failing fixtures, including
  basics, "any number" cards, off-identity hybrid cards, banned cards, Game
  Changers, and two commanders.
- Simulation tests: fixed-seed fixtures, each engine rule, replay of on-curve
  fixtures, limit checks, and invalid rule checks. All offline.
- E2E smoke: process start, `tools/list` order stable across two starts.

## Change Isolation Check

Likely future changes and where they land:

- A new card field in `concise`: one projection function and its test. Both
  card results and `deck_get` pick it up.
- A new simulation rule kind: one union entry, one evaluator case, fixtures.
- A new card list source, such as a Moxfield import: one source record and
  one resolver, shared by `card_lists_compare` and `card_list_query`; neither
  pure function changes.
- A new groupable card field: one enum value and one field reader in the query
  function.

None of these touches toolset manifests or provider adapters.
