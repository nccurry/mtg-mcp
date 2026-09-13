# MCP Toolsets

Toolsets limit the list of tools an LLM sees. They do not grant permission to
write.

## Select toolsets

Set `--toolsets`, `MTGMCP__TOOLSETS`, or `TOOLSETS` in `mtg-mcp.json`.

```text
mtg-mcp --toolsets=default
mtg-mcp --toolsets=decks,stats
mtg-mcp --toolsets=spellbook
mtg-mcp --toolsets=none
```

| Value | Enabled toolsets |
| --- | --- |
| Omitted or `default` | `decks,scryfall,stats` |
| `all` | `decks,scryfall,stats,archidekt,playgroup,spellbook` |
| `none` | None |
| Exact comma-separated list | Only the named toolsets |

Unknown names fail startup. Selection is fixed for the session. The server does
not advertise dynamic tool-list changes.

## Combine toolsets with modes

The available tools are those in the selected toolsets that the operation mode
allows.

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 33 | 55 | 55 |
| `all` | 61 | 84 | 97 |
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
| Commander Spellbook variants or combo evidence for an existing local deck | `spellbook` |

The default profile supports local deckbuilding without loading provider-account
tools. Enable Archidekt, Playgroup, or Spellbook only for those workflows.

Use `all` for deliberate broad investigation or diagnostics. For ordinary
work, name only the toolsets the session needs. Start a new MCP session to
change the selection.

The three `spellbook` tools are read-only in every mode. They return Commander
Spellbook evidence, not a recommendation about a deck or combo.

Read `mtg://server/capabilities` to inspect the active selection and exact tool
count.
