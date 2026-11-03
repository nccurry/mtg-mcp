<!-- mcp-name: io.github.nccurry/mtg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/nccurry/mtg-mcp/main/assets/logo.svg" alt="mtg-mcp logo" width="180" />
</p>

<h1 align="center">mtg-mcp</h1>

<p align="center">
  <strong>Magic: The Gathering cards, decks, and exact math for your AI assistant.</strong>
</p>

<p align="center">
  <a href="#install">Install</a>
  ·
  <a href="#connect">Connect</a>
  ·
  <a href="#optional-accounts">Accounts</a>
  ·
  <a href="https://github.com/nccurry/mtg-mcp/blob/main/docs/reference.md">Reference</a>
</p>

<p align="center">
  <a href="https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/nccurry/mtg-mcp/actions/workflows/ci.yml/badge.svg" /></a>
  <a href="https://www.nuget.org/packages/Nccurry.MtgMcp"><img alt="NuGet" src="https://img.shields.io/nuget/v/Nccurry.MtgMcp?logo=nuget&amp;color=004880" /></a>
  <a href="https://registry.modelcontextprotocol.io/v0/servers?search=io.github.nccurry/mtg-mcp"><img alt="MCP Registry" src="https://img.shields.io/badge/MCP_Registry-io.github.nccurry%2Fmtg--mcp-6E56CF" /></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/11.0"><img alt=".NET 11 preview" src="https://img.shields.io/badge/.NET-11.0_preview-512BD4?logo=dotnet" /></a>
  <a href="https://github.com/nccurry/mtg-mcp/blob/main/LICENSE"><img alt="License: AGPL-3.0-or-later" src="https://img.shields.io/badge/License-AGPL--3.0--or--later-blue.svg" /></a>
</p>

---

mtg-mcp is an MCP server that gives an AI assistant grounded Magic: The Gathering evidence: Scryfall card search,
local decks, Archidekt sync, Commander Spellbook combos, and exact statistics. The assistant makes the deckbuilding
calls.

## Install

Install the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (preview), then:

```sh
dotnet tool install -g Nccurry.MtgMcp
```

It runs on Windows x64, Linux x64, and macOS on Apple silicon.

## Connect

Claude Code:

```sh
claude mcp add --scope user mtg-mcp -- mtg-mcp
```

Other MCP clients:

```json
{
  "mcpServers": {
    "mtg-mcp": { "command": "mtg-mcp" }
  }
}
```

If a client can't find `mtg-mcp`, use its full path: `~/.dotnet/tools/mtg-mcp` (on Windows,
`%USERPROFILE%\.dotnet\tools\mtg-mcp.cmd`).

To update, run `dotnet tool update -g Nccurry.MtgMcp`, then restart or reconnect mtg-mcp in your client.

## Optional accounts

Archidekt and Playgroup tools turn on when their credentials are present.

| Service | File | Contents |
| --- | --- | --- |
| Archidekt | `~/.mtg-mcp/archidekt.json` | `{ "username": "...", "password": "..." }` |
| Playgroup | `~/.mtg-mcp/playgroup.json` | `{ "apiKey": "..." }` |

Every tool, setting, and workflow is in the
[reference](https://github.com/nccurry/mtg-mcp/blob/main/docs/reference.md). To build from source, see
[CONTRIBUTING](https://github.com/nccurry/mtg-mcp/blob/main/CONTRIBUTING.md).

## License

[AGPL-3.0-or-later](https://github.com/nccurry/mtg-mcp/blob/main/LICENSE)
