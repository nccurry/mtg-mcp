# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` is being rebuilt as an evidence and workflow server for Magic: The
Gathering. Its job is to return grounded card, deck, provider, and statistical
evidence with visible provenance and limits. The client LLM remains responsible
for deckbuilding decisions.

## Rewrite Status

This branch contains the Phase 3 repository foundation for the clean-break
`0.9.0` rewrite. It is intentionally not a usable MCP server yet.

- `MtgMcp.Core` provides closed result and evidence unions with stable JSON
  discriminators.
- `MtgMcp.App` provides operation-mode enforcement, standard configuration,
  versioned data-root resolution, legacy-data detection, and sensitive-value
  redaction.
- No MCP tools, resources, prompts, provider calls, persistence,
  recommendations, or simulations are registered.
- `mtg-mcp --smoke` verifies configuration and process startup. The minimal MCP
  host remains Foundation Phase 4 work.
- The legacy `0.8.0` implementation remains available in Git history and its
  released package; it is not copied into this rewrite.

The active requirements and phase boundaries are in the
[Rewrite Skeleton and Repository Foundation PLC](docs/llms/plcs/in-progress/rewrite-skeleton-foundation/README.md).
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
directory followed by `mtg-mcp/v0.9`. Foundation startup does not create that
directory or any database. Legacy entries are detected only to report the
clean-break boundary; they are never parsed, migrated, or modified.

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
task smoke:mcp
```

From a fresh checkout, run `./bootstrap.sh`; on Windows, run
`.\bootstrap.ps1`. Normal tests are deterministic and offline. The current
foundation has no live-provider tests.

## Architecture

- `MtgMcp.Core` contains provider-neutral, dependency-light domain logic and
  must not reference the host or third-party runtime packages.
- `MtgMcp.App` owns process and, in later foundation phases, MCP host concerns.
- Capability projects are added only when their independently approved child
  PLC is implemented.
- Normal validation enforces at least 90 percent line coverage for each
  production assembly that contains executable source.

See [AGENTS.md](AGENTS.md), [North Star](docs/north-star.md), and
[Design Goals](docs/design-goals.md) for durable repository guidance.

## License

Licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
