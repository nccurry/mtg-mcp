# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` gives an LLM grounded Magic: The Gathering card, deck, provider, and
statistical evidence. The LLM makes deckbuilding decisions.

The clean-break `0.9.0-preview.1` server uses stdio. It exposes 93 tools, one
capability resource, and no prompts. It does not migrate `0.8.x` data or tool
schemas.

## Status

The evidence-first rewrite is feature complete. Stabilization is active. Stable
release, cross-platform package approval, and rollback rehearsal remain open.

| Capability | Tools | Default | Writes |
| --- | ---: | --- | --- |
| Local decks and interchange | 28 | Yes | Local |
| Scryfall evidence | 18 | Yes | Local cache and corpus |
| Exact statistics | 8 | Yes | No |
| Archidekt | 23 | No | Remote |
| Playgroup | 16 | No | Remote |

The packaged acceptance run passed 88 tools live. Two Scryfall corpus operations
are fixture-backed, Scryfall rollback awaits a second provider generation, and
two Playgroup writes remain fixture-only because the public API has no cleanup.
See [live acceptance](docs/llms/plcs/in-progress/rewrite-stabilization-cutover/LIVE_ACCEPTANCE.md).

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

Use `mtg-mcp` as the MCP command. The default invocation is equivalent to:

```text
mtg-mcp --mode=local --toolsets=default
```

Use `--smoke` for a one-shot configuration check. It does not start an MCP
session.

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

## Choose toolsets

Toolsets control relevance. They do not grant authority.

| Value | Result |
| --- | --- |
| `default` or omitted | Enable `decks`, `scryfall`, and `stats` |
| `all` | Enable every implemented toolset |
| `none` | Expose no tools |
| Comma-separated list | Enable the exact named toolsets |

Available toolsets are `decks`, `scryfall`, `stats`, `archidekt`, and
`playgroup`. Selection is fixed for the MCP session.

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 32 | 54 | 54 |
| `all` | 57 | 80 | 93 |
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
- deterministic tag-rule categorization.

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
immutable snapshots, and explicit corpus lifecycle operations.

The local corpus contains All Cards, Rulings, Oracle Tags, and Art Tags. Corpus
sync is explicit. It never runs at startup or in the background.

Freshness policies are `default`, `cache-only`, and `refresh`. The default TTL
is 24 hours. Immutable snapshots do not expire.

`scryfall_card_collection` accepts 150 ordered lookup rows. It uses local corpus
hits first, deduplicates provider misses, and sends provider batches of at most
75. Results use stable cursor pagination.

Arbitrary Scryfall queries remain provider-authoritative. The cache reuses only
the exact same request. Card facts and community tags remain separate evidence
classes.

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
task surface:report
task coverage
task pack
task smoke:process
task smoke:mcp
task release:tool-smoke
```

Normal tests are deterministic and offline. Live tests require explicit opt-in.
Each production assembly must maintain at least 90 percent line coverage.

## Architecture

| Project | Ownership |
| --- | --- |
| `MtgMcp.Core` | Provider-neutral contracts and evidence |
| `MtgMcp.Decks` | Local SQLite decks and interchange |
| `MtgMcp.Scryfall` | Official transport, corpus, snapshots, and pacing |
| `MtgMcp.Archidekt` | Observed provider contract and synchronization |
| `MtgMcp.Playgroup` | Pinned official API evidence |
| `MtgMcp.Statistics` | BCL-only exact calculations |
| `MtgMcp.App` | MCP host, configuration, composition, and schemas |

Read [North Star](docs/north-star.md), [Design Goals](docs/design-goals.md), and
the [rewrite guide](docs/rewrite-guide.md) for durable constraints.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
