# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

`mtg-mcp` is being rebuilt as an evidence and workflow server for Magic: The
Gathering. Its job is to return grounded card, deck, provider, and statistical
evidence with visible provenance and limits. The client LLM remains responsible
for deckbuilding decisions.

## Rewrite Status

This branch contains the Phase 2 repository foundation for the clean-break
`0.9.0` rewrite. It is intentionally not a usable MCP server yet.

- Production code consists only of dependency-light `MtgMcp.Core` and the
  `MtgMcp.App` process skeleton.
- No MCP tools, resources, prompts, provider calls, persistence, configuration,
  recommendations, or simulations are registered.
- `mtg-mcp --smoke` verifies that the foundation executable can start. Other
  arguments fail until the minimal MCP host is implemented in Foundation
  Phase 4.
- The legacy `0.8.0` implementation remains available in Git history and its
  released package; it is not copied into this rewrite.

The active requirements and phase boundaries are in the
[Rewrite Skeleton and Repository Foundation PLC](docs/llms/plcs/in-progress/rewrite-skeleton-foundation/README.md).
The [rewrite guide](docs/rewrite-guide.md) explains how this branch relates to
the broader `0.9.0` program.

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
