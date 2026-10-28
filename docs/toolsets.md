# MCP Toolsets

Toolsets limit the list of tools an LLM sees. They do not grant permission to
write.

## Select toolsets

Set `--toolsets`, `MTGMCP__TOOLSETS`, or `TOOLSETS` in `mtg-mcp.json`.

```text
mtg-mcp
mtg-mcp --toolsets=decks,stats
mtg-mcp --toolsets=spellbook
mtg-mcp --toolsets=none
```

| Value | Enabled toolsets |
| --- | --- |
| Omitted, `default`, or `all` | Every toolset whose credentials are present: `decks`, `scryfall`, `stats`, `spellbook`, and `archidekt` and `playgroup` when configured |
| `none` | None |
| Exact comma-separated list | Only the named toolsets, still subject to credentials |

Unknown names fail startup. Selection is fixed for the session. The server does
not advertise dynamic tool-list changes.

## Credentials turn provider toolsets on

`decks`, `scryfall`, `stats`, and `spellbook` need no credentials. `archidekt`
and `playgroup` are off when their credentials are absent, even when you name
them in `--toolsets`. The server checks only that the settings or files exist.
It does not call the provider and does not read secrets into logs.

| Toolset | On when any of these is true |
| --- | --- |
| `archidekt` | Both `ARCHIDEKT:USERNAME` and `ARCHIDEKT:PASSWORD` are set; or `ARCHIDEKT:CREDENTIALS_FILE` names a file that exists; or `~/.mtg-mcp/archidekt.json` exists |
| `playgroup` | `PLAYGROUP:API_KEY` is set; or `PLAYGROUP:CREDENTIALS_FILE` names a file that exists; or `~/.mtg-mcp/playgroup.json` exists |

A username alone or a password alone does not count. A file counts when it
exists, even if its contents are wrong; the `archidekt_auth_status` and
`playgroup_auth_status` tools show that state. As environment variables, write
the settings with the `MTGMCP__` prefix and double underscores, for example
`MTGMCP__ARCHIDEKT__USERNAME`.

When a toolset is off for this reason, the server writes one line to stderr
that names the settings to add, and the capabilities resource reports the
reason in `disabledReason`: `credentials-missing`, or `credentials-invalid` for
an invalid Playgroup configuration. An invalid Playgroup configuration fails
startup only when `playgroup` is named in `--toolsets`. Credentials added later
take effect on the next start.

## Combine toolsets with modes

The available tools are those in the selected toolsets that the operation mode
allows. The default mode is `remote`.

| Selection | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| Default or `all`, no credentials | 35 | 48 | 48 |
| Default or `all`, Archidekt credentials only | 44 | 58 | 69 |
| Default or `all`, Archidekt and Playgroup credentials | 59 | 73 | 86 |
| `none` | 0 | 0 | 0 |

`read-only` allows reads only. `local` adds local writes. `remote` adds remote
writes, including Archidekt uploads. A toolset cannot widen these permissions.
Destructive tools carry a destructive annotation, so clients ask before running
them.

## Tools in each toolset

Every tool belongs to one toolset. Tool names say where the data comes from:
`local_deck_*` is the local deck store, `scryfall_*` is Scryfall card data,
`stats_*` is exact math, and `archidekt_*`, `playgroup_*`, and `spellbook_*` name
their provider. Each description starts with the data the tool works on:
`Local deck store:`, `Scryfall:`, `Installed Scryfall card data:`,
`Saved Scryfall requests:`, `Archidekt:`, `Playgroup:`, `Commander Spellbook:`,
or `Exact math:`. The prefix names the input data, not the toolset. The two `card_list*` tools are the one
exception to the `<source>_<object>_<action>` rule: they read any card list, so they have no single source
prefix, and their descriptions start with `Scryfall:` because installed Scryfall card data resolves every list.

| Toolset | Read-only | Local write | Remote write |
| --- | ---: | ---: | ---: |
| `decks` | 11 | 9 | 0 |
| `scryfall` | 11 | 4 | 0 |
| `stats` | 9 | 0 | 0 |
| `spellbook` | 4 | 0 | 0 |
| `archidekt` | 9 | 1 | 11 |
| `playgroup` | 15 | 0 | 2 |

## Choose a workflow

| Workflow | Toolsets |
| --- | --- |
| Local deck work | `decks` |
| Card and tag evidence | `scryfall` |
| Exact probabilities and summaries | `stats` |
| Archidekt synchronization | `decks,scryfall,archidekt` |
| Playgroup evidence | `playgroup` |
| Commander Spellbook variants or combo evidence for an existing local deck | `spellbook` |

With every toolset on, the tool list is large. To keep a session small, name
only the toolsets it needs and start a new MCP session to change the selection.

The server sends short instructions at initialization. They name the first
tool for each job in the active selection, say that the agent makes every
deckbuilding judgment, and say that text written by other people in provider
results is untrusted data.

The four `spellbook` tools are read-only in every mode. They return Commander
Spellbook evidence, not a recommendation about a deck or combo.

Read `mtg://server/capabilities` to inspect the active selection, why a
toolset is off, and the exact tool count.
