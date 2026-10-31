# src — Agent Instructions

The root `AGENTS.md` still applies. This file adds rules for `src/`.

## What this folder owns

The production projects. Each project folder may add its own `AGENTS.md`.

## Rules

- Keep provider-specific cache, auth, pacing, and user-agent behavior inside
  the owning adapter unless Core already has a shared primitive for it.
- Use `docs/adapters.md` as the shared provider map instead of repeating
  adapter rules in project-level instruction files.
- Keep tool annotations, descriptions, and resource URIs accurate when public
  MCP behavior changes.
- Preserve deterministic sort keys, source metadata, assumptions, warnings,
  and confidence fields in evidence outputs.
- Document new persistence or configuration formats in docs and cover them in
  tests.
- Update architecture or surface tests when project references, public MCP
  shape, operation modes, or adapter boundaries change on purpose.

## Checks

```sh
task lint
task test
```
