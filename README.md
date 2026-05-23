# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

[![CI](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/nccurry/mtg-mcp/branch/main/graph/badge.svg)](https://codecov.io/gh/nccurry/mtg-mcp)
[![NuGet](https://img.shields.io/nuget/v/Nccurry.MtgMcp?label=NuGet)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nccurry.MtgMcp?label=downloads)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-io.github.nccurry%2Fmtg--mcp-0f766e)](https://registry.modelcontextprotocol.io/?q=io.github.nccurry%2Fmtg-mcp)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue.svg)](LICENSE)

`mtg-mcp` is an unofficial MCP server for building and tuning Magic: The Gathering
decks with Scryfall card data and optional Archidekt writeback.

It is not affiliated with Hasbro, Wizards of the Coast, Magic: The Gathering,
Scryfall, or Archidekt.

## What it does

- Search Scryfall for cards, prints, rulings, and card suggestions.
- Create, import, export, and manage local decks.
- Open Archidekt decks and optionally write changes back to Archidekt.
- Add, remove, move, categorize, and update cards and decks.
- Analyze mana, cost, curve, consistency, draw odds, legality, brackets, and power.
- Expose factual card facets and count cards from explicit caller-supplied predicates.
- Compare decks to Commander heuristics, global popularity context, and recent
  card releases.
- Gather deterministic card data from explicit Scryfall queries, then persist
  caller-supplied add/remove plans.
- Detect Commander Spellbook combos and near-misses, then estimate combo pressure.
- Aggregate normalized corpus signals for trends, lesser-known cards, exemplar decks, and source-backed budget data.
- Simulate goldfish development, projected board states, and likely win turns.
- Preview persisted edit plans before applying them.

## Quickstart

Install the .NET tool from NuGet:

```powershell
dotnet tool install --global Nccurry.MtgMcp
```

Smoke-test the installed command:

```powershell
mtg-mcp --smoke
```

Configure your MCP client to run the `mtg-mcp` stdio command. Scryfall lookup
and local deck analysis do not require an API key.

For Codex:

```powershell
codex mcp add mtg-mcp `
  --env MTGMCP__OPERATION_MODE=plan `
  -- mtg-mcp
```

For clients that use JSON MCP config:

```json
{
  "mcpServers": {
    "mtg-mcp": {
      "command": "mtg-mcp",
      "env": {
        "MTGMCP__OPERATION_MODE": "plan"
      }
    }
  }
}
```

Use `plan` by default. Switch to `apply` when you want the assistant to create
local decks, change deck contents, create checkpoints, or write back to
Archidekt.

## Configuration

Most users only need this:

```powershell
$env:MTGMCP__OPERATION_MODE="plan"
$env:MTGMCP__DATA_DIR="$env:LOCALAPPDATA\mtg-mcp"
```

`MTGMCP__OPERATION_MODE` accepts:

- `read-only`: allow lookup and analysis tools only.
- `plan`: allow lookup, analysis, metadata refresh, and explicit edit plans.
- `apply`: allow deck edits, checkpoints, and Archidekt writeback.

`MTGMCP__INTELLIGENCE__ANALYSIS_DEPTH` controls how much corpus-aware
data tools request and return:

- `minimal`: compact, high-signal evidence with fewer source calls.
- `balanced`: default source breadth and compact evidence.
- `best`: wider enabled source set and richer evidence for deeper analysis.

Supported settings:

| Setting | Use |
| --- | --- |
| `MTGMCP__OPERATION_MODE` | Safety mode: `read-only`, `plan`, or `apply`. |
| `MTGMCP__INTELLIGENCE__ANALYSIS_DEPTH` | Corpus analysis depth: `minimal`, `balanced`, or `best`. |
| `MTGMCP__DATA_DIR` | Local storage for decks, plans, and cached data. |
| `MTGMCP__INTELLIGENCE__CACHE__MODE` | Corpus source-fact cache: `persisted`, `memory`, or `off`. |
| `MTGMCP__INTELLIGENCE__CACHE__MAX_BYTES` | Persisted cache size limit. Default: `104857600`. |
| `MTGMCP__INTELLIGENCE__CACHE__MAX_ENTRIES` | Cache entry limit. Default: `5000`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__SCRYFALL_CARD_METADATA` | Scryfall card metadata TTL. Default: `7d`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__SCRYFALL_SEARCH` | Scryfall search and EDHREC-rank TTL. Default: `24h`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__COMMANDERSPELLBOOK` | Commander Spellbook combo lookup TTL. Default: `24h`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__DECK_SEARCH` | Deck search API TTL. Default: `6h`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__DECK_DETAILS` | Individual deck detail API TTL. Default: `7d`. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__CORPUS_SIGNALS` | Normalized corpus signal report TTL. Default: `6h`. |
| `MTGMCP__INTELLIGENCE__SOURCES__SCRYFALL__ENABLED` | Enable or disable Scryfall metadata corpus evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__COMMANDERSPELLBOOK__ENABLED` | Enable or disable Commander Spellbook corpus evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__ENABLED` | Enable or disable TopDeck.gg corpus evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__API_KEY` | TopDeck.gg API key for tournament decklist evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__BASE_ADDRESS` | Override TopDeck.gg API URL for tests or mirrors. |
| `MTGMCP__INTELLIGENCE__SOURCES__SPICERACK__ENABLED` | Enable or disable Spicerack corpus evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__SPICERACK__API_KEY` | Spicerack API key for recent public decklist evidence. |
| `MTGMCP__INTELLIGENCE__SOURCES__SPICERACK__BASE_ADDRESS` | Override Spicerack API URL for tests or mirrors. |
| `MTGMCP__ARCHIDEKT__CREDENTIALS_FILE` | JSON credentials file for Archidekt. |
| `MTGMCP__ARCHIDEKT__JWT` | Optional Archidekt JWT. |
| `MTGMCP__ARCHIDEKT__REFRESH_TOKEN` | Preferred Archidekt auth token. |
| `MTGMCP__ARCHIDEKT__USER_ID` | Archidekt user id used with token auth. |
| `MTGMCP__ARCHIDEKT__EMAIL` | Fallback Archidekt login email. |
| `MTGMCP__ARCHIDEKT__USERNAME` | Fallback Archidekt login username. |
| `MTGMCP__ARCHIDEKT__PASSWORD` | Fallback Archidekt login password. |
| `MTGMCP__SCRYFALL__BASE_ADDRESS` | Override Scryfall API URL for tests or mirrors. |
| `MTGMCP__SCRYFALL__USER_AGENT` | Override Scryfall user agent. |
| `MTGMCP__SCRYFALL__MAX_RATE_LIMIT_RETRIES` | Number of Scryfall `429` retries before surfacing a failure. Default: `3`. |
| `MTGMCP__COMMANDERSPELLBOOK__BASE_ADDRESS` | Override Commander Spellbook API URL. |

`mtg-mcp.json` is the only JSON config file mtg-mcp reads. Environment
variables should use the `MTGMCP__...` names above; duplicate bare aliases such
as `MODE` or `ANALYSIS_DEPTH` are not supported.

Example `mtg-mcp.json`:

```json
{
  "MtgMcp": {
    "OperationMode": "plan",
    "DataDir": "C:/Users/you/AppData/Local/mtg-mcp",
    "Intelligence": {
      "AnalysisDepth": "balanced",
      "Cache": {
        "Mode": "persisted",
        "MaxBytes": 104857600,
        "MaxEntries": 5000,
        "Ttls": {
          "ScryfallCardMetadata": "7d",
          "ScryfallSearch": "24h",
          "CommanderSpellbook": "24h",
          "DeckSearch": "6h",
          "DeckDetails": "7d",
          "CorpusSignals": "6h"
        }
      },
      "Sources": {
        "Scryfall": { "Enabled": true },
        "CommanderSpellbook": { "Enabled": true },
        "TopDeck": {
          "Enabled": true,
          "ApiKey": "..."
        },
        "Spicerack": {
          "Enabled": true,
          "ApiKey": "..."
        }
      }
    },
    "Scryfall": {
      "MaxRateLimitRetries": 3
    }
  }
}
```

Corpus recommendations query structured APIs on demand and cache source facts
under `DataDir/corpus-cache`. The cache is shared across agents using the same
data directory. It does not store final recommendations, prompt rationale, or
deckbuilding opinions. Pass `refresh=true` to corpus tools when you want one
call to bypass fresh cache entries.

The corpus source policy is API-only: official/documented APIs and unofficial
structured JSON endpoints may be used when clearly labeled, but mtg-mcp does
not scrape HTML, parse page markup, or use browser automation for corpus data.
`mtg://corpus/sources` reports enabled, missing-key, disabled, unsupported, and
permission-sensitive source states.

Archidekt credentials are only needed for private decks, account-bound deck data,
checkpoints, or writeback. Create a credentials file:

```powershell
mtg-mcp auth archidekt `
  --credentials-file "$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  --refresh-token "..." `
  --user-id "..."
```

Then pass it to the MCP server:

```powershell
codex mcp add mtg-mcp `
  --env MTGMCP__OPERATION_MODE=apply `
  --env MTGMCP__ARCHIDEKT__CREDENTIALS_FILE="$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  -- mtg-mcp
```

JWT or refresh token auth is preferred. Email or username plus password login is
available as a fallback.

Inside an MCP client, these resources help verify setup:

- `mtg://config/effective` shows non-secret effective configuration.
- `mtg://corpus/sources` shows enabled and planned corpus sources.
- `mtg://archidekt/auth-status` shows redacted Archidekt credential status.

## How to use it

Ask your MCP client for the deckbuilding task you want done:

```text
Search for blue one-mana cantrips legal in Commander.
```

Prompts that create, open, or change decks need `apply` mode:

```text
Import this decklist as a local Commander deck and summarize the plan.
```

```text
Open this Archidekt deck locally, analyze the mana base, and suggest fixes under $10.
```

```text
Find budget replacements for cards over $20 and preview the plan before changing anything.
```

```text
Find new cards for this deck from the last year, or pass a YYYY-MM-DD since date.
```

```text
Tell me what new or lesser-known cards are showing signal for this commander, using best analysis.
```

```text
Show top exemplar decks and explain the source evidence for Skullclamp in this deck.
```

```text
Apply the previewed deck plan and create an Archidekt checkpoint first.
```

Common tuning tools include `refresh_deck_card_snapshots`,
`summarize_deck_workspace`, `analyze_deck_consistency`,
`get_card_facets`, `count_deck_cards_matching`, `query_cards_for_deck`,
`create_deck_plan_from_explicit_changes`, and `preview_deck_plan`.

Facet tools are deliberately fact-first. For example,
`count_deck_cards_matching` does not decide what "card advantage" means; the
caller supplies a JSON predicate over facets such as `scryfall.oracle_text`,
`workspace.categories`, `user.tags`, or locally stored `tagger.oracle_tags`, and
the tool returns matching cards plus the exact evidence rows.

## Deck Intent Configuration

Deck intent is optional text that tells analyses and caller-supplied queries what
the deck is trying to do.

For local decks, use `suggest_deck_intent`, edit the text, then save it with
`set_deck_intent`. For Archidekt decks, the same block is stored in the deck
description and writes back only when Archidekt writeback is enabled.

Best-practice analysis uses `Power Level`, `Heuristic Profile`,
`Package Template`, `Local Meta`, and `Packages` to choose and compare
Commander heuristics. Query and planning workflows can use `Targets`, `Budget`,
`Prefer`, `Avoid`, and `Protect` as explicit constraints.

```text
MTG MCP Deck Intent
Version: 1
Format: commander
Commander: Teysa Karlov
Power Level: tuned-casual
Heuristic Profile: command-zone-template
Package Template: none
Local Meta: go-wide tokens, graveyards

Targets
Ramp: 8-10
Draw: 10-12
Interaction: 10-14

Avoid
- deterministic infinite combos
End MTG MCP Deck Intent
```

These fields shape analyses and explicit query/planning workflows:

- `Targets`: desired counts for roles or tags, such as `Ramp: 8-10`.
- `Budget`: price guidance for upgrades and replacements.
- `Prefer`: effects, themes, or cards to bias toward.
- `Avoid`: effects, themes, or cards to keep out.
- `Protect`: cards or packages that should not be cut casually.
- `Power Level`: table strength. Values: `precon`, `casual`,
  `tuned-casual`, `high-power`, `cedh`.

`Heuristic Profile`: `auto`, `commander-baseline`, `command-zone-template`,
`edhrec-foundation`, `mana-rich-39-land`, `fifty-mana-sources`, `package-8x8`,
`package-7x9`, `package-9x7`, `seventy-five-percent`, `cedh-turbo`,
`cedh-midrange`, `cedh-stax`, `cedh-tempo`.

`Package Template`: `none`, `8x8`, `7x9`, `9x7`.

Values are case-insensitive. Spaces and underscores normalize to hyphens.
Aliases include `upgraded-precon` -> `casual`, `mid-power` -> `tuned-casual`,
`optimized` -> `high-power`, and `competitive` or `cEDH` -> `cedh`.

Package brews may add a `Packages` section. Counts use the same syntax as
`Targets`: `8`, `6-9`, or `4+`.

Heuristics are advisory. Sources include the
[Command Zone Template](https://edh.fandom.com/wiki/Command_Zone_Template),
[8x8 Theory](https://the8x8theory.tumblr.com/what-is-the-8x8-theory),
[7x9 / 8x8 / 9x7 templates](https://edh.fandom.com/wiki/7_by_9),
[EDHREC deckbuilding guide](https://edhrec.com/guides/how-to-build-a-commander-deck),
[EDHREC mana-base foundations](https://edhrec.com/articles/foundations-how-to-build-mana-bases),
[75% Commander](https://edh.fandom.com/wiki/75_Percent),
[EDHREC cEDH intro](https://edhrec.com/guides/intro-to-cedh), and
[Commander's Herald cEDH guide](https://commandersherald.com/a-beginners-guide-to-cedh/).

## How it works

`mtg-mcp` runs as a stdio MCP server. Your MCP client calls its tools to search
Scryfall, manage decks, analyze deck structure, and optionally sync changes to
Archidekt.

Local deck data and edit plans are saved under `MTGMCP__DATA_DIR`.
Archidekt writeback is opt-in: the deck must be opened with writeback enabled,
and the server must be running in `apply` mode before deck contents are changed.
