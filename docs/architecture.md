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
| `MtgMcp.OnCurve` | Pure, seeded sampling engine for `stats_simulate` |
| `MtgMcp.App` | MCP host, configuration, composition, modes, and schemas |

Core references no adapter or host. Provider adapters do not reference one
another. App wires the server together.

## Available tools

The server uses stdio and registers static tools for one session. It exposes:

- 19 `local_deck_*` tools;
- 13 `scryfall_*` tools and two `card_list*` tools (`card_list_query`, `card_lists_compare`);
- 10 `stats_*` tools;
- 21 `archidekt_*` tools (on when Archidekt credentials are present);
- 17 `playgroup_*` tools (on when a Playgroup key is present);
- 4 `spellbook_*` tools;
- `mtg://server/capabilities`; and
- three workflow prompts (`audit-deck`, `build-from-commander`, and `compare-decks`).

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

`stats_simulate` is the only sampled estimate. App reads the saved deck, resolves the
caller's groups to main-zone entries, and passes plain counts and rule records to
`MtgMcp.OnCurve`, which shuffles with a versioned SplitMix64 generator and applies only
the fixed engine rules and the rules in the request. Its `on-curve` preset reads installed
Scryfall facts and expands to ordinary rules. The result returns its seed, applied rules,
uncertainty ranges, and limits. It does not parse card text or make a deck choice.

## Write flow

Local and remote writes use explicit operations. Existing-deck writes require a
revision. Synchronization and categorization use preview fingerprints. Remote
writes also require provider fingerprints or checksums where supported.

The server refuses stale or tampered requests. It does not choose a conflict
winner. A conflict caused by stale state includes a `current` object with the
current local revision, remote fingerprint, or both, so the caller can preview
again and retry.

## Non-goals

Stable `0.9.0` does not provide:

- deck legality verdicts (Commander rule facts are reported, with no legal or illegal answer);
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
