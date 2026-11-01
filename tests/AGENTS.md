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

## Recorded provider fixtures

Each adapter test project keeps real provider responses under
`Fixtures/recorded/<route>/<case>.json`, listed in `Fixtures/recorded/manifest.json`
(id, method, URL, route, status, file, capture date, SHA-256, what was scrubbed).
The shared loader, `ReplayHttpHandler`, and `JsonShape` live in
`MtgMcp.Core.Tests/Recorded` and are linked into adapter test projects.

- To add a recording: add a manifest entry (leave `sha256` empty; set
  `trimData` to cap a `data` array), run `task fixtures:refresh`, review
  the diff, and read it through `ReplayHttpHandler` and `RecordedManifest`.
  Never edit a recording or hash by hand; a test fails on any mismatch or
  unlisted file.
- Refresh only after a provider bug report, before a release, or to add a case.
  It sends read-only requests, scrubs personal and volatile fields, and the diff
  shows what moved. `refresh: false` keeps an older recording as it is.
- Compare shapes with `JsonShape`, not exact bytes.
- The linked `Recorded` helpers are compiled into several test projects, so
  they must stay dependency-free.
