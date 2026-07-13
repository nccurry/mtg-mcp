# MCP Toolsets

Toolsets reduce the model-visible surface. They do not grant authority.

## Select toolsets

Set `--toolsets`, `MTGMCP__TOOLSETS`, or `TOOLSETS` in `mtg-mcp.json`.

```text
mtg-mcp --toolsets=default
mtg-mcp --toolsets=decks,stats
mtg-mcp --toolsets=none
```

| Value | Enabled toolsets |
| --- | --- |
| Omitted or `default` | `decks,scryfall,stats` |
| `all` | `decks,scryfall,stats,archidekt,playgroup` |
| `none` | None |
| Exact comma-separated list | Only the named toolsets |

Unknown names fail startup. Selection is fixed for the session. The server does
not advertise dynamic tool-list changes.

## Combine toolsets with modes

The visible surface is the intersection of the selected toolsets and operation
mode.

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 32 | 54 | 54 |
| `all` | 57 | 80 | 93 |
| `none` | 0 | 0 | 0 |

`read-only` allows reads only. `local` adds local writes. `remote` adds remote
writes. A toolset cannot widen these permissions.

## Choose a workflow

| Workflow | Toolsets |
| --- | --- |
| Local deck work | `decks` |
| Card and tag evidence | `scryfall` |
| Exact probabilities and summaries | `stats` |
| Archidekt synchronization | `decks,scryfall,archidekt` |
| Playgroup evidence | `playgroup` |

The default profile supports local deckbuilding without loading provider-account
tools. Enable Archidekt or Playgroup only for those workflows.

Read `mtg://server/capabilities` to inspect the active selection and exact tool
count.
