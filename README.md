# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` gives an LLM grounded Magic: The Gathering card, deck, provider, and
statistical evidence. The LLM makes deckbuilding decisions.

The clean-break `0.9.0` server uses stdio. It exposes 97 tools, one
capability resource, and no prompts. It does not migrate `0.8.x` data or tool
schemas.

## Status

The evidence-first rewrite is complete. `0.9.0` is the first stable release of
the new surface.

| Capability | Tools | Default | Storage or writes |
| --- | ---: | --- | --- |
| Local decks and interchange | 29 | Yes | Local |
| Scryfall evidence | 18 | Yes | Local cache and card data |
| Exact statistics | 8 | Yes | No |
| Archidekt | 23 | No | Remote |
| Playgroup | 16 | No | Remote |
| Commander Spellbook evidence | 3 | No | Local cache only |

The packaged acceptance run before the Spellbook addition passed 88 tool calls.
Two Scryfall card-data download operations are fixture-backed, Scryfall rollback
awaits a second provider generation, and two Playgroup writes remain fixture-only
because the public API has no cleanup. Commander Spellbook has its own bounded,
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

`task install` also starts an isolated, read-only Scryfall-enabled MCP server
and stops it. That catches single-file packaging problems that `--smoke` cannot
see.

## Choose a mode

Modes control authority.

| Mode | Reads | Local writes | Remote writes |
| --- | --- | --- | --- |
| `read-only` | Yes | No | No |
| `local` | Yes | Yes | No |
| `remote` | Yes | Yes | Yes |

`local` is the default. Scryfall cache misses can require a local write for
pacing and evidence persistence. In `read-only`, those misses return
`local-write-required` before HTTP.

Commander Spellbook reads are available in all three modes. They never change a
deck or make a write request to the source. Successful responses use a short-lived
local cache so repeated exact requests do not create needless provider traffic.

## Choose toolsets

Toolsets control relevance. They do not grant authority.

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

Read `mtg://server/capabilities` to inspect the active mode, toolsets, counts,
credential state, package version, and data-store state. Reading the resource
does not contact a provider.

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
- Archidekt and Moxfield manual artifacts;
- exact Scryfall identity reconciliation; and
- deterministic tag-rule categorization; and
- fact-backed on-curve estimates with caller-supplied land rules.

Every existing-deck mutation requires `expectedRevision`. The store is
format-neutral. Validation checks structure, not Commander legality, card
quality, or strategic fit.

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

Provider artifacts do not perform network automation. Excluded entries remain
in the native companion and are omitted from provider text.

## Query Scryfall

The `scryfall` toolset supports search, exact card lookup, collection lookup,
prints, rulings, sets, catalogs, autocomplete, bulk metadata, tag evidence,
immutable snapshots, and explicit card-data download operations.

The local card-data store contains All Cards, Rulings, Oracle Tags, and Art
Tags. Card-data downloads are explicit. They never run at startup or in the
background.

Freshness policies are `default`, `cache-only`, and `refresh`. The default TTL
is 24 hours. Immutable snapshots do not expire.

`scryfall_card_collection` accepts 150 ordered lookup rows. It uses local card-data
hits first, deduplicates provider misses, and sends provider batches of at most
75. Results use stable cursor pagination.

Arbitrary Scryfall queries remain provider-authoritative. The cache reuses only
the exact same request. Card facts and community tags remain separate evidence
classes.

## Read Commander Spellbook evidence

Enable the `spellbook` toolset for three source-evidence tools:

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

Archidekt may renumber category positions. Content equality therefore compares
category identity, membership, flags, and primary assignment, not the
provider-controlled numeric rank.

## Read Playgroup evidence

Enable the `playgroup` toolset. It exposes the pinned Public API 1.0.0 surface.
All modes include 14 safe read tools. `remote` adds two write tools.

Results preserve provider JSON and include operation, API version, retrieval
time, checksums, and limitations. The adapter does not rank decks or infer
quality.

Request starts are at least 250 milliseconds apart. Reads have bounded retry.
Writes are single-attempt. Live acceptance does not invoke writes because the
public API provides no cleanup operation.

## Evidence and safety

The server keeps these output classes distinct:

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

| Project | Ownership |
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
