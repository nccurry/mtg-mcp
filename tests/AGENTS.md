# Test Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `tests/`.

## Test Shape

- Prefer focused tests that prove observable behavior.
- Keep normal tests deterministic, offline, and free of real Archidekt mutations.
- Use fake HTTP, fixtures, temporary files, and in-memory repositories before live services.
- Mark live network or real-provider tests with `Category=Live` so `task test` stays safe.
- Avoid asserting implementation choreography when a behavior assertion is available.

## Validation

- Run the affected test project first for code changes.
- Use `task test:unit` for Core behavior, `task test:integration` for fixture-backed adapter behavior, and `task test:e2e` for mocked MCP process behavior.
- Run `task lint` or `task test` when public APIs, shared helpers, or MCP shape changes.
- Update architecture tests when project boundaries or adapter references intentionally change.
- Update MCP surface tests when tool, resource, prompt, annotation, or operation-mode visibility changes.
