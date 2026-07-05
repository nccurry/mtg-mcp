# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` is being rebuilt as an evidence and workflow server for Magic: The
Gathering. Its job is to return grounded card, deck, provider, and statistical
evidence with visible provenance and limits. The client LLM remains responsible
for deckbuilding decisions.

## Rewrite Status

This branch contains the completed repository foundation and local deck store,
implemented offline manual deck interchange, the unified Scryfall evidence
capability, and guarded Archidekt synchronization for the clean-break `0.9.0`
rewrite. It is a usable stdio MCP server with one capability resource plus
deterministic `deck_*`, `scryfall_*`, and opt-in `archidekt_*` and
`playgroup_*` workflows.

- `MtgMcp.Core` provides closed result and evidence unions with stable JSON
  discriminators plus immutable, provider-neutral deck contracts.
- `MtgMcp.Decks` owns revisioned `decks.db` storage, transactional mutations,
  local structural validation, guarded backup/restore, and network-free manual
  import/export transformations.
- `MtgMcp.Scryfall` owns official API reads, exact-request snapshots, explicit
  four-dataset corpus synchronization, community-tag joins, cross-process
  pacing, and the shared `scryfall.db` store.
- `MtgMcp.Archidekt` owns observed provider contracts, credential isolation,
  fresh deck/folder/snapshot evidence, guarded remote operations, and a
  conservative process-wide per-account request lane.
- `MtgMcp.Playgroup` owns the pinned official Public API 1.0.0 contract,
  lossless provider-shaped evidence, bearer isolation, conservative pacing,
  bounded read retry, and single-attempt remote writes.
- `MtgMcp.App` provides the stdio MCP host, operation-mode enforcement,
  static capability-toolset selection, standard configuration, versioned
  data-root resolution, legacy-data detection, and sensitive-value redaction.
- Standard initialization and `mtg://server/capabilities` are implemented.
  The default surface is twenty-one tools in `read-only`, forty-one tools in
  `local` and `remote`, one resource, and zero prompts. The complete opt-in
  `all` profile is 46/67/80 tools by mode.
- `mtg-mcp --smoke` is a one-shot configuration/process probe. `task smoke:mcp`
  establishes a real session with the official C# client and reads the
  capability resource.
- No recommendations, simulations, strategic decisions, or later-child
  capability placeholders are registered.
- The legacy `0.8.0` implementation remains available in Git history and its
  released package; it is not copied into this rewrite.

The deck requirements and implementation evidence are in the
[Local Deck Store PLC](docs/llms/plcs/completed/local-deck-store/README.md) and
[Manual Deck Interchange PLC](docs/llms/plcs/in-progress/manual-deck-interchange/README.md).
Scryfall requirements and acceptance evidence are in the
[Scryfall Corpus And Evidence PLC](docs/llms/plcs/completed/scryfall-corpus-and-evidence/README.md).
Archidekt requirements and acceptance evidence are in the
[Archidekt Deck Sync PLC](docs/llms/plcs/completed/archidekt-deck-sync/README.md).
Playgroup requirements and acceptance evidence are in the
[Playgroup Public API PLC](docs/llms/plcs/completed/playgroup-public-api/README.md).
The [rewrite guide](docs/rewrite-guide.md) explains how this branch relates to
the broader `0.9.0` program.

## Foundation Configuration

The process accepts `--mode`, `--toolsets`, `--data-dir`, and
`--scryfall-ttl-hours` alongside `--smoke`. Equivalent environment variables
are `MTGMCP__MODE`, `MTGMCP__TOOLSETS`, `MTGMCP__DATA_DIR`, and
`MTGMCP__SCRYFALL_TTL_HOURS`. An optional `mtg-mcp.json` in the working
directory uses the corresponding `MODE`, `TOOLSETS`, `DATA_DIR`, and
`SCRYFALL_TTL_HOURS` keys. The Scryfall evidence TTL defaults to 24 hours.
Command-line values override environment values, which override JSON.

