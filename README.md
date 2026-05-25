# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

[![CI](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/nccurry/mtg-mcp/branch/main/graph/badge.svg)](https://codecov.io/gh/nccurry/mtg-mcp)
[![NuGet](https://img.shields.io/nuget/v/Nccurry.MtgMcp?label=NuGet)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nccurry.MtgMcp?label=downloads)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-io.github.nccurry%2Fmtg--mcp-0f766e)](https://registry.modelcontextprotocol.io/?q=io.github.nccurry%2Fmtg-mcp)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue.svg)](LICENSE)

`mtg-mcp` is an unofficial MCP server for Magic: The Gathering deckbuilding. It
connects MCP clients to Scryfall card data, local deck workspaces, optional
Moxfield imports, Archidekt writeback, Playgroup.gg playgroup data, Commander
Spellbook combos, and API-backed deckbuilding evidence.

It is not affiliated with Hasbro, Wizards of the Coast, Magic: The Gathering,
Scryfall, Moxfield, Archidekt, Playgroup.gg, or Commander Spellbook.

## Quickstart

```powershell
dotnet tool install --global Nccurry.MtgMcp
mtg-mcp --smoke
```

Configure your MCP client to run the `mtg-mcp` stdio command. Scryfall lookup
and local deck analysis work without account credentials.

Codex example:

```powershell
codex mcp add mtg-mcp `
  --env MTGMCP__OPERATION_MODE=plan `
  -- mtg-mcp
```

JSON MCP client example:

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

Set `MTGMCP__OPERATION_MODE` explicitly:

- `read-only`: lookup and analysis only.
- `plan`: lookup, analysis, metadata refresh, and saved edit plans.
- `apply`: deck edits, checkpoints, and Archidekt writeback. Writeback still
  requires opening the Archidekt workspace with writeback enabled.

## Features

| Area | What the MCP exposes |
| --- | --- |
| Card data | Scryfall search, fuzzy card lookup, prints, rulings, suggestions, and Scryfall query syntax guidance. |
| Workspaces | Create, import, parse, export, open, validate, summarize, migrate, and update local or Archidekt-backed decks. |
| Deck editing | Add, remove, move, categorize, annotate, and set quantities; create, preview, list, apply, or delete persisted edit plans. |
| Moxfield | Import public or unlisted decks as generic local workspaces while preserving boards, tags, and print metadata when available. |
| Archidekt | Create decks, open decks, list visible decks, write back when enabled, copy local workspaces into Archidekt, and manage deck checkpoints. |
| Playgroup.gg | Check auth, get playgroups and decks, list playgroup users/decks, list user decks, rank decks by power, Elo, win rate, competitive rating, games played, or average win turn, and score candidate cards against local-meta pressure. |
| Analysis | Mana base, curve, colors, categories, cost, legality, draw odds, consistency, best practices, Commander bracket, card facets, and explicit facet predicates. |
| Simulation | Goldfish runs, projected board states, win-turn estimates, deterministic performance analysis, plan comparisons, and Archidekt reference comparisons. |
| Recommendations | New releases, Commander meta context, caller-supplied Scryfall queries, lesser-known cards, commander trends, exemplar decks, raw source evidence, and Reddit discussion evidence. |
| Combos | Completed combos, near-misses, and combo pressure using Commander Spellbook or local heuristics. |
| Deck intent | Optional human-readable deck goals, budgets, local meta, role targets, simulation profiles, win routes, preferences, avoided cards, and protected cards. |

Most users can ask naturally instead of naming tools:

```text
Open this Archidekt deck locally, analyze the mana base, and suggest fixes under $10.
```

```text
Import this Moxfield deck, dry-run copying it to a new private Archidekt deck, and preserve its tags.
```

Moxfield role tags import as secondary workspace categories. When copied to
Archidekt, those tag categories are marked as not included in deck totals so
Mainboard, Commander, and other board categories still control legality and
deck size. Existing Archidekt copies can be repaired or refreshed with
`copy_workspace_to_archidekt` using `replaceExistingDestination=true`.

```text
Find budget replacements for cards over $20 and preview the plan before changing anything.
```

```text
List decks from this Playgroup URL and rank them by win rate.
```

```text
Find new cards for this deck from the last year and explain the source evidence.
```

```text
Goldfish this deck through turn 6 and compare the previewed plan against it.
```

## Configuration

`mtg-mcp.json` is the only JSON config file the server reads. Environment
variables use the `MTGMCP__...` names below; the equivalent JSON path is under
`MtgMcp`. For example, `MTGMCP__PLAYGROUP__CREDENTIALS_FILE` maps to
`MtgMcp.Playgroup.CredentialsFile`.

Minimal `mtg-mcp.json`:

```json
{
  "MtgMcp": {
    "OperationMode": "plan",
    "DataDir": "C:/Users/you/AppData/Local/mtg-mcp"
  }
}
```

Common credential config:

```json
{
  "MtgMcp": {
    "Archidekt": {
      "CredentialsFile": "C:/Users/you/.mtg-mcp/archidekt.json"
    },
    "Playgroup": {
      "CredentialsFile": "C:/Users/you/.mtg-mcp/playgroup.json"
    }
  }
}
```

`archidekt.json`:

```json
{
  "refreshToken": "...",
  "userId": "..."
}
```

`playgroup.json`:

```json
{
  "apiKey": "..."
}
```

You can also create an Archidekt credentials file with:

```powershell
mtg-mcp auth archidekt `
  --credentials-file "$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  --refresh-token "..." `
  --user-id "..."
```

Supported environment settings. In rows with slashes, repeat the full prefix for
each abbreviated suffix.

| Setting | Use |
| --- | --- |
| `MTGMCP__OPERATION_MODE` | `read-only`, `plan`, or `apply`. Set explicitly; the app default is `apply`. |
| `MTGMCP__DATA_DIR` | Local decks, plans, workspaces, and corpus cache. |
| `MTGMCP__INTELLIGENCE__ANALYSIS_DEPTH` | Corpus depth: `minimal`, `balanced`, or `best`. |
| `MTGMCP__INTELLIGENCE__CACHE__MODE` | Source-fact cache: `persisted`, `memory`, or `off`. |
| `MTGMCP__INTELLIGENCE__CACHE__MAX_BYTES` / `MAX_ENTRIES` | Persisted cache limits. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__SCRYFALL_CARD_METADATA` / `SCRYFALL_SEARCH` / `COMMANDERSPELLBOOK` / `DECK_SEARCH` / `DECK_DETAILS` / `CORPUS_SIGNALS` | Per-source cache TTLs such as `24h` or `7d`. |
| `MTGMCP__INTELLIGENCE__SOURCES__SCRYFALL__ENABLED` / `SCRYFALL_TAGGER__ENABLED` / `COMMANDERSPELLBOOK__ENABLED` / `TOPDECK__ENABLED` / `SPICERACK__ENABLED` / `EDHTOP16__ENABLED` / `REDDIT__ENABLED` | Enable or disable corpus sources. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__API_KEY` / `SPICERACK__API_KEY` / `REDDIT__API_KEY` | Optional source API keys. |
| `MTGMCP__INTELLIGENCE__SOURCES__EDHTOP16__ALLOW_UNOFFICIAL_API` / `REDDIT__ALLOW_UNOFFICIAL_API` | Allow bounded unofficial structured JSON endpoints for those sources. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__BASE_ADDRESS` / `SPICERACK__BASE_ADDRESS` / `EDHTOP16__BASE_ADDRESS` / `REDDIT__BASE_ADDRESS` | Source API URL overrides. |
| `MTGMCP__ARCHIDEKT__BASE_ADDRESS` / `CREDENTIALS_FILE` / `JWT` / `REFRESH_TOKEN` / `USER_ID` / `EMAIL` / `USERNAME` / `PASSWORD` | Archidekt API and credential settings. Refresh token auth is preferred; email or username plus password is fallback. |
| `MTGMCP__ARCHIDEKT__RATE_LIMIT__MAX_REQUESTS` / `WINDOW_SECONDS` | Optional process-local Archidekt pacing. For example, `30` requests per `60` seconds leaves room for browser activity; `0` max requests disables proactive pacing. |
| `MTGMCP__MOXFIELD__BASE_ADDRESS` / `USER_AGENT` / `CURL_FALLBACK_ENABLED` / `CURL_PATH` | Moxfield import endpoint settings. Imports use an anonymous, unofficial endpoint; when Moxfield blocks .NET HTTP requests, the adapter can retry through `curl` if available. |
| `MTGMCP__PLAYGROUP__BASE_ADDRESS` / `API_KEY` / `CREDENTIALS_FILE` | Playgroup.gg API settings. Credential files may use JSON or `apiKey=value`, `accessToken=value`, or `token=value` lines. |
| `MTGMCP__SIMULATION__PROFILE_PATHS__0` / `MTGMCP__SIMULATION__ALLOW_EXTERNAL_PROFILE_OVERRIDES` | Optional external simulation profile JSON files or simple glob paths. Built-in profiles always remain available. |
| `MTGMCP__SCRYFALL__BASE_ADDRESS` / `USER_AGENT` / `MAX_RATE_LIMIT_RETRIES` | Scryfall API settings. |
| `MTGMCP__COMMANDERSPELLBOOK__BASE_ADDRESS` | Commander Spellbook API setting. |

Use these resources inside an MCP client to verify setup without exposing
secrets:

- `mtg://config/effective`
- `mtg://server/info`
- `mtg://archidekt/auth-status`
- `mtg://playgroup/auth-status`
- `mtg://corpus/sources`

## MCP Surface

Useful resources:

- Deck data: `mtg://deck/{deckId}`, `mtg://deck/{deckId}/summary`,
  `mtg://deck/{deckId}/intent`.
- Usage guides: `mtg://scryfall/syntax-cheatsheet`,
  `mtg://formats/{format}/deck-rules`, `mtg://usage/workspace-selection`,
  `mtg://usage/operation-modes`, `mtg://usage/deck-intent`.
- Status: `mtg://config/effective`, `mtg://corpus/sources`,
  `mtg://server/info`, `mtg://archidekt/auth-status`,
  `mtg://playgroup/auth-status`.

Built-in prompts cover brewing, tuning, budget replacements, cost reduction,
power increases or reductions, Commander bracket reduction, mana-base work,
consistency, local meta tuning, new releases, goldfishing, goal-focused
packages, and rules/rulings checks.

For Playgroup-aware tuning, `score_cards_for_playgroup_meta` scores explicit
candidate names, or cards in excluded workspace categories, with visible factor
scores for plan fit, deterministic performance delta, local-meta coverage,
self-harm, price/bracket constraints, and evidence confidence. Playgroup decks
are ranked from fetched game participations; Archidekt decklists are imported
read-only when Playgroup exposes an Archidekt URL.

Simulation results include the resolved simulation profile, why that profile was
chosen, route evidence, and warnings when a claim comes from fallback
heuristics. See [`docs/simulation-profiles.md`](docs/simulation-profiles.md)
for the compact profile, deck-intent, and route syntax reference.

## Deck Intent

Deck intent is optional text stored in a workspace description, and in the
Archidekt deck description when writeback is enabled. Use `suggest_deck_intent`,
`get_deck_intent`, `set_deck_intent`, and `clear_deck_intent`.

Small example:

```text
MTG MCP Deck Intent
Version: 2
Format: commander
Commander: Teysa Karlov
Goal: Aristocrats value with resilient sacrifice engines
Power Level: tuned-casual
Power Target: tuned casual
Heuristic Profile: command-zone-template
Simulation Profile: value
Archetype Tags: aristocrats, tokens, graveyard
Local Meta: graveyards, go-wide tokens
Budget: prefer upgrades under $10

Build Targets
Ramp: 8-10
Draw: 10-12
Interaction: 10-14

Simulation
Mulligan Style: multiplayer-london
Hold Interaction From Turn: 3
Minimum Interaction Held: 1
Prefer Commander On Curve: true

Win Routes
Blood Artist Drain: requires commander, tag:aristocrats, tokens>=4; earliest turn 6; kind finisher

Avoid
- deterministic infinite combos
End MTG MCP Deck Intent
```

Supported power levels are `precon`, `casual`, `tuned-casual`, `high-power`,
and `cedh`. Supported heuristic profiles are `auto`, `commander-baseline`,
`command-zone-template`, `edhrec-foundation`, `mana-rich-39-land`,
`fifty-mana-sources`, `package-8x8`, `package-7x9`, `package-9x7`,
`seventy-five-percent`, `cedh-turbo`, `cedh-midrange`, `cedh-stax`, and
`cedh-tempo`. Supported simulation profiles are `auto`, `neutral`, `aggro`,
`combo`, `control`, `value`, `big-mana`, and `stax`. Package templates are
`none`, `8x8`, `7x9`, and `9x7`.

For the full syntax, read `mtg://usage/deck-intent` or
[`docs/simulation-profiles.md`](docs/simulation-profiles.md).

## How It Works

`mtg-mcp` runs as a stdio MCP server. It stores local workspaces, edit plans,
annotations, and cache data under `MTGMCP__DATA_DIR`.

Archidekt writeback has two gates: the server must run in `apply` mode, and the
deck must be opened with writeback enabled. Multi-card Archidekt edits require
or create a checkpoint before applying a plan.

Card-only edit plans are applied as a single batch. Commander deck-size checks
use the final included card count, so equal add/remove swaps can add before
cutting as long as the finished deck remains legal.

Corpus recommendations query structured APIs on demand and cache source facts
under `DataDir/corpus-cache`. The cache stores source facts, not final
recommendations or prompt rationale. Pass `refresh=true` to supported tools to
bypass fresh cache entries for one call.

The corpus policy is API-only: official/documented APIs and explicitly allowed
unofficial structured JSON endpoints may be used, but mtg-mcp does not scrape
HTML, parse page markup, or use browser automation for corpus data.

## Development

Use `Taskfile.yml` for common workflows:

```powershell
task test
task lint
task install:local
task install:local:cleanup
```

`task install:local` packs a unique local prerelease version, updates the global
`.NET` tool, publishes a self-contained binary, and copies it to the configured
local MCP command path when one is found. If that executable is locked by a
running MCP process, it writes a versioned binary beside it and updates the
Codex MCP config for the next server start.

`task install:local:cleanup` removes old unlocked versioned local binaries while
keeping the currently configured MCP command path.
