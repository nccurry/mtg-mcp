# Source Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `src/`.

## Project Boundaries

- For ordinary maintenance of the current server, existing projects and
  contracts remain source truth. For an authorized rewrite child, follow
  [`docs/rewrite-guide.md`](../docs/rewrite-guide.md) and the approved child PLC
  instead of extending legacy workspace, plan, recommendation, intent, or
  simulation abstractions.
- Keep `MtgMcp.Core` dependency-light and free of adapter or host references.
- Keep MCP server registration, tools, resources, prompts, operation modes, and server metadata in `MtgMcp.App`.
- Keep third-party HTTP request and response contracts in their adapter projects.
- Keep source/provider-specific cache, auth, pacing, and user-agent behavior inside the owning adapter unless Core already has a shared primitive.
- Use `docs/adapters.md` as the shared provider-operation map instead of
  repeating adapter rules in project-local instruction files.
- Update architecture or surface tests when project references, public MCP shape, operation modes, or allowed adapter boundaries intentionally change.

## Implementation

- Prefer existing Core models and helpers for current-server maintenance. In the
  rewrite, reuse only items allowed by the audit/active child; existing code is
  reference evidence rather than the default foundation.
- Guard mutating MCP tools with `OperationModeGuard`.
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