Archidekt uses the nested JSON keys `ARCHIDEKT:USERNAME`,
`ARCHIDEKT:PASSWORD`, and `ARCHIDEKT:CREDENTIALS_FILE`, or their
`MTGMCP__ARCHIDEKT__...` environment forms. The provider origin is fixed to
Archidekt so a configuration mistake cannot send credentials to another host.
The standard fallback credential file is `.mtg-mcp/archidekt.json` beneath the
user profile when it exists. Authentication status never returns an identity,
secret value, token, or path.

Playgroup uses `PLAYGROUP:API_KEY` in `mtg-mcp.json` or
`MTGMCP__PLAYGROUP__API_KEY` in the environment. The origin is fixed to the
official public API, and `playgroup_auth_status` reports only whether a key is
configured.

Modes are `read-only`, `local` (the default), and `remote`. Read-only mode
forbids local and remote mutation. A provider-shaped read may be visible there,
but the current Scryfall tools return `local-write-required` before HTTP when a
miss would require coordinated pacing and snapshot persistence. Local mode adds
local writes, and remote mode adds explicit remote writes.

Toolsets control relevance, not authority. Omitted `TOOLSETS` or `default`
enables implemented default toolsets, `all` enables every implemented stable
toolset, `none` exposes zero tools, and a comma-separated exact lowercase list
selects an explicit subset. `decks` and `scryfall` are implemented and both are
default-enabled. `archidekt` and `playgroup` are implemented and opt-in. They can be selected
independently; `none` leaves only
MCP initialization and `mtg://server/capabilities`. Unimplemented names fail
startup instead of creating placeholder tools. Selection is fixed for the
session and the server does not advertise dynamic tool-list changes.

When `DATA_DIR` is omitted, the resolved path is the platform application-data
directory followed by `mtg-mcp/v0.9`. Startup and deck reads do not create that
directory. The first authorized deck write creates `decks.db`; an authorized
Scryfall acquisition or explicit corpus sync creates `scryfall.db`. All MCP
processes using the same data root reuse those stores. Legacy entries are
detected only to report the clean-break boundary; they are never parsed,
migrated, or modified.

## Local Deck Surface

All modes expose `deck_list`, `deck_get`, `deck_validate`, and
`deck_backup_list`. `local` and `remote` additionally expose revision-guarded
deck, entry, category, batch, and backup mutations. Categories are independent
of zones, duplicate card rows remain independently addressable, and every
existing-deck mutation requires `expectedRevision`.

The store is format-neutral and preserves unresolved card identities. Its
Commander validation checks only explicitly documented local structure; it
does not infer legality, card roles, provider validity, or deck quality.

## Manual Deck Interchange

All modes expose `deck_interchange_formats`, `deck_import_preview`, and
`deck_export_bundle`. `local` and `remote` also
expose fingerprint-guarded `deck_import_create`. Imports never query a card or
provider service, and partial text imports require explicit acceptance.

`mtg-mcp-json-v1` is the lossless native format with schema tag
`mtg-mcp.deck/v1`. `generic-text-v1` preserves quantity, name, zone headings,
and available printing hints while reporting companion-only fields.
`archidekt-text-v1` and `moxfield-bulk-edit-v1` generate manual artifact bundles
with native JSON and category-assignment companions. Both formats passed dated
manual UI acceptance and are available without an experimental opt-in.
Archidekt preserves quantities, exact printings, and one primary category, but
zones, distinct same-print finishes, and secondary categories are
companion-only. Moxfield preserves exact printings, finish markers, and local
tags, while zones remain companion-only. Excluded entries are never emitted to
provider text and remain in the native companion.

## Scryfall Evidence Surface

All modes expose fourteen reads covering official search, exact card and
collection lookup, printings, rulings, sets, catalogs, autocomplete, bulk
metadata, corpus status, immutable snapshot replay, and installed community
tags. `local` and `remote` additionally expose explicit corpus
sync/rollback/delete and guarded snapshot deletion.

