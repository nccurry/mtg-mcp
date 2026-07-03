# Source Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `src/`.

## Project Boundaries

- This branch is the authorized rewrite. Follow
  [`docs/rewrite-guide.md`](../docs/rewrite-guide.md) and the active child PLC;
  add only the projects and contracts assigned to the active phase.
- Keep `MtgMcp.Core` dependency-light and free of adapter or host references.
- Keep MCP server registration, tools, resources, prompts, operation modes, and server metadata in `MtgMcp.App`.
- Keep future third-party HTTP request and response contracts in their owning
  adapter projects.
- Keep future source/provider-specific cache, auth, pacing, and user-agent
  behavior inside the owning adapter unless Core has an approved shared
  primitive.
- Use `docs/adapters.md` as the shared provider-operation map instead of
  repeating adapter rules in project-local instruction files.
- Update architecture or surface tests when project references, public MCP shape, operation modes, or allowed adapter boundaries intentionally change.

## Implementation

- Reuse only items allowed by the audit and active child. Removed legacy code is
  reference evidence in Git history rather than the default foundation.
- Guard mutating MCP tools with the established `OperationModeGuard`.
- Keep tool annotations, descriptions, and resource URIs accurate when public MCP behavior changes.
- Pass `CancellationToken` through async library paths and use `ConfigureAwait(false)` outside host-specific code.
- Keep provider errors sanitized. Never include Archidekt credentials, bearer tokens, cookies, or local secret paths in exceptions, logs, tool output, or tests.
- Use loops for multi-step behavior and keep LINQ simple.
- Keep methods shallow and use guard clauses when they make failure paths
  explicit.

## Documentation And Contracts

- Add useful XML summaries for every named declaration, including private
  members. Use inline comments only for non-obvious rules and invariants.
- Preserve deterministic sort keys, source metadata, assumptions, warnings, and confidence fields in evidence-oriented outputs.
- Make new persistence or config formats explicit in docs and tests.
