# Rewrite Skeleton And Repository Foundation Software Architecture And Design Document

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

The authorized implementation starts from updated `main` in a sibling worktree,
removes audited product projects from that branch, and rebuilds a minimal host.
The legacy source remains reachable through history and released artifacts; it
is not copied into a `legacy/` directory.

### Initial projects

| Project | Responsibility | Allowed dependencies |
| --- | --- | --- |
| `MtgMcp.Core` | Provider-neutral identifiers, evidence/result unions, configuration-independent contracts. | BCL only |
| `MtgMcp.App` | CLI/stdio host, configuration, MCP registration, mode guard, diagnostics, server metadata. | Core, MCP/hosting packages |

Later child PLCs may add `MtgMcp.Decks`, `MtgMcp.Scryfall`,
`MtgMcp.Archidekt`, `MtgMcp.Playgroup`, `MtgMcp.Statistics`, and
`MtgMcp.Tagger`. They may not reverse the dependency direction or expose
provider transport types through Core.

### Common contracts

`EvidenceDescriptor` is a closed union with cases for source fact, source
evidence, exact derivation, parser classification, heuristic estimate, and
sampled estimate. Case payloads carry only applicable metadata such as source,
retrieval time, snapshot, assumptions, model version, or seed.

`OperationResult<T>` is a closed union with success, not-found, not-cached,
unsupported, unavailable, conflict, and invalid-input cases. Errors use stable
reason codes and sanitized messages. Empty collections remain successful empty
data, never a substitute for unknown or unavailable state.

### Configuration and data

Configuration uses standard .NET JSON, `MTGMCP__` environment keys, and command
line sources. The default data root is a `mtg-mcp/v0.9` folder under platform
application data. The capability resource reports logical database state and
configuration presence but never credentials or absolute secret paths.
`v0.9` identifies the compatible data-schema family across `0.9.x`; package and
server versions independently use full SemVer such as `0.9.0-preview.N`.

### Operation modes

- `read-only`: permits pure calculations, cache/database reads, and explicit
  provider HTTP reads/previews; forbids every local database/file mutation and
  every remote mutation.
- `local`: adds local deck/cache/snapshot writes but still forbids remote
  provider mutation.
- `remote`: adds explicitly requested remote mutations guarded by the owning
  provider child.

Mode describes mutation authority, not network availability. Deterministic
offline execution is a normal-test policy, not a fourth runtime mode.

### MCP surface

- Standard MCP initialization returns server name and version.
- `mtg://server/capabilities` returns effective mode, protocol/package versions,
  tool/resource/prompt counts, module status, and data-schema versions without
  secret values or paths. Its resource count includes the capability resource
  itself, so the foundation reports zero tools, one resource, and zero prompts.
- No tools, prompts, or placeholders for future modules are registered.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Orphan branch | Rejected; loses useful history and complicates rollback. |
| Keep all projects but empty them | Rejected; creates misleading placeholders and dependency drag. |
| Retain old Core and remove only tools | Rejected; decision services and storage dominate current Core. |
| Generic service/plugin framework up front | Rejected; children add only proven boundaries. |
| Preserve `plan/apply` modes | Rejected; they do not distinguish local persistence from remote mutation. |

## Failure Modes

- Unknown operation mode fails startup with a sanitized configuration error.
- Unwritable data root is reported unavailable; startup may continue for
  read-only server metadata but no local capability is advertised.
- Missing future databases remain unavailable rather than being created by the
  foundation.
- Legacy data is detected only to produce a migration boundary note; it is not
  parsed or changed.

## Test Architecture

Core unit tests cover unions, serialization, stable ordering, and exhaustive
switches. App tests cover mode normalization, redaction, configuration, surface
inventory, and capability output. Architecture tests enforce references and
package allowlists. Mocked process E2E starts stdio, inspects initialization,
and reads the capability resource. The existing per-assembly coverage gate
applies to both production assemblies. Foundation work also rewrites legacy
per-project coverage conveniences, integration-test lists, and surface-report
filters so every referenced project/test still exists.
