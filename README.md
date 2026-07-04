# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` is being rebuilt as an evidence and workflow server for Magic: The
Gathering. Its job is to return grounded card, deck, provider, and statistical
evidence with visible provenance and limits. The client LLM remains responsible
for deckbuilding decisions.

## Rewrite Status

This branch contains the completed repository foundation and local deck store
for the clean-break `0.9.0` rewrite. It is a usable stdio MCP server with one
capability resource and a deterministic local `deck_*` workflow.

- `MtgMcp.Core` provides closed result and evidence unions with stable JSON
  discriminators plus immutable, provider-neutral deck contracts.
- `MtgMcp.Decks` owns revisioned `decks.db` storage, transactional mutations,
  local structural validation, and guarded backup/restore.
- `MtgMcp.App` provides the stdio MCP host, operation-mode enforcement,
  standard configuration, versioned data-root resolution, legacy-data
  detection, and sensitive-value redaction.
- Standard initialization and `mtg://server/capabilities` are implemented.
  The surface is four read tools in `read-only`, nineteen tools in `local` and
  `remote`, one resource, and zero prompts.
- `mtg-mcp --smoke` is a one-shot configuration/process probe. `task smoke:mcp`
  establishes a real session with the official C# client and reads the
  capability resource.
- No provider calls, recommendations, simulations, import/export formats, or
  later-child capability placeholders are registered.
- The legacy `0.8.0` implementation remains available in Git history and its
  released package; it is not copied into this rewrite.

The deck requirements and implementation evidence are in the
[Local Deck Store PLC](docs/llms/plcs/completed/local-deck-store/README.md).
The [rewrite guide](docs/rewrite-guide.md) explains how this branch relates to
the broader `0.9.0` program.

## Foundation Configuration

The process accepts `--mode` and `--data-dir` alongside `--smoke`. Equivalent
environment variables are `MTGMCP__MODE` and `MTGMCP__DATA_DIR`. An optional
`mtg-mcp.json` in the working directory uses `MODE` and `DATA_DIR` keys.
Command-line values override environment values, which override JSON.

Modes are `read-only`, `local` (the default), and `remote`. Read-only mode still
permits explicit provider reads; it forbids local and remote mutation. Local
mode adds local writes, and remote mode adds explicit remote writes.

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

## Product Direction

The stable rewrite will provide explicit, capability-prefixed operations for
local decks, Scryfall, Archidekt, Playgroup, exact deck statistics, and a local
Scryfall Tagger cache. Provider facts, exact derivations, parser
classifications, heuristics, and sampled estimates remain visibly distinct.

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