The active corpus is never downloaded at startup or by an ordinary read.
`scryfall_corpus_sync` explicitly streams All Cards, Rulings, Oracle Tags, and
Art Tags and atomically activates only a complete validated generation. The
store retains the active and immediately previous generations. Arbitrary
search expressions remain provider-authoritative; only an eligible snapshot of
the exact same request can be reused.

Freshness policies are `default`, `cache-only`, and `refresh`. Immutable
snapshots never expire; the TTL only controls default reuse. `read-only` can
reuse stored evidence but returns `local-write-required` before HTTP when an
acquisition would need leases, pacing, or persistence. Card facts and community
tags carry separate evidence descriptors, and explicit tag coverage prevents
an absent tag corpus from looking like a known empty assignment set.
Exact names include front/back face names, art tags join through root or face
illustrations, and bulk price/rank freshness follows the provider dataset's
update time rather than the later local download time.
Collection resolution accepts up to 150 ordered identities, uses the local
corpus first, and splits remaining unique misses into official batches of at
most 75. Results are stable cursor pages: 25 rows by default, at most 100
compact rows, or 25 rows with raw objects. A cursor continuation replays its
exact retained evidence without HTTP or refresh.
Card, ruling, set, and tag tools return normalized evidence without raw source
objects by default. Set `includeRaw=true` when unknown provider extensions or
byte-level source inspection matter; raw pages remain preserved either way.
Compact snapshot replay similarly returns stable member ordinals and checksums,
and adds the exact stored objects only when `includeRaw=true`.
Scryfall's supported catalog vocabulary is documented under
[Catalogs](https://scryfall.com/docs/api/catalogs); live catalog endpoints use
`/catalog/{name}`.

## Archidekt Evidence And Synchronization

Enable `archidekt` explicitly. It contributes 11 read/preview tools in
`read-only`, adds local pull apply in `local`, and exposes all 23 tools in
`remote`. The surface covers authentication readiness; owned deck list/get;
three-way local/baseline/remote diffs; guarded pull and push; private-by-default
deck create and verified delete; folder list/get/create/update/move/empty-only
delete; and named snapshot list/get/create/rename/delete/guarded restore.

Every read returns fresh provider evidence. Every apply replays local revision,
remote fingerprint, source checksum, and preview fingerprint guards. The MCP
never chooses a conflict winner. Folder deletion independently checks the
owned deck list because the observed folder-tree response omits deck rows.
Snapshot restore preserves current deck name, visibility, and folder placement
because Archidekt snapshots do not own those account-level fields.

Provider starts are serialized per configured account within the process, at
least two seconds apart, and capped at 30 in any rolling 60 seconds. A single
MCP invocation has a hard 150-request budget shared across composed preview,
authentication, apply, and verification calls. `403` stops immediately; `429`
stops the operation and installs the bounded `Retry-After` cooldown; ambiguous
mutations are never retried. Read retries are bounded to two transient
transport/5xx failures. Archidekt is an observed replaceable web contract, so
contract drift returns structured unavailable/unsupported outcomes instead of
guessed behavior.

## Playgroup Evidence Surface

Enable `playgroup` explicitly. It contributes redacted auth status plus every
documented Public API 1.0.0 GET operation—14 tools total—in all modes. In
`remote`, two additional tools submit a game-event batch or create a live
session. The official contract has no deck-update operation, so capability
metadata reports `deck-update` as unsupported and the adapter never probes a
private route.

Each result contains the exact provider JSON together with operation ID,
endpoint, API version, pinned-contract checksum, retrieval time, source-body
checksum, and limitations. Provider fields, explicit nulls, pagination, and
additive fields are preserved without turning Playgroup observations into deck
rankings or local quality scores. One tool invocation makes one provider call
unless an idempotent GET receives a bounded transient failure.

Request starts share a process-wide credential lane and are at least 250 ms
apart. GETs retry transient transport/5xx failures at most twice; `401` and
`403` stop immediately; `429` permits one retry only with a present bounded
`Retry-After`. Writes are never retried after a response or ambiguous
transport failure. Because the current API exposes no cleanup for either
write, normal and acceptance tests use fixtures only for writes; the opt-in
live test performs `/me` and cannot mutate provider state.

## Product Direction

The proposed stable target will provide explicit, capability-prefixed
operations for exact deck statistics in addition to the implemented local and
provider surfaces. Official card facts and
community tag evidence share `scryfall.db` but retain distinct schemas and
evidence classes. Provider facts, exact derivations, parser classifications,
heuristics, and sampled estimates remain visibly distinct.

The target capability-toolset layer keeps ordinary discovery small:
`decks`, `scryfall`, and `stats` form the default profile, while Archidekt and
Playgroup require explicit enablement. Toolsets control relevance;
operation modes remain the authority boundary. Only implemented descriptors
appear in capability metadata or can be selected.

Accepted AMEND-004 governs the implemented unified Scryfall boundary. The later
[deterministic categorization](docs/llms/plcs/planned/deterministic-deck-categorization/README.md)
packet remains planning-only and will evaluate caller-owned rules without
inventing category meanings.

Stable releases will not contain advisor prompts, intent inference, weak-card
judgments, replacement recommendations, blended quality scores, or strategic
automation. Deferred ideas are tracked in
[Potential Features](docs/potential-features.md).

## Development

The repository pins a .NET 11 preview SDK. Use Task as the supported command
menu:

```bash
task --list
task lint
task test
task coverage
task pack
task smoke:process
task smoke:mcp
task test:live:methods
```

From a fresh checkout, run `./bootstrap.sh`; on Windows, run
`.\bootstrap.ps1`. Normal tests are deterministic and offline. Opt-in
`Category=Live` tests verify current Scryfall metadata, a bounded read, and a
disposable 60-card Red/White Weenies deck resolved through the official MCP
client. `MTGMCP_RUN_ARCHIDEKT_LIVE=1` enables a private disposable
deck/folder/snapshot lifecycle with verified cleanup under production pacing.
`MTGMCP_RUN_PLAYGROUP_LIVE=1` enables a read-only authenticated `/me` probe
when `MTGMCP__PLAYGROUP__API_KEY` is configured.
The multi-gigabyte corpus acceptance additionally requires
`MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1` and an explicit
`MTGMCP_SCRYFALL_ACCEPTANCE_DATA_DIR`; it never deletes that directory.
The separate [live method acceptance](docs/llms/plans/live-method-acceptance.md)
installs the generated package and exercises all 80 public tools. It requires
an explicitly marked scratch root and clean committed worktree, pins results
to that commit and package version, restores its owner-authorized Archidekt
deck before cleanup, and never invokes the two Playgroup writes.

## Architecture

- `MtgMcp.Core` contains provider-neutral, dependency-light domain logic and
  must not reference the host or third-party runtime packages.
- `MtgMcp.Decks` contains local SQLite persistence and depends only on Core and
  the pinned SQLite runtime.
- `MtgMcp.Scryfall` contains official provider transport, evidence mapping,
  SQLite snapshots/corpus storage, and cross-process coordination.
- `MtgMcp.Archidekt` contains the observed provider contract, transport,
  canonical evidence mapping, synchronization planning, and pacing.
- `MtgMcp.Playgroup` contains the pinned official API fixture, lossless
  provider evidence transport, authentication, pacing, and retry policy.
- `MtgMcp.App` owns process, configuration, and MCP host concerns.
- Capability projects are added only when their independently approved child
  PLC is implemented.
- Normal validation enforces at least 90 percent line coverage for each
  production assembly that contains executable source.

See [AGENTS.md](AGENTS.md), [North Star](docs/north-star.md), and
[Design Goals](docs/design-goals.md) for durable repository guidance.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
