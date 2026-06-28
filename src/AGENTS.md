# Source Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `src/`.

## Project Boundaries

- Keep `MtgMcp.Core` dependency-light and free of adapter or host references.
- Keep MCP server registration, tools, resources, prompts, operation modes, and server metadata in `MtgMcp.App`.
- Keep third-party HTTP request and response contracts in their adapter projects.
- Keep source/provider-specific cache, auth, pacing, and user-agent behavior inside the owning adapter unless Core already has a shared primitive.
- Update architecture or surface tests when project references, public MCP shape, operation modes, or allowed adapter boundaries intentionally change.

## Implementation

- Prefer existing Core models, option types, request pacing, retry, JSON, and text helpers before adding new ones.
- Guard mutating MCP tools with `OperationModeGuard`.
- Keep tool annotations, descriptions, and resource URIs accurate when public MCP behavior changes.
- Pass `CancellationToken` through async library paths and use `ConfigureAwait(false)` outside host-specific code.
- Keep provider errors sanitized. Never include Archidekt credentials, bearer tokens, cookies, or local secret paths in exceptions, logs, tool output, or tests.
- Use loops for multi-step behavior and keep LINQ simple.

## Public Contracts

- Add XML summary comments for new public C# declarations.
- Preserve deterministic sort keys, source metadata, assumptions, warnings, and confidence fields in evidence-oriented outputs.
- Make new persistence or config formats explicit in docs and tests.
