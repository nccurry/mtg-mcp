# Latest MCP and Toolchain Requirements

## Document Control

- Lifecycle status: Complete
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Purpose

Keep mtg-mcp on its newest supported tools and protocol. The server must use
one current MCP revision. It must not silently support older protocol clients.

## References

- [Repository toolchain files](../../../../../global.json),
  [Mise file](../../../../../mise.toml), and
  [Taskfile](../../../../../Taskfile.yml).
- [Central NuGet versions](../../../../../Directory.Packages.props).
- [Microsoft .NET release index](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json).
- [Mise .NET support](https://mise.jdx.dev/lang/dotnet.html).
- [C# 15 changes](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15).
- [MCP C# server options](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerOptions.html).
- [MCP C# client options](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Client.McpClientOptions.html).
- [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management).

## Outcomes

| Outcome | Success signal |
| --- | --- |
| A contributor installs the declared toolchain. | `mise exec -- task setup:verify` shows the SDK from `global.json`, pinned PowerShell, and local tools. |
| A current MCP client connects. | The end-to-end client uses `2026-07-28` and reads the capability resource. |
| An older MCP client does not connect by accident. | A client pinned to an `initialize` revision fails with the SDK's protocol error. |
| A maintainer can reproduce the reviewed tool and package graph. | Direct versions and committed lock files identify the selected result. |

## Scope

### In scope

- Keep direct SDK, Mise, local tool, and NuGet pins current at phase start.
- Pin the server and official test client to MCP `2026-07-28`.
- Remove implicit MCP protocol fallback from the supported server contract.
- Keep Task as the normal repository command entry point.
- Commit Mise and NuGet lock files for the reviewed tool and package graph.
- Pin GitHub Actions by commit SHA and keep Dependabot as their weekly updater.
- Manage the MCP Registry publisher through Mise instead of a `latest` download.
- Use C# 15 features in changed code only when they make the code shorter or
  easier to read.
- Add focused tests and documentation for the exact protocol policy.

### Out of scope

- A .NET final-release move before Microsoft ships it.
- An MCP HTTP server, MCP tasks, server-to-client sampling, or a new transport.
- Compatibility aliases, older protocol support, or a legacy client bridge.
- A whole-project syntax rewrite to use C# 15.
- Forced transitive package pins unless the owner accepts that policy.
- A new provider, cache, toolset, or persistence format.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| LMT-001 | Must | `global.json` must be the only .NET SDK version source. | Mise discovers the SDK through `global.json`; `mise.toml` has no competing .NET version. |
| LMT-002 | Must | Task must remain the menu for setup, lint, tests, and dependency reports. | `task --list` and `task setup:verify` work through Mise. |
| LMT-003 | Must | Direct package and tool pins must use exact versions. | `task deps:check` reports no direct update at phase completion, or the packet records an intentional exception. |
| LMT-004 | Must | Mise and NuGet must lock their reviewed resolved graphs. | `mise.lock` records lockable Mise downloads, each `packages.lock.json` is committed, and normal restore and CI use locked mode. |
| LMT-005 | Must | GitHub Actions must use exact commit-SHA pins with readable release comments. | Workflow source contains no floating action tag; Dependabot continues to watch the action ecosystem. |
| LMT-006 | Must | The MCP Registry publisher and CI Mise runner must use exact reviewed versions. | The release workflow invokes the configured publisher through Task or Mise, CI requests the exact Mise version, and neither path downloads `latest`. |
| LMT-007 | Must | The server must require MCP `2026-07-28`. | `McpServerOptions.ProtocolVersion` uses the host's exact current-protocol constant. |
| LMT-008 | Must | The end-to-end client must require MCP `2026-07-28`. | The test helper sets `McpClientOptions.ProtocolVersion` to its exact current-protocol constant. |
| LMT-009 | Must | Older `initialize` clients must not receive a fallback connection. | A focused end-to-end test proves a client pinned to an older revision receives a protocol failure. |
| LMT-010 | Must | Tool results must keep structured JSON and text, while the capability resource remains JSON text. | Tests and source review show no binary MCP content type was added. |
| LMT-011 | Must | Changed code must remain clear and use C# 15 only where it reduces code or improves closed-case handling. | Code review finds no mechanical syntax conversion or unnecessary wrapper. |
| LMT-012 | Must | Documentation must state the current-only protocol policy and the version owners. | The README, toolchain guide, and capability assertions match the code. |

## Interfaces and Compatibility

The public tool, resource, prompt, and mode inventory does not change. The
capability resource reports the negotiated protocol version. After this phase,
only clients that speak MCP `2026-07-28` can use the server.

This is a deliberate breaking change. No setting enables an older protocol.

## Quality Attributes

| Attribute | Requirement | Measure |
| --- | --- | --- |
| Repeatability | One file owns each direct tool version. | Fresh setup succeeds through Mise and Task. |
| Clarity | Version ownership is visible in source and docs. | A reviewer can map .NET, Mise, NuGet, and local tool pins without guesswork. |
| Safety | The client cannot silently downgrade the protocol. | The focused older-client test fails as expected. |
| Testability | The protocol behavior needs no network. | All protocol tests run in the offline suite. |

## Resolved Package Rule

Pin each direct package at its current exact version. Commit the NuGet lock
files that record the full compatible package graph. This makes normal restores
repeatable without promoting every transitive package into the public tool
dependency list.

If a transitive package has a security or compatibility problem, update its
closest direct owner first. Add a direct override only when that route cannot
fix the problem and package-smoke tests prove the override.

## Traceability

| Requirement | Design | Validation |
| --- | --- | --- |
| LMT-001 to LMT-006 | [Version ownership](SADD.md#version-ownership) | `task setup:verify`, `task deps:check`, locked restore, and source inspection |
| LMT-007 to LMT-009 | [Current-only MCP protocol](SADD.md#current-only-mcp-protocol) | App and end-to-end protocol tests |
| LMT-010 | [MCP output limit](SADD.md#mcp-output-limit) | Source and surface tests |
| LMT-011 | [C# 15 use](SADD.md#c-15-use) | Code review and `task lint` |
| LMT-012 | [Documentation](SADD.md#documentation) | Markdown inspection and MCP smoke tests |

## Definition of Done

- [x] Every Must requirement has passing evidence.
- [x] Mise and NuGet lock files are committed and enforced.
- [x] GitHub Actions and the registry publisher have exact pins.
- [x] `task ci` passes.
- [x] Package and installed-tool MCP smoke checks pass.
- [x] The packet records final version and test evidence.
