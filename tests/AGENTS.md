# tests — Agent Instructions

The root `AGENTS.md` still applies. This file adds rules for `tests/`.

## What this folder owns

One test project per production project, plus `MtgMcp.Architecture.Tests`
and the process-level `MtgMcp.E2E.Tests`.

## Rules

- Prefer focused tests that prove observable behavior. Avoid asserting
  implementation steps when a behavior assertion is available.
- Treat the 90 percent coverage floor as a minimum, not a substitute for
  failure, boundary, and edge-case tests.
- Update architecture tests when project boundaries or adapter references
  change on purpose.
- Update MCP surface tests when tool, resource, prompt, annotation, or
  operation-mode visibility changes.
- Test the `default`, `all`, `none`, and representative explicit toolset
  profiles across every operation mode. Prove that toolset selection changes
  only which tools are visible and never expands what a mode may write.
- Derive expected surface and mode behavior from current code and docs, not
  from legacy counts or compatibility aliases.
- Keep provider facts, exact derivations, parser classifications, sampled
  estimates, heuristics, and unavailable states visibly distinct in
  assertions.

## Checks

Run the affected test project first, then widen:

```sh
task test:unit
task test:integration
task test:e2e
task test
```
