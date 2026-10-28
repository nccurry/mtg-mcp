# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` gives an LLM grounded Magic: The Gathering card, deck, provider, and
statistical evidence. The LLM makes deckbuilding decisions.

Version `0.9.0` is a fresh stdio server design. Its full `all` profile in
`remote` mode exposes 97 tools, one `mtg://server/capabilities` resource, and
no prompts. The focused default profile exposes 33 tools in `read-only` mode
and 55 in `local` or `remote` mode. It does not migrate `0.8.x` data or tool
schemas.

## Status

The evidence-first rewrite is complete. `0.9.0` is the first stable release of
the new server.

| Feature | Tools | Default | Storage or writes |
| --- | ---: | --- | --- |
| Local decks and interchange | 29 | Yes | Local |
| Scryfall evidence | 18 | Yes | Local cache and card data |
| Exact statistics | 8 | Yes | No |
| Archidekt | 23 | No | Remote |
| Playgroup | 16 | No | Remote |
| Commander Spellbook evidence | 3 | No | Local cache only |

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
mtg-mcp --mode=local --toolsets=default
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

`local` is the default. A Scryfall cache miss can need a local write to record
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
| `default` or omitted | Enable `decks`, `scryfall`, and `stats` |
| `all` | Enable every implemented toolset |
| `none` | Expose no tools |
| Comma-separated list | Enable the exact named toolsets |

Available toolsets are `decks`, `scryfall`, `stats`, `archidekt`, `playgroup`,
and `spellbook`. Selection is fixed for the MCP session.

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 33 | 55 | 55 |
| `all` | 61 | 84 | 97 |
| `none` | 0 | 0 | 0 |

Read `mtg://server/capabilities` to see the active mode, toolsets, tool counts,
credential state, package version, and local data state. It does not contact a
provider.

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

Use `MTGMCP__ARCHIDEKT__USERNAME` and `MTGMCP__ARCHIDEKT__PASSWORD`, or create:

```text
~/.mtg-mcp/archidekt.json
```

The provider origin is fixed. Authentication output never contains credentials,
account identity, or a credential path.

### Configure Playgroup

Use `MTGMCP__PLAYGROUP__API_KEY`, or create:

```text
~/.mtg-mcp/playgroup.json
```

The file contains one `apiKey` property. The provider origin is fixed to the
official public API.

## Work with local decks

The `decks` toolset supports:

- revisioned deck, entry, zone, category, and provider-binding changes;
- atomic ordered batches;
- guarded backup, restore, and delete;
- native JSON and generic text import and export;
- manual import and export files for Archidekt and Moxfield;
- exact Scryfall identity reconciliation;
- tag-rule grouping with repeatable results; and
- on-curve estimates based on card data and caller-supplied land rules.

Every change to an existing deck requires `expectedRevision`. The local store
is not tied to one import format. Validation checks structure, not Commander
legality, card quality, or strategic fit.

Use `deck_identity_reconcile_preview` before apply. Resolution uses printing ID,
set and collector number, Oracle ID, then exact name. It never uses fuzzy
matching or selects an arbitrary printing.

Use `deck_category_rules_validate`, `deck_category_rules_preview`, and
`deck_category_rules_apply` to evaluate caller-owned tag rules. Rules can be
inline or use the transparent `common-v1` preset. The MCP does not decide what a
category means.

### Estimate mana on curve

Use `deck_on_curve_estimate` to sample how often one mainboard card can be cast
by a chosen turn. Supply the saved deck ID and current revision, the target
entry ID, one rule for each eligible land, a turn limit, and optionally a seed
and sample count. The result includes the exact installed Scryfall facts used,
the caller rules, a replay seed, a 95% Wilson interval, miss counts, and up to
two sample traces.

Version 1 models only simple printed costs and caller-described single-faced
lands. It does not read card text, infer land behavior, model other mana
sources, choose plays, rate the deck, or predict real games.

## Import and export decks

