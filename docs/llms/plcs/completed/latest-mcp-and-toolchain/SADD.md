# Latest MCP and Toolchain Design

## Document Control

- Lifecycle status: Complete
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Chosen Design

Use the current direct versions that the repository owns. Keep each direct pin
in one existing source file. Pin both ends of the local MCP test connection to
the current MCP protocol version.

The server does not support a second protocol revision. The test client does
not allow the SDK to fall back to an older revision. This keeps the behavior
honest: a client either speaks the current protocol or gets a clear failure.

## Version Ownership

| Item | Owner | Policy |
| --- | --- | --- |
| .NET SDK | `global.json` | Use one exact prerelease SDK pin. Mise reads this file. |
| Mise runner | `mise.toml` and CI workflow | `min_version` is a local bootstrap floor. CI requests one exact reviewed Mise version from a SHA-pinned action. |
| Task, PowerShell, and registry publisher | `mise.toml` | Use exact version pins. |
| Mise downloads | `mise.lock` | Record resolved release URLs and checksums for tool providers that Mise can lock. |
| Local .NET tools | `dotnet-tools.json` | Use exact version pins. |
| Direct NuGet packages | `Directory.Packages.props` | Use exact central version pins. |
| Resolved NuGet graph | Project `packages.lock.json` files | Lock the tested direct and transitive dependency graph. |
| GitHub Actions | `.github/workflows/*.yml` | Pin each action by SHA and retain its release comment. |
| Build, test, and release commands | `Taskfile.yml` | Use `mise exec` for managed executables. |
| Toolchain updates | `task deps:check` and `task deps:update` | Inspect first. Refresh pins and lock files as one reviewed change. |

Mise must not receive a second `dotnet` version in `mise.toml`. Its documented
`global.json` support avoids competing pins. The `dotnet.isolated` setting keeps
the active SDK separate from a machine-wide SDK selection.

Mise installs itself before it can read project configuration. So,
`min_version` is a compatibility floor for a contributor's installed runner,
not a second tool pin. Each CI workflow sets the Mise action's `version` input
to the exact reviewed version. The lock file applies only to downloads that
Mise can lock; the exact `global.json` pin remains the .NET SDK owner.

Direct dependencies are the package contract that mtg-mcp owns. NuGet lock
files record the complete tested graph without making every transitive package a
public dependency. A direct override remains an exception for a confirmed
security or compatibility issue.

Mise manages the exact MCP Registry publisher release and records its download
in `mise.lock`. The release workflow invokes that managed executable. It does
not download a moving `latest` release during publication.

## Current-only MCP Protocol

`ModelContextProtocol` 2.2.0 supports MCP `2026-07-28`. The SDK describes this
revision as the latest supported protocol. It uses `server/discover` and
per-request metadata instead of the old `initialize` handshake.

The public SDK options take protocol revision strings. Its similarly named
helper type is internal in SDK 2.2.0, so each process boundary owns one small,
named wire-version constant. The E2E assembly intentionally does not reference
the App assembly just to reuse this string.

The host change is small:

```csharp
options.ProtocolVersion = SupportedProtocolVersion;
```

The end-to-end client uses the matching explicit option:

```csharp
new McpClientOptions
{
    ProtocolVersion = CurrentProtocolVersion,
}
```

An explicit client protocol version means the client refuses a downgrade. An
explicit server version means the server rejects older `initialize` clients.
The test suite must prove both conditions.

No configuration key selects a legacy protocol. No compatibility adapter,
alias, or dual-session behavior is added.

## MCP Output Limit

Tool results return structured JSON and concise text. The capability resource
remains text content declared as `application/json`. Neither surface emits
binary content blocks. Keep that limit in this phase.

This guard avoids an open SDK binary-content serialization problem from changing
the server behavior by accident. It does not prevent a later dedicated media
design from adding binary output with an updated SDK and focused tests.

## C# 15 Use

The project uses the .NET 11 preview SDK and `LangVersion` `preview`. Changed
code can use C# 15 when it reduces a real branch or data-construction cost.

Examples that fit this phase:

- Collection expressions for a small immutable test input.
- A union case when a closed protocol result needs a payload.
- Exhaustive `switch` handling when it replaces a loose string branch.

Do not replace working records, enums, or result types only to use a new
language feature. Do not create a helper merely to hide one protocol string.

## Building Blocks

| Area | Change | Owner |
| --- | --- | --- |
| `FoundationHost` | Set the server protocol version. | App hosting |
| `McpProcessSession` | Use one private client-creation method for normal and live sessions; default to the current protocol and allow an older one only in the rejection test. | E2E tests |
| Protocol tests | Prove current connection and older-client rejection. | E2E tests |
| Capability test | Assert the resource reports `2026-07-28`. | E2E tests |
| Version files | Change only when a direct update exists. | Repository configuration |
| Documentation | State version ownership and current-only behavior. | Docs |

Core, provider adapters, deck storage, tool registration, and operation modes
do not change.

## Error Handling

An older client connection fails at the MCP protocol boundary. The server does
not start a partial legacy session. The default E2E helper uses the current
protocol in both normal and live session paths. One narrow helper input
supplies the older protocol only for the rejection test, which checks the
failure kind rather than copied SDK text.

Version checks remain read-only. A failed dependency report does not rewrite a
pin. A failed update stops before unrelated code changes.

## Test Design

| Test | Purpose |
| --- | --- |
| Current-client process test | Connect with MCP `2026-07-28`, list tools, and read capabilities. |
| Older-client process test | Request an `initialize` revision and assert a protocol failure. |
| Capability-resource test | Assert the exact protocol string and unchanged tool counts. |
| Output-shape inspection | Make sure tool results stay structured JSON and text, the resource stays JSON text, and no binary content enters the stable surface. |
| Toolchain report | Run the standard Mise and Task dependency checks plus locked restore. |
| GitHub Actions | Verify every action is a SHA pin with a release comment. |
| Registry publisher | Verify the installed Mise tool version and release command. |
| Package smoke test | Install the packed tool and make one current-protocol connection. |

## Documentation

Update the repository documents that describe setup or protocol support. Use
links to the version files instead of copying volatile version numbers.

## Alternatives

| Option | Decision | Reason |
| --- | --- | --- |
| Leave the protocol unset. | Rejected | The SDK then supports both current and legacy clients. |
| Use a configurable protocol version. | Rejected | It reintroduces backwards compatibility and multiplies test paths. |
| Promote every transitive NuGet package to a direct pin. | Rejected | Lock files make the resolved graph repeatable without expanding the public dependency contract. |
| Download the registry publisher at `latest` in CI. | Rejected | A release must use the reviewed Mise-managed version. |
| Rewrite the codebase for C# 15. | Rejected | Syntax churn does not improve the product. |
| Add MCP tasks or HTTP transport. | Rejected | This phase needs only a protocol policy change. |

## Risks and Deferred Work

| Item | Type | Response |
| --- | --- | --- |
| A client only speaks an older MCP revision. | Intentional break | It receives a clear protocol failure. |
| Microsoft ships a newer .NET 11 preview. | Maintenance | Start a new focused update after `task deps:check`. |
| A publisher update becomes necessary between releases. | Maintenance | Refresh its Mise pin and lock data, then run the release checks. |
| The SDK's binary-content issue changes. | Deferred | Revisit only when a media feature needs binary MCP output. |
