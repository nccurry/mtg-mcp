# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` is being rebuilt as an evidence and workflow server for Magic: The
Gathering. Its job is to return grounded card, deck, provider, and statistical
evidence with visible provenance and limits. The client LLM remains responsible
for deckbuilding decisions.

## Rewrite Status

This branch contains the completed repository foundation and local deck store,
plus implemented offline manual deck interchange for the clean-break `0.9.0`
rewrite. It is a usable stdio MCP server with one capability resource and
deterministic local `deck_*` workflows.

- `MtgMcp.Core` provides closed result and evidence unions with stable JSON
  discriminators plus immutable, provider-neutral deck contracts.
- `MtgMcp.Decks` owns revisioned `decks.db` storage, transactional mutations,
  local structural validation, guarded backup/restore, and network-free manual
  import/export transformations.
- `MtgMcp.App` provides the stdio MCP host, operation-mode enforcement,
  static capability-toolset selection, standard configuration, versioned
  data-root resolution, legacy-data detection, and sensitive-value redaction.
- Standard initialization and `mtg://server/capabilities` are implemented.
  The surface is seven read tools in `read-only`, twenty-three tools in `local`
  and `remote`, one resource, and zero prompts.
- `mtg-mcp --smoke` is a one-shot configuration/process probe. `task smoke:mcp`
  establishes a real session with the official C# client and reads the
  capability resource.
- No provider calls, recommendations, simulations, or later-child capability
  placeholders are registered.
- The legacy `0.8.0` implementation remains available in Git history and its
  released package; it is not copied into this rewrite.

The deck requirements and implementation evidence are in the
[Local Deck Store PLC](docs/llms/plcs/completed/local-deck-store/README.md) and
[Manual Deck Interchange PLC](docs/llms/plcs/in-progress/manual-deck-interchange/README.md).
The [rewrite guide](docs/rewrite-guide.md) explains how this branch relates to
the broader `0.9.0` program.

## Foundation Configuration

The process accepts `--mode`, `--toolsets`, and `--data-dir` alongside
`--smoke`. Equivalent environment variables are `MTGMCP__MODE`,
`MTGMCP__TOOLSETS`, and `MTGMCP__DATA_DIR`. An optional `mtg-mcp.json` in the
working directory uses `MODE`, `TOOLSETS`, and `DATA_DIR` keys.
Command-line values override environment values, which override JSON.

Modes are `read-only`, `local` (the default), and `remote`. Read-only mode still
permits explicit provider reads; it forbids local and remote mutation. Local
mode adds local writes, and remote mode adds explicit remote writes.

Toolsets control relevance, not authority. Omitted `TOOLSETS` or `default`
enables implemented default toolsets, `all` enables every implemented stable
toolset, `none` exposes zero tools, and a comma-separated exact lowercase list
selects an explicit subset. Only `decks` is implemented today, so `default`,
`all`, and `decks` expose the deck surface while `none` leaves only MCP
initialization and `mtg://server/capabilities`. Unimplemented names fail startup
instead of creating placeholder tools. Selection is fixed for the session and
the server does not advertise dynamic tool-list changes.

When `DATA_DIR` is omitted, the resolved path is the platform application-data
directory followed by `mtg-mcp/v0.9`. Startup and deck reads do not create that
directory. The first authorized deck write creates `decks.db`. Legacy entries
are detected only to report the clean-break boundary; they are never parsed,
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
with native JSON and category-assignment companions. Those provider formats
remain explicit opt-in experiments until current manual UI acceptance is
recorded; their output does not claim a successful provider import.

## Product Direction

The stable rewrite will provide explicit, capability-prefixed operations for
local decks, Scryfall, Archidekt, Playgroup, exact deck statistics, and a local
Scryfall Tagger cache. Provider facts, exact derivations, parser
classifications, heuristics, and sampled estimates remain visibly distinct.

The capability-toolset layer keeps ordinary discovery small:
`decks`, `scryfall`, and `stats` form the default profile, while Archidekt,
Playgroup, and Tagger require explicit enablement. Toolsets control relevance;
operation modes remain the authority boundary. Only implemented descriptors
appear in capability metadata or can be selected.

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
```

From a fresh checkout, run `./bootstrap.sh`; on Windows, run
`.\bootstrap.ps1`. Normal tests are deterministic and offline. The current
implementation has no live-provider tests.

## Architecture

- `MtgMcp.Core` contains provider-neutral, dependency-light domain logic and
  must not reference the host or third-party runtime packages.
- `MtgMcp.Decks` contains local SQLite persistence and depends only on Core and
  the pinned SQLite runtime.
- `MtgMcp.App` owns process, configuration, and MCP host concerns.
- Capability projects are added only when their independently approved child
  PLC is implemented.
- Normal validation enforces at least 90 percent line coverage for each
  production assembly that contains executable source.

See [AGENTS.md](AGENTS.md), [North Star](docs/north-star.md), and
[Design Goals](docs/design-goals.md) for durable repository guidance.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