Use `deck_interchange_formats` to inspect preservation limits.

| Format | Notes |
| --- | --- |
| `mtg-mcp-json-v1` | Lossless native format |
| `generic-text-v1` | Quantity, name, zone headings, and printing hints |
| `archidekt-text-v1` | Manual import plus native/category companions |
| `moxfield-bulk-edit-v1` | Bulk Edit text plus native/tag companions |

These provider export files do not contact a provider. Excluded entries remain
in the native companion file and are omitted from provider text.

## Query Scryfall

The `scryfall` toolset supports search, exact card lookup, collection lookup,
prints, rulings, sets, catalogs, autocomplete, bulk metadata, tag evidence,
immutable snapshots, and explicit card-data download operations.

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
The public names `scryfall_corpus_status`, `scryfall_corpus_sync`,
`scryfall_corpus_rollback`, and `scryfall_corpus_delete` are existing API
names for inspecting and managing that installed card data.

Freshness policies are `default`, `cache-only`, and `refresh`. The default TTL
is 24 hours. Immutable snapshots do not expire.

`scryfall_card_collection` accepts 150 ordered lookup rows. It uses local card-data
hits first, deduplicates provider misses, and sends provider batches of at most
75. Results use stable cursor pagination.

For arbitrary queries, Scryfall remains the source of truth. The cache reuses
only the exact same request. Card facts and community tags stay separate kinds
of information.

## Read Commander Spellbook evidence

Enable the `spellbook` toolset for three tools that return Commander Spellbook
data:

- `spellbook_variant_search` returns one bounded page for an exact Commander
  Spellbook query.
- `spellbook_variant_get` returns one exact source variant by ID.
- `spellbook_deck_combos_find` sends only the `commander` and `main` entries
  from one exact saved deck revision, then reports the entries it skipped.

The tools preserve Commander Spellbook JSON and name the source, request,
retrieval time, cache state, checksum, and limits. They do not rank results,
infer card roles, or recommend a combo or card change. The cache lasts 15
minutes by default; configure it with a whole number from 1 through 1,440
minutes.

## Calculate exact statistics

The `stats` toolset provides eight read-only tools for:

- univariate and multivariate hypergeometric probability;
- probability-by-turn tables;
- caller-defined mana-source availability;
- package and combo assembly;
- explicit mulligan schedules;
- minimum-copy and minimum-source solving; and
- deterministic deck composition summaries.

Every probability returns an exact reduced fraction and a stable decimal. The
caller supplies the population, groups, turn draws, mana capabilities, and keep
rules. The MCP does not infer legality, roles, or whether a result is good.

Deck-backed statistics can select entries by entry ID, zone name, or category
ID. This lets an LLM use existing Archidekt groups or deterministic categories
without hiding the selected cards.

## Synchronize Archidekt

Enable the `archidekt` toolset. Use `remote` mode for remote writes.

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

Enable the `playgroup` toolset. It exposes tools for the pinned Public API 1.0.0.
All modes include 14 safe read tools. `remote` adds two write tools.

Results preserve provider JSON and include operation, API version, retrieval
time, checksums, and limitations. The adapter does not rank decks or infer
quality.

Request starts are at least 250 milliseconds apart. Reads have bounded retry.
Writes are single-attempt. Live acceptance does not invoke writes because the
public API provides no cleanup operation.

## Errors

A failed tool call sets the MCP `isError` flag and keeps the structured body
(`kind`, `reasonCode`, `message`). Check `isError` first, then read `kind`.
A call whose arguments cannot be read returns `kind: "invalid-input"`, reason
`invalid-arguments`, and a top-level `path` to the rejected value, for example
`changes[0].entryDraft.quantity`. A union's `kind` property (such as a deck
change's `add-entry`) may appear anywhere in its object. A `conflict` caused by
stale state includes `current`, the current local revision, remote fingerprint,
or both, to preview again against.

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
simulation. Deferred work is listed in [Potential Features](docs/potential-features.md).

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
