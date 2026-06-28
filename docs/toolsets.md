# MCP Toolsets

`MtgMcp.Toolsets` controls which MCP tools the server advertises. Blank is the
compatibility profile: it advertises every tool allowed by the current operation
mode. Set a comma-separated list to narrow the advertised surface:

```bash
MTGMCP__TOOLSETS=cards,workspace,analysis
```

Toolset filtering intersects with operation-mode filtering:

- `read-only`: advertises read tools only.
- `plan`: advertises read tools and local planning-state tools.
- `apply`: advertises all selected tools.

A practical starter profile for local deck work is
`cards,workspace,editing,analysis,plans,simulation,recommendations,sources,combos`.
Add `archidekt`, `playgroup`, `intent`, or `facets` when those workflows are needed.

## Current Toolsets

| Toolset | Scope |
|---|---|
| `analysis` | Deck summaries, structure, mana, cost, bracket, weak-spot, and re-evaluation tools. |
| `archidekt` | Archidekt deck, folder, checkpoint, copy, and comparison tools. |
| `cards` | Scryfall card search, single/batch lookup, image links, rulings, and prints. |
| `combos` | Combo lookup and win-route classification. |
| `editing` | Deck and category mutation tools. |
| `facets` | Card/deck facet read tools and local facet annotations. |
| `intent` | Deck intent get, suggest, set, and clear tools. |
| `plans` | Deck edit plan create, preview, clone, list, get, delete, apply, and package preview tools. |
| `playgroup` | Playgroup.gg read and ranking tools. |
| `recommendations` | Commander evidence, card query, swap review, ramp evaluation, batch tuning, and playgroup-meta scoring. |
| `server` | Server info. |
| `simulation` | Goldfish, board projection, win-turn, performance, and performance comparison tools. |
| `sources` | Source status, evidence search, Commander trends, and exemplar/lesser-known card tools. |
| `workspace` | Workspace lifecycle, listing, parsing, validation, diff, export, refresh, and local checkpoints. |

## Workflow Coverage

| Workflow | Minimum toolsets |
|---|---|
| Look up individual cards and rulings | `cards` |
| Start, open, validate, export, and diff local workspaces | `workspace` |
| Edit decks and categories | `workspace`, `editing` |
| Create and apply edit plans | `workspace`, `plans`, `editing` |
| Run deck analysis and re-evaluation | `workspace`, `analysis` |
| Run goldfish and performance simulations | `workspace`, `simulation` |
| Query recommendation sources | `workspace`, `recommendations`, `sources` |
| Use Commander combo evidence | `workspace`, `recommendations`, `combos` |
| Use Archidekt writeback and remote checkpoints | `workspace`, `archidekt` |
| Use Playgroup.gg ranking context | `playgroup` |

Phase 1 consolidation work should update this file before removing or merging
tools so every README workflow remains mapped to a supported toolset.
