# Test Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `tests/`.

## Test Shape

- Prefer focused tests that prove observable behavior.
- Keep normal tests deterministic, offline, and free of real provider mutations.
- Use fake HTTP, fixtures, temporary files, and in-memory repositories before live services.
- Mark live network or real-provider tests with `Category=Live` so `task test` stays safe.
- Avoid asserting implementation choreography when a behavior assertion is available.
- Add useful XML summaries to test types, test methods, fixtures, and private
  helpers without restating their names.
- Treat 90 percent per-production-assembly line coverage as a floor, not a
  substitute for failure, boundary, and edge-case coverage.

## Validation

- Run the affected test project first for code changes.
- Use `task test:unit` for focused behavior, `task test:integration` for current
  cross-project integration checks, and `task test:e2e` for process behavior.
- Run `task lint` or `task test` when public APIs, shared helpers, or MCP shape changes.
- Update architecture tests when project boundaries or adapter references intentionally change.
- Update MCP surface tests when tool, resource, prompt, annotation, or operation-mode visibility changes.
- When capability toolsets are implemented, test the default, `all`, `none`,
  and representative explicit profiles across every operation mode. Prove that
  toolset selection changes relevance only and never expands mode authority.
- For rewrite work, derive expected surface/mode behavior from the active child
  manifest rather than legacy counts or compatibility aliases. Keep provider
  facts, exact derivations, parser classifications, sampled estimates,
  heuristics, and unavailable states visibly distinct in assertions.
