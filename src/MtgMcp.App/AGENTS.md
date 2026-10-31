# MtgMcp.App — Agent Instructions

The root `AGENTS.md` and `src/AGENTS.md` still apply. This file adds rules for
`src/MtgMcp.App/`.

## What this folder owns

The MCP host: transport, DI, configuration, logging, CLI, MCP error mapping,
provider composition, tool registration, and result presenters.

## MCP surface

- Keep tool registration sorted deterministically and keep titles,
  descriptions, annotations, input schemas, and typed output schemas accurate.
- Prefer bounded structured results. Use shared detail levels, pagination, and
  resources instead of unbounded text or collections.
- Preserve evidence source, assumptions, warnings, confidence, freshness, and
  replay metadata when presenting Core results.
- Annotations are risk hints, not authorization. Enforce write capability with
  `OperationModeGuard`, and keep local and remote writes distinct.
- Each stable tool belongs to exactly one of `decks`, `scryfall`, `stats`,
  `archidekt`, `playgroup`, or `spellbook`. Build one deterministic
  registration set at startup by intersecting enabled toolsets with
  operation-mode visibility.
- Every toolset is on by default. `archidekt` and `playgroup` are off when
  their credentials are absent, even when named; check presence only, never
  call the provider or read secrets for this. Do not advertise dynamic
  tool-list changes while registration is fixed per session.
- Community-tag evidence goes through `scryfall`. Do not add a Tagger
  descriptor, placeholder, or alias.

## Host boundary

- Keep provider-neutral contracts in Core, local deck and exact-statistics
  behavior in their own projects, and provider contracts in adapters.
- Sanitize errors before they reach MCP output or logs.
- Pass cancellation through tool wrappers. Avoid anonymous return types when a
  stable structured schema is useful.

## Checks

- Update surface tests for every tool, resource, prompt, annotation, schema,
  operation-mode, or toolset change. They prove prefixed names, one capability
  resource, the three workflow prompts, exact toolset membership, default and
  explicit profile behavior, and that legacy decision surfaces are absent.
- Use App unit tests for presenters and registration, and process E2E tests
  for transport-visible behavior.

```sh
task test:unit
task test:e2e
```
