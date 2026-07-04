# MCP Toolsets

## Clean-Break `0.9.0` Toolsets

The current host selects a static model-visible surface at startup. Omitted
configuration or `default` enables implemented default toolsets, `all` enables
every implemented stable toolset, `none` exposes no tools, and an exact
comma-separated lowercase list selects an explicit subset.

```bash
MTGMCP__TOOLSETS=decks
mtg-mcp --toolsets=none
```

`decks` and `scryfall` are implemented today; unimplemented names fail startup
and do not appear as capability placeholders. Accepted AMEND-004 defines the
stable target names as `decks`, `scryfall`, `stats`, `archidekt`, and
`playgroup`. The current default is `decks,scryfall`; exact statistics will join
it after its own child is implemented. Archidekt and Playgroup remain opt-in.
There is no Tagger descriptor, prefix, database, or compatibility alias.

Toolsets control relevance. The independently configured `read-only`, `local`,
and `remote` modes control authority, and invocation-time write guards remain
mandatory. Selection is fixed for a session and the server advertises no
dynamic tool-list change. See the
[capability-toolset PLC](llms/plcs/completed/mcp-capability-toolsets/README.md)
and [rewrite guide](rewrite-guide.md).

## Historical Legacy Filter

The remainder of this file documents the removed pre-rewrite filter. The
clean-break design does not preserve these names or counts.

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
Add `archidekt`, `playgroup`, `intent`, `facets`, or `collection` when those
workflows are needed.

## Historical Legacy Toolsets

| Toolset | Scope |
|---|---|
| `analysis` | Deck summaries, structure, mana, cost, bracket, weak-spot, and re-evaluation tools. |
| `archidekt` | Archidekt deck, folder, checkpoint, copy, and comparison tools. |
| `cards` | Scryfall card search, single/batch lookup, image links, rulings, and prints. |
| `collection` | Local collection get/set tools and workspace ownership diffs. |
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
| Check owned cards for a workspace | `workspace`, `collection` |
| Use Archidekt writeback and remote checkpoints | `workspace`, `archidekt` |
| Use Playgroup.gg ranking context | `playgroup` |

This taxonomy is historical and must not be extended. Rewrite work updates the
active child toolset assignment, profile matrix, capability resource, and
cutover crosswalk instead.
