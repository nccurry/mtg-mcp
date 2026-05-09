# mtg-mcp

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
- MCP tools, resources, and prompts exposed over stdio.

Moxfield support is intentionally out of scope for v1 because there is no supported public write API.

## Quick Start

```powershell
dotnet run --project src/MtgMcp.App/MtgMcp.App.csproj
```

For a single-file binary:

```powershell
task publish
```

Configure an MCP client to run the published `MtgMcp.App` binary or the `dotnet run` command above.

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
- `plan`: read-only tools are allowed; mutating tools return an error asking for apply mode.
- `read-only` or `ask`: read-only tools are allowed; mutating tools return an error asking for apply mode.

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
