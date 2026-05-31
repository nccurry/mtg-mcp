# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

<p align="center">
  <img src="assets/logo.svg" alt="mtg-mcp logo" width="220" />
</p>

[![CI](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/nccurry/mtg-mcp/branch/main/graph/badge.svg)](https://codecov.io/gh/nccurry/mtg-mcp)
[![NuGet](https://img.shields.io/nuget/v/Nccurry.MtgMcp?label=NuGet)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nccurry.MtgMcp?label=downloads)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![.NET](https://img.shields.io/badge/.NET-11.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-io.github.nccurry%2Fmtg--mcp-0f766e)](https://registry.modelcontextprotocol.io/?q=io.github.nccurry%2Fmtg-mcp)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue.svg)](LICENSE)

`mtg-mcp` is an unofficial MCP server for Magic: The Gathering deckbuilding. It
connects MCP clients to Scryfall card data, local deck workspaces, optional
Moxfield imports, Archidekt writeback, Playgroup.gg playgroup data, Commander
Spellbook combos, and API-backed deckbuilding evidence.

It is not affiliated with Hasbro, Wizards of the Coast, Magic: The Gathering,
Scryfall, Moxfield, Archidekt, Playgroup.gg, or Commander Spellbook.

## Quickstart

```bash
dotnet tool install --global Nccurry.MtgMcp
mtg-mcp --version
mtg-mcp --smoke
```

Configure your MCP client to run the `mtg-mcp` stdio command. Scryfall lookup
and local deck analysis work without account credentials.

PowerShell note: when invoking the executable by a quoted full path, prefix it
with the call operator:

```powershell
& "C:\Users\you\.dotnet\tools\mtg-mcp.exe" --version
```

Codex example:

```bash
codex mcp add mtg-mcp \
  --env MTGMCP__OPERATION_MODE=plan \
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
| Card data | Scryfall search with optional format legality filtering, fuzzy card lookup, prints, rulings, and Scryfall query syntax guidance. |
| Workspaces | Create, import, parse, export, open, validate, summarize, migrate, and update local or Archidekt-backed decks. |
| Deck editing | Add, remove, move, categorize, annotate, and set quantities; create, preview, list, apply, or delete persisted edit plans. |
| Moxfield | Import public or unlisted decks as generic local workspaces while preserving boards, tags, and print metadata when available. |
| Archidekt | Create decks, open decks, list visible decks, write back when enabled, copy local workspaces into Archidekt, and manage deck checkpoints. |
| Playgroup.gg | Check auth, get playgroups and decks, list playgroup users/decks, list user decks, rank decks by power, Elo, win rate, competitive rating, games played, or average win turn, and score candidate cards against local-meta pressure. |
| Analysis | Mana base, curve, colors, categories, cost, legality, draw odds, consistency, best practices, Commander bracket, card facets, and explicit facet predicates. |
| Simulation | Goldfish runs, projected board states, win-turn estimates, deterministic performance analysis, plan comparisons, and Archidekt reference comparisons. |
| Recommendations | New-card swap evidence, Commander aggregate cards/tags, win-condition evidence, caller-supplied Scryfall queries, lesser-known cards, commander trends, exemplar decks, raw source evidence, and Reddit discussion evidence. |
| Combos | Catalog-backed combo search/details, deck combo analysis, route labels, terminal/needs-payoff flags, near-misses, and clearly separated local heuristics. |
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
`archidekt_copy_workspace` using `replaceExistingDestination=true`.

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
  "username": "you-or-you@example.com",
  "password": "..."
}
```

Archidekt does not expose a public self-service page for generating refresh
tokens, so Archidekt credentials are configured as username and password only.
The `username` value can be your Archidekt username or account email address.

`playgroup.json`:

```json
{
  "apiKey": "..."
}
```

You can also create an Archidekt credentials file with:

```bash
mtg-mcp auth archidekt \
  --credentials-file "$HOME/.mtg-mcp/archidekt.json" \
  --username "you-or-you@example.com" \
  --password "..."
```

PowerShell equivalent:

```powershell
mtg-mcp auth archidekt `
  --credentials-file "$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  --username "you-or-you@example.com" `
  --password "..."
```

You can create a Playgroup credentials file the same way:

```bash
mtg-mcp auth playgroup \
  --credentials-file "$HOME/.mtg-mcp/playgroup.json" \
  --api-key "..."
```

PowerShell equivalent:

```powershell
mtg-mcp auth playgroup `
  --credentials-file "$env:USERPROFILE\.mtg-mcp\playgroup.json" `
  --api-key "..."
```

Supported environment settings. In rows with slashes, repeat the full prefix for
each abbreviated suffix.

| Setting | Use |
| --- | --- |
| `MTGMCP__OPERATION_MODE` | `read-only`, `plan`, or `apply`. Set explicitly; the app default is `apply`. |
| `MTGMCP__DATA_DIR` | Local decks, plans, workspaces, and source-fact cache. |
| `MTGMCP__INTELLIGENCE__ANALYSIS_DEPTH` | Recommendation source depth: `minimal`, `balanced`, or `best`. |
| `MTGMCP__INTELLIGENCE__CACHE__MODE` | Source-fact cache: `persisted`, `memory`, or `off`. |
| `MTGMCP__INTELLIGENCE__CACHE__MAX_BYTES` / `MAX_ENTRIES` | Persisted cache limits. |
| `MTGMCP__INTELLIGENCE__CACHE__TTLS__SCRYFALL_CARD_METADATA` / `SCRYFALL_SEARCH` / `COMMANDERSPELLBOOK` / `DECK_SEARCH` / `DECK_DETAILS` / `CORPUS_SIGNALS` | Per-source cache TTLs such as `24h` or `7d`. |
| `MTGMCP__INTELLIGENCE__SOURCES__SCRYFALL__ENABLED` / `SCRYFALL_TAGGER__ENABLED` / `COMMANDERSPELLBOOK__ENABLED` / `TOPDECK__ENABLED` / `SPICERACK__ENABLED` / `EDHREC__ENABLED` / `EDHTOP16__ENABLED` / `REDDIT__ENABLED` | Enable or disable recommendation sources. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__API_KEY` / `SPICERACK__API_KEY` / `REDDIT__API_KEY` | Optional source API keys. |
| `MTGMCP__INTELLIGENCE__SOURCES__EDHREC__ALLOW_UNOFFICIAL_API` / `EDHTOP16__ALLOW_UNOFFICIAL_API` / `REDDIT__ALLOW_UNOFFICIAL_API` | Allow bounded unofficial structured JSON endpoints for those sources. |
| `MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__BASE_ADDRESS` / `SPICERACK__BASE_ADDRESS` / `EDHREC__BASE_ADDRESS` / `EDHTOP16__BASE_ADDRESS` / `REDDIT__BASE_ADDRESS` | Source API URL overrides. |
| `MTGMCP__ARCHIDEKT__BASE_ADDRESS` / `CREDENTIALS_FILE` / `USERNAME` / `PASSWORD` | Archidekt API and credential settings. The username value may be an Archidekt username or account email. |
| `MTGMCP__ARCHIDEKT__RATE_LIMIT__MAX_REQUESTS` / `WINDOW_SECONDS` | Optional process-local Archidekt pacing. For example, `30` requests per `60` seconds leaves room for browser activity; `0` max requests disables proactive pacing. |
| `MTGMCP__MOXFIELD__BASE_ADDRESS` / `USER_AGENT` / `CURL_FALLBACK_ENABLED` / `CURL_PATH` | Moxfield import endpoint settings. Imports use an anonymous, unofficial endpoint; when Moxfield blocks .NET HTTP requests, the adapter can retry through `curl` if available. |
| `MTGMCP__PLAYGROUP__BASE_ADDRESS` / `API_KEY` / `CREDENTIALS_FILE` | Playgroup.gg API settings. Credential files may use JSON or `apiKey=value`, `accessToken=value`, or `token=value` lines. |
| `MTGMCP__SIMULATION__PROFILE_PATHS__0` / `MTGMCP__SIMULATION__ALLOW_EXTERNAL_PROFILE_OVERRIDES` | Optional external simulation profile JSON files or simple glob paths. Built-in profiles always remain available. |
| `MTGMCP__SCRYFALL__BASE_ADDRESS` / `USER_AGENT` / `MAX_RATE_LIMIT_RETRIES` | Scryfall API settings. |
| `MTGMCP__COMMANDERSPELLBOOK__BASE_ADDRESS` | Commander Spellbook API setting. |

### Recommendation sources

Recommendation source providers give deckbuilding tools source-backed evidence
beyond the local decklist. Use `source_list` or `mtg://sources/status` to
report the current source status:

- `available`: the provider can be queried.
- `missing-config`: the provider is implemented, but needs a configured key.
- `disabled`: the provider is implemented, but disabled by configuration.
- `failed`: the provider failed during a lookup; other sources still run.

TopDeck.gg and Spicerack are API-backed decklist recommendation sources. They are
enabled by default, but they do not make network calls until an API key is
configured.

```powershell
$env:MTGMCP__INTELLIGENCE__SOURCES__TOPDECK__API_KEY = "..."
$env:MTGMCP__INTELLIGENCE__SOURCES__SPICERACK__API_KEY = "..."
```

Equivalent `mtg-mcp.json`:

```json
{
  "MtgMcp": {
    "Intelligence": {
      "Sources": {
        "TopDeck": {
          "ApiKey": "..."
        },
        "Spicerack": {
          "ApiKey": "..."
        }
      }
    }
  }
}
```

Set `Enabled` to `false` for either source to exclude it:

```json
{
  "MtgMcp": {
    "Intelligence": {
      "Sources": {
        "TopDeck": {
          "Enabled": false
        }
      }
    }
  }
}
```

TopDeck.gg uses the documented tournaments v2 API for tournament standings and
decklists. Get a key from TopDeck.gg and keep visible attribution when using
its data in user-facing output. Spicerack uses the documented public decklist
database API for recent tournament results and decklist text.

EDHREC is enabled by default as an unofficial source for broad Commander
aggregate inclusion and synergy evidence. It uses structured JSON pages only,
never HTML scraping or browser automation. Set `AllowUnofficialApi` to `false`
to exclude it:

```powershell
$env:MTGMCP__INTELLIGENCE__SOURCES__EDHREC__ALLOW_UNOFFICIAL_API = "false"
```

Equivalent `mtg-mcp.json`:

```json
{
  "MtgMcp": {
    "Intelligence": {
      "Sources": {
        "Edhrec": {
          "AllowUnofficialApi": false
        }
      }
    }
  }
}
```

EDHREC evidence is cached, attribution-sensitive, and not backed by a stable
public API contract. Treat it as popularity and synergy context, not tournament
performance or source decklist evidence. Commander theme filters are used only
when a source exposes deterministic theme slugs; other sources return an
`unsupported-theme` note instead of silently falling back to broader rows.

Reddit discussion evidence is also enabled by default through bounded public
JSON lookups over Commander-focused subreddits. Configure a bearer token to use
Reddit's OAuth API path, or set `AllowUnofficialApi` to `false` for the Reddit
source to disable public JSON discussion search. EDHTop16 remains opt-in because
it uses an unofficial cEDH-focused endpoint rather than broad casual Commander
data.

These tools consume recommendation sources or source-backed catalog evidence:

- `commander_get_aggregate_cards`: returns commander card rows grouped by
  source. It does not merge unlike populations when `source` is omitted.
- `commander_get_tags`: returns source-backed commander tags and themes.
- `commander_get_win_condition_evidence`: bundles aggregate cards, tags,
  commander-containing combos, route classifications, and payoff candidates as
  structured evidence only.
- `deck_review_new_card_swaps`: returns new-card swap evidence plus
  deterministic cut evidence, not automatic edits.
- `deck_find_exemplar_decks`: returns high-signal source decks.
- `deck_analyze_commander_trends`: ranks card candidates from enabled sources.
- `deck_find_lesser_known_cards`: finds lower-known candidates with source evidence.
- `source_explain_card_signal`: explains one card's source signal in a deck context.
- `source_search_evidence`: inspects one source by key, such as `topdeck`,
  `spicerack`, or `edhrec`, without making deckbuilding choices.
- `source_search_reddit_discussions`: returns bounded raw discussion rows and
  card mentions; Reddit is never treated as prevalence evidence.

TopDeck and Spicerack evidence is tournament and event decklist evidence. It is
not broad casual Commander inclusion data. Use `bypassCache=true` on source-backed
recommendation tools to bypass fresh source-fact cache entries for one call.

Use these resources inside an MCP client to verify setup without exposing
secrets:

- `mtg://config/effective`
- `mtg://server/info`
- `mtg://providers/archidekt/auth-status`
- `mtg://providers/playgroup/auth-status`
- `mtg://sources/status`

## MCP Surface

Workflow-first tools:

- Start or open workspaces with `workspace_start`, `workspace_list`, and
  `workspace_open`; parse, export, and validate with `workspace_parse_decklist`,
  `workspace_export`, and `workspace_validate`.
- Search cards with `card_search`, `card_get`, `card_get_prints`, and
  `card_get_rulings`.
- Inspect decks with `deck_summarize`, `deck_analyze_structure`,
  `deck_analyze_mana`, `deck_analyze_consistency`, and
  `deck_analyze_performance`; use `deck_analyze_land_drop_odds` for the
  turn-by-turn land-drop question.
- Inspect combos and win routes with `deck_analyze_combos`,
  `combo_search_by_card`, `combo_get_details`, `card_classify_win_routes`, and
  `wincon_find_payoffs`.
- Research changes with `deck_query_cards`, `deck_review_new_card_swaps`,
  `commander_get_aggregate_cards`, `commander_get_win_condition_evidence`,
  source tools, and Playgroup tools.
- Preview edits with `deck_plan_create`, `deck_plan_preview`, and
  `deck_plan_compare_performance`; apply only with `deck_plan_apply`.
- Use provider tools such as `archidekt_copy_workspace`,
  `archidekt_checkpoint_create`, and `playgroup_rank_decks` when the workflow
  needs provider-specific behavior.

Public tool prefixes are `archidekt_`, `card_`, `commander_`, `combo_`,
`deck_`, `playgroup_`, `server_`, `source_`, `wincon_`, and `workspace_`.
Compatibility aliases from older 0.x releases are intentionally not exposed.

Useful resources:

- Workspace data: `mtg://workspace/{workspaceId}`,
  `mtg://workspace/{workspaceId}/summary`,
  `mtg://workspace/{workspaceId}/intent`.
- Usage guides: `mtg://scryfall/syntax-cheatsheet`,
  `mtg://formats/{format}/deck-rules`, `mtg://usage/workspace-selection`,
  `mtg://usage/operation-modes`, `mtg://usage/deck-intent`.
- Status: `mtg://config/effective`, `mtg://sources/status`,
  `mtg://server/info`, `mtg://providers/{provider}/auth-status`.

Built-in prompts cover brewing, tuning, Commander common-card and win-condition
evidence, cost reduction, power increases or reductions, Commander bracket
reduction, mana-base work, land-drop risk, consistency, local meta tuning,
new-card swaps, missing combo pieces, goldfishing, goal-focused packages, and
rules/rulings checks.

For Playgroup-aware tuning, `deck_score_cards_for_playgroup_meta` scores
explicit candidate names with `candidateSource=explicit-cards`, or cards in
excluded workspace categories with `candidateSource=excluded-workspace-cards`,
with visible factor scores for plan fit, deterministic performance delta,
local-meta coverage, self-harm, price/bracket constraints, and evidence
confidence. Playgroup decks are ranked from fetched game participations;
Archidekt decklists are imported read-only when Playgroup exposes an Archidekt
URL.

Simulation results include the resolved simulation profile, why that profile was
chosen, route evidence, and warnings when a claim comes from fallback
heuristics. See [`docs/simulation-profiles.md`](docs/simulation-profiles.md)
for the compact profile, deck-intent, and route syntax reference.

## Deck Intent

Deck intent is optional text stored in a workspace description, and in the
Archidekt deck description when writeback is enabled. Use `deck_intent_suggest`,
`deck_intent_get`, `deck_intent_set`, and `deck_intent_clear`.

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

Source-backed recommendations query structured APIs on demand and cache source
facts under the recommendation source cache directory. The cache stores source
facts, not final recommendations or prompt rationale. Pass `bypassCache=true`
to supported tools to bypass fresh cache entries for one call.

The source policy is API-only: official/documented APIs and explicitly allowed
unofficial structured JSON endpoints may be used, but mtg-mcp does not scrape
HTML, parse page markup, or use browser automation for source data.

## Development

### Fresh Checkout

Development is supported without installing the pinned SDK globally. From a
fresh checkout, run the bootstrap script for your platform. It installs Go Task
under `.tools` when needed, then delegates to `task setup`, which installs the
pinned .NET SDK under `.dotnet`, restores dotnet tools, and restores NuGet
packages:

```bash
./bootstrap.sh
```

```powershell
.\bootstrap.ps1
```

You can pass any Task workflow through bootstrap:

```bash
./bootstrap.sh test
```

After setup, use the same Task workflows as CI:

```bash
task test
task lint
task install:local
task install:local:cleanup
```

On Linux, `scripts/setup-linux.sh` remains available when you also want local
PowerShell and repo-local .NET/NuGet cache paths. Source `scripts/env-linux.sh`
after using it to carry those paths into future shells.

`task install:local` publishes the current host runtime by default, so Linux
uses `linux-x64` or `linux-arm64` depending on the machine. Override it only
when cross-publishing:

```bash
task publish:runtime RUNTIME=linux-x64
```

For a system-wide setup instead of the local bootstrap, install these
prerequisites yourself and then run `task setup`:

- .NET SDK `11.0.100-preview.4.26230.115` or a compatible .NET 11 preview SDK.
- Task CLI `3.51.1` or newer.
- PowerShell 7 `pwsh` on Linux/macOS, or Windows PowerShell on Windows.
- `curl`, used by the setup script and optional Moxfield HTTP fallback.

### Common Workflows

Use `Taskfile.yml` for common workflows:

```bash
task test
task lint
task install:local
task install:local:cleanup
```

`task install:local` packs the current checkout using the package version in
`server.json`. It reinstalls the global `.NET` tool from that freshly packed
local package, publishes a self-contained binary, and copies it to the configured
local MCP command path when one is found. If that executable is locked by a
running MCP process, it writes a versioned binary beside it and updates the Codex
MCP config for the next server start. Pass `LOCAL_VERSION=...` only when you need
an explicit one-off package version.

`task install:local:cleanup` removes old unlocked versioned local binaries while
keeping the currently configured MCP command path.
