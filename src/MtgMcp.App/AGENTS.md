# MCP Application Instructions

Root and `src/AGENTS.md` remain authoritative. This file adds defaults for
`MtgMcp.App`.

## MCP Surface

- Keep tool registration deterministically sorted and keep titles,
  descriptions, annotations, input schemas, and typed output schemas accurate.
- Prefer bounded structured results. Use shared detail levels, pagination, and
  resources instead of returning unbounded text or collections.
- Preserve evidence source, assumptions, warnings, confidence, freshness, and
  replay metadata when presenting Core results.
- Treat annotations as accurate risk hints, not authorization. Enforce write
  capability with `OperationModeGuard`. Rewrite modes are `read-only`, `local`,
  and `remote`, with local and remote mutation kept distinct.

## Host Boundary

- Keep transport, DI, configuration, logging, CLI, MCP error mapping, and
  provider composition in App. Keep provider-neutral shared contracts in Core,
  local deck and exact-statistics behavior in their owning rewrite projects,
  and provider contracts in adapters.
- Sanitize errors before they reach MCP output or logs. Never expose provider
  secrets or local credential paths.
- Pass cancellation through tool wrappers and avoid anonymous return types when
  a stable structured schema is useful.

## Validation

- Update surface tests for every tool, resource, prompt, annotation, schema,
  operation-mode, or toolset change.
- Stable rewrite surface tests must also prove capability-prefixed names, one
  capability resource, zero prompts, and absence of legacy decision surfaces.
- Use App unit tests for presenters and registration, and mocked process E2E
  tests for transport-visible behavior.
