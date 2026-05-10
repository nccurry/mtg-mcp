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
- Create, import, export, and manage local deck workspaces.
- Open Archidekt decks and optionally write changes back to Archidekt.
- Add, remove, move, categorize, and update cards and decks.
- Analyze mana, cost, curve, consistency, draw odds, legality, and upgrade paths.
- Create recommendation plans for budget swaps, upgrades, mana bases, categories,
  and power tuning.
- Preview recommendation plans before applying them.

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
workspaces, change deck contents, create checkpoints, or write back to Archidekt.

## Optional Archidekt writeback

Archidekt credentials are only needed when you want to list private decks, open
account-bound deck data, create checkpoints, or write changes back.

Create a credentials file:

```powershell
mtg-mcp auth archidekt `
  --credentials-file "$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  --refresh-token "..." `
  --user-id "..."
```

Then configure your MCP client with write access:

```powershell
codex mcp add mtg-mcp `
  --env MTGMCP__OPERATION_MODE=apply `
  --env MTGMCP__ARCHIDEKT__CREDENTIALS_FILE="$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  -- mtg-mcp
```

A credentials file can also be created by hand:

```json
{
  "jwt": "optional-jwt",
  "refreshToken": "optional-refresh-token",
  "userId": "optional-archidekt-user-id",
  "email": "fallback-email",
  "username": "fallback-username",
  "password": "fallback-password"
}
```

JWT or refresh token auth is preferred. Email or username plus password login is
available as a fallback.

## Configuration

Most users only need these settings:

```powershell
$env:MTGMCP__OPERATION_MODE="plan"
$env:MTGMCP__DATA_DIR="$env:LOCALAPPDATA\mtg-mcp"
$env:MTGMCP__ARCHIDEKT__CREDENTIALS_FILE="$env:USERPROFILE\.mtg-mcp\archidekt.json"
```

`MTGMCP__OPERATION_MODE` accepts:

- `read-only`: allow lookup and analysis tools only.
- `plan`: allow lookup, analysis, metadata refresh, and recommendation plans.
- `apply`: allow deck edits, checkpoints, and Archidekt writeback.

Inside an MCP client, these resources help verify setup:

- `mtg://config/effective` shows non-secret effective configuration.
- `mtg://archidekt/auth-status` shows redacted Archidekt credential status.

## How to use it

Ask your MCP client for the deckbuilding task you want done:

```text
Search for blue one-mana cantrips legal in Commander.
```

Prompts that create, open, or change workspaces need `apply` mode:

```text
Import this decklist into a local Commander workspace and summarize the plan.
```

```text
Open this Archidekt deck locally, analyze the mana base, and suggest fixes under $10.
```

```text
Find budget replacements for cards over $20 and preview the plan before changing anything.
```

```text
Apply the previewed deck plan and create an Archidekt checkpoint first.
```

## How it works

`mtg-mcp` runs as a stdio MCP server. Your MCP client calls its tools to search
Scryfall, manage deck workspaces, analyze deck structure, and optionally sync
changes to Archidekt.

Local workspaces and recommendation plans are saved under `MTGMCP__DATA_DIR`.
Archidekt writeback is opt-in: a workspace must be opened with writeback enabled,
and the server must be running in `apply` mode before deck contents are changed.
