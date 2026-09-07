# Latest MCP and Toolchain PLC Packet

> [!IMPORTANT]
> This packet is authorized for implementation. It supports the current MCP
> protocol only and does not add compatibility behavior.

## Lifecycle

- Status: Complete
- Folder: `docs/llms/plcs/completed/latest-mcp-and-toolchain/`
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Complete
- Implementation authorized: Yes

## Summary

The repository already uses the current direct .NET, NuGet, local tool, Mise,
and Task versions that were available on 2026-09-07. This packet makes the
MCP protocol policy match that current-only direction.

The server and its official-client end-to-end tests will require MCP
`2026-07-28`. Older `initialize` clients will not receive a fallback path.
The change is intentional. It keeps the public protocol contract small and
current.

The packet also records one source of truth for each version type. `global.json`
selects the .NET SDK. Mise installs that SDK and pins Task and PowerShell.
Task remains the normal command entry point after setup.

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance rules.
- [SADD.md](SADD.md): version ownership and MCP protocol design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): small delivery phases.
- [FIXTURES.md](FIXTURES.md): protocol and version-policy test cases.

## Decision Snapshot

| Decision | Status | Reason | Detail |
| --- | --- | --- | --- |
| Use current direct versions and lock the resolved graph. | Accepted | The owner wants current, repeatable versions. | [SADD](SADD.md#version-ownership) |
| Keep `global.json` as the .NET SDK source. | Accepted | Mise reads it without a duplicate SDK pin. | [SADD](SADD.md#version-ownership) |
| Use Task after Mise setup. | Accepted | It gives people, agents, and CI one command menu. | [SADD](SADD.md#version-ownership) |
| Lock Mise downloads and NuGet restores. | Accepted | A fresh machine must resolve the reviewed tool and package graph. | [SADD](SADD.md#version-ownership) |
| Pin GitHub Actions by commit SHA. | Accepted | Dependabot already keeps the reviewed action pins current. | [SADD](SADD.md#version-ownership) |
| Manage `mcp-publisher` through Mise. | Accepted | The release workflow must not download an unpinned `latest` binary. | [SADD](SADD.md#version-ownership) |
| Pin MCP to `2026-07-28`. | Accepted | The owner chose no backwards compatibility. | [SADD](SADD.md#current-only-mcp-protocol) |
| Use C# 15 only when it makes changed code simpler. | Accepted | New syntax must improve clarity, not add novelty. | [SADD](SADD.md#c-15-use) |

## Project and Surface Impact

The phase changes these areas:

- `global.json`, `mise.toml`, `mise.lock`, `Directory.Packages.props`, local
  tool files, and committed NuGet lock files when a reviewed update changes
  them.
- GitHub Actions workflow action references and the release publisher command.
- `FoundationHost` to pin the server protocol.
- `McpProcessSession` and MCP end-to-end tests to require the same protocol.
- The capability resource and protocol documentation.
- Version, dependency, protocol, process, package, and surface tests.

It does not add an MCP tool, resource, prompt, operation mode, provider, cache,
or data migration.

## Update Policy

Direct package, tool, and SDK versions use exact current pins. Committed lock
files record the complete reviewed result of each restore or installation.
They do not promote every transitive NuGet package into the published tool.

GitHub Actions use exact commit SHAs with a readable release comment.
Dependabot already proposes weekly action and NuGet updates. The MCP Registry
publisher becomes an exact Mise-managed GitHub release asset. A fresh update
must pass the normal Task validation before its pin and lock files are kept.

## Planning Readiness

- [x] The scope and non-scope are clear.
- [x] The requirements have measurable acceptance criteria.
- [x] The protocol behavior has official SDK evidence.
- [x] The version owners are clear.
- [x] The public MCP surface impact is clear.
- [x] The test and package checks are named.
- [x] The version and lock-file policy is clear.
- [x] The GitHub Actions and registry-publisher policy is clear.
- [x] An independent design review has examined this packet and its findings are fixed.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | `mise exec -- task setup:verify` | Passed | The pinned .NET SDK, PowerShell, and local tools are available. |
| 2026-09-07 | `mise install --locked` | Passed | The committed Mise lock file resolves the declared toolchain. |
| 2026-09-07 | `mise exec -- task deps:check` | Passed | No direct NuGet or Mise update, vulnerability, or deprecated package was reported. Transitive updates remain for their direct owners to review. |
| 2026-09-07 | `mise exec -- task registry:validate` | Passed | The Mise-managed MCP Registry publisher validates `server.json`. |
| 2026-09-07 | `mise exec -- task test:e2e` | Passed | 49 process-hosted MCP tests passed, including older-client rejection. |
| 2026-09-07 | `mise exec -- task ci` | Passed | Formatting, strict build, 565 offline tests, 90-percent line coverage gates, and MCP smoke checks passed. |
| 2026-09-07 | `mise exec -- task pack` and `task release:tool-smoke` | Passed | The packed `0.9.0` tool installed into an isolated folder and passed 44 MCP smoke tests. |
| 2026-09-07 | Markdown links, source scan, and phase audit | Passed | Local links resolve; the stable output surface has no binary content types; the combined audit found no remaining implementation issue. |
| 2026-09-07 | Independent design review | Passed after fixes | Clarified Mise bootstrap ownership, both E2E client paths, and the resource-versus-tool JSON output rule. |
