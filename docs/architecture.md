# Architecture

## Purpose

`mtg-mcp` is an evidence and workflow server. It returns card facts, provider
evidence, exact mathematics, and guarded operations. The client LLM decides how
to build the deck.

## Projects

| Project | Ownership |
| --- | --- |
| `MtgMcp.Core` | Provider-neutral contracts, failures, and evidence types |
| `MtgMcp.Decks` | Revisioned local decks, SQLite, backups, and interchange |
| `MtgMcp.Scryfall` | Official API transport, corpus, snapshots, and pacing |
| `MtgMcp.Archidekt` | Observed provider contract and synchronization |
| `MtgMcp.Playgroup` | Pinned official Public API contract |
| `MtgMcp.Statistics` | BCL-only exact calculations |
| `MtgMcp.App` | MCP host, configuration, composition, modes, and schemas |

Core references no adapter or host. Provider adapters do not reference one
another. App is the composition root.

## Runtime surface

The server uses stdio and registers static tools for one session. It exposes:

- 28 `deck_*` tools;
- 18 `scryfall_*` tools;
- 8 `stats_*` tools;
- 23 opt-in `archidekt_*` tools;
- 16 opt-in `playgroup_*` tools;
- `mtg://server/capabilities`; and
- zero prompts.

Toolsets control relevance. Modes control authority. See
[Toolsets](toolsets.md).

## Data

The `v0.9` application-data root contains independent stores:

- `decks.db` for local decks, provider bindings, and sync baselines; and
- `scryfall.db` for card facts, rulings, community tags, request snapshots,
  leases, and corpus generations.

The server does not migrate or modify legacy data. Multiple MCP processes reuse
the same stores. SQLite coordinates Scryfall pacing and corpus activation across
processes.

## Evidence flow

```text
Provider or local store
        |
        v
Owning adapter or deck store
        |
        v
Provider-neutral result and evidence contracts
        |
        v
MCP tool output
        |
        v
Client LLM judgment
```

Provider facts, community evidence, exact derivations, parser classifications,
heuristics, and sampled estimates remain distinct. Unknown, unavailable,
unsupported, and empty results are not interchangeable.

## Write flow

Local and remote writes use explicit operations. Existing-deck writes require a
revision. Synchronization and categorization use preview fingerprints. Remote
writes also require provider fingerprints or checksums where supported.

The server refuses stale or tampered requests. It does not choose a conflict
winner.

## Non-goals

Stable `0.9.0` does not provide:

- deck legality decisions;
- advisor prompts or intent inference;
- weak-card or replacement judgments;
- blended quality scores;
- strategic simulation;
- unofficial Moxfield network automation; or
- automatic legacy migration.

## Validation

Architecture tests enforce project references, exact surface registration,
toolset membership, operation-mode visibility, and forbidden legacy surfaces.
Each production assembly must maintain at least 90 percent line coverage.

Read [North Star](north-star.md), [Design Goals](design-goals.md), and the
[rewrite guide](rewrite-guide.md) for product constraints.
