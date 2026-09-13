# Architecture

## Purpose

`mtg-mcp` is an evidence and workflow server. It returns card facts, provider
evidence, exact mathematics, one bounded sampled estimate, and guarded
operations. The client LLM decides how to build the deck.

## Projects

| Project | Responsible for |
| --- | --- |
| `MtgMcp.Core` | Provider-neutral contracts, failures, and evidence types |
| `MtgMcp.Decks` | Revisioned local decks, SQLite, backups, and interchange |
| `MtgMcp.Scryfall` | Official API transport, installed card data, snapshots, and pacing |
| `MtgMcp.Archidekt` | Observed provider contract and synchronization |
| `MtgMcp.Playgroup` | Pinned official Public API contract |
| `MtgMcp.Spellbook` | Bounded Commander Spellbook source evidence and cache |
| `MtgMcp.Statistics` | BCL-only exact calculations |
| `MtgMcp.OnCurve` | Pure, repeatable sampled cast-by-turn calculation |
| `MtgMcp.App` | MCP host, configuration, composition, modes, and schemas |

Core references no adapter or host. Provider adapters do not reference one
another. App wires the server together.

## Available tools

The server uses stdio and registers static tools for one session. It exposes:

- 29 `deck_*` tools;
- 18 `scryfall_*` tools;
- 8 `stats_*` tools;
- 23 opt-in `archidekt_*` tools;
- 16 opt-in `playgroup_*` tools;
- 3 opt-in `spellbook_*` tools;
- `mtg://server/capabilities`; and
- zero prompts.

Toolsets choose which tools appear. Modes control what the server may change.
See [Toolsets](toolsets.md).

## Data

The `v0.9` application-data root contains independent stores:

- `decks.db` for local decks, provider bindings, and sync baselines; and
- `scryfall.db` for card facts, rulings, community tags, request snapshots,
  leases, and versions of installed card data.

The server does not migrate or modify legacy data. Multiple MCP processes reuse
the same stores. SQLite coordinates Scryfall pacing and activation of a
completed card-data version across processes.

## Evidence flow

```text
Provider or local store
        |
        v
Owning adapter or deck store
        |
        v
Shared result and evidence types
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

`deck_on_curve_estimate` is the only sampled estimate. App reads the saved deck
and installed Scryfall facts, then passes a resolved request to `MtgMcp.OnCurve`.
The calculation uses only caller-supplied land rules and returns its seed,
uncertainty range, and limits. It does not parse card text or make a deck choice.

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

Architecture tests enforce project references, exact tool registration, toolset
membership, operation-mode visibility, and absence of forbidden legacy tools.
Each production assembly must maintain at least 90 percent line coverage.

Read [North Star](north-star.md), [Design Goals](design-goals.md), and the
[rewrite guide](rewrite-guide.md) for product constraints.
