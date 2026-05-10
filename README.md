# mtg-mcp
<!-- mcp-name: io.github.nccurry/mtg-mcp -->

[![CI](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/nccurry/mtg-mcp/branch/main/graph/badge.svg)](https://codecov.io/gh/nccurry/mtg-mcp)
[![NuGet](https://img.shields.io/nuget/v/Nccurry.MtgMcp?label=NuGet)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![NuGet downloads](https://img.shields.io/nuget/dt/Nccurry.MtgMcp?label=downloads)](https://www.nuget.org/packages/Nccurry.MtgMcp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-io.github.nccurry%2Fmtg--mcp-0f766e)](https://registry.modelcontextprotocol.io/?q=io.github.nccurry%2Fmtg-mcp)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue.svg)](LICENSE)

`mtg-mcp` is an unofficial community project. It is not produced by, endorsed by,
supported by, or affiliated with Wizards of the Coast, Scryfall, or Archidekt.
Magic: The Gathering and related materials are property of Wizards of the Coast LLC.

`mtg-mcp` is a self-contained C#/.NET MCP server for building Magic: The Gathering
decks with Scryfall card data and Archidekt deck writeback.

## Features

- Search, fetch, print, ruling, and suggestion tools powered by Scryfall.
- Local deck workspaces for offline brewing, listing, and import/export.
- Archidekt-bound workspaces for immediate deck writeback.
- Mutation results include explicit `local-only` or `archidekt-writeback` persistence markers.
- `start_deck_workspace` and `mtg://usage/workspace-selection` guide LLMs to ask
  before ambiguous local versus Archidekt choices.
- MCP tool annotations mark read-only, destructive, idempotent, and open-world behavior for compatible clients.
- Server-side operation modes can block mutations for Ask/Plan style sessions.
- Category-based organization for mainboard, sideboard, maybeboard, and custom Archidekt categories.
- Deck checkpoint tools backed by Archidekt snapshots.
- Deck intelligence tools for Scryfall normalization, plan summaries, role/tag classification, draw odds, cost analysis, mana-base analysis, consistency analysis, budget replacements, upgrades, bracket reduction, power tuning, and category cleanup plans.
- Persisted `DeckEditPlan` workflows keep recommendations separate from mutations; `apply_deck_plan` is the only recommendation tool that changes deck contents.
- Plan preview tools show before/after cost, validation, role, mana, consistency, and Commander bracket estimates before any plan is applied.
- MCP tools, resources, and prompts exposed over stdio.

Moxfield support is intentionally out of scope for v1 because there is no supported public write API.

Commander bracket tools use live Scryfall `is:game-changer` search results plus
heuristics for fast mana, tutors, stax, combo, extra turns, and mass land denial.
Bracket output is an advisory estimate for pregame discussion, not an official
determination. The current public bracket context is Wizards' beta update:
https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026.

## Quick Start

Install the packaged tool from NuGet:

```powershell
dotnet tool install --global Nccurry.MtgMcp
mtg-mcp
```

Windows users can also install the current GitHub release with WinGet while the
package is pending in the official community source:

```powershell
$version = "0.1.0"
$zip = "$env:TEMP\mtg-mcp-$version-winget.zip"
$manifestRoot = "$env:TEMP\mtg-mcp-$version-winget"

Invoke-WebRequest `
  "https://github.com/nccurry/mtg-mcp/releases/download/$version/mtg-mcp-$version-winget.zip" `
  -OutFile $zip

Remove-Item $manifestRoot -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive $zip -DestinationPath $manifestRoot

winget settings --enable LocalManifestFiles
winget install --manifest $manifestRoot --accept-package-agreements
```

After the official WinGet submission is approved, install with:

```powershell
winget install Nccurry.MtgMcp
```

Configure your MCP client to run the `mtg-mcp` stdio command.

For Codex:

```powershell
codex mcp add mtg-mcp `
  --env MTGMCP__ARCHIDEKT__CREDENTIALS_FILE="$env:USERPROFILE\.mtg-mcp\archidekt.json" `
  --env MTGMCP__OPERATION_MODE=apply `
  -- mtg-mcp
```

For local development or testing an unpublished checkout, run from source:

```powershell
dotnet run --project src/MtgMcp.App/MtgMcp.App.csproj
```

Or pack and install the current checkout as a local .NET tool:

```powershell
task pack VERSION=0.0.0-local
dotnet tool install --global Nccurry.MtgMcp `
  --version 0.0.0-local `
  --add-source artifacts/packages
```

For a single-file binary fallback:

```powershell
task publish
```

When using a fallback path, configure the MCP client to run the release archive
binary or the `dotnet run` command above instead of `mtg-mcp`.

## Configuration

Configuration can come from CLI arguments, environment variables, or JSON files.
Environment variables use `__` as the section separator.

Common settings:

```powershell
$env:MTGMCP__DATA_DIR="$env:LOCALAPPDATA\mtg-mcp"
$env:MTGMCP__OPERATION_MODE="apply"
$env:MTGMCP__ARCHIDEKT__JWT="..."
$env:MTGMCP__ARCHIDEKT__REFRESH_TOKEN="..."
$env:MTGMCP__ARCHIDEKT__USER_ID="..."
$env:MTGMCP__ARCHIDEKT__EMAIL="..."
$env:MTGMCP__ARCHIDEKT__CREDENTIALS_FILE="$env:USERPROFILE\.mtg-mcp\archidekt.json"
```

`MTGMCP__OPERATION_MODE` accepts:

- `apply` or `act`: read and write tools are allowed.
- `plan`: read-only tools and non-mutating planning tools are allowed; deck-content changes return an error asking for apply mode.
- `read-only` or `ask`: read-only tools are allowed; deck-content changes and planning-state writes return an error asking for plan or apply mode.

Credential files can contain:

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

For passwords with quotes, backslashes, or other punctuation, a simpler `key=value`
file is also supported and avoids JSON escaping:

```text
userId=optional-archidekt-user-id
email=archidekt-user@example.com
password=pa\ss"word=with#punctuation!
```

JWT/refresh token auth is preferred. Email or username plus password login is only
a fallback, and secrets are redacted from MCP resources and logs.

## Development

```powershell
task restore
task build
task test
task smoke:mcp
```

Live Archidekt write tests are opt-in and require explicit environment variables plus a throwaway deck ID.

## Releases

Releases are created from plain SemVer tags such as `0.1.0`; tags prefixed with
`v` are rejected by the release workflow.

```powershell
task release:verify VERSION=0.1.0
git tag 0.1.0
git push origin 0.1.0
```

The release workflow publishes the `Nccurry.MtgMcp` NuGet tool package, attaches
self-contained `win-x64`, `linux-x64`, and `osx-arm64` archives to the GitHub
Release, and publishes `server.json` metadata to the MCP Registry. Publishing to
NuGet requires a repository secret named `NUGET_API_KEY`.
