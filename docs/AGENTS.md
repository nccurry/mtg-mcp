# docs — Agent Instructions

The root `AGENTS.md` still applies. This file adds rules for `docs/`.

## What this folder owns

Product, architecture, adapter, and versioning docs, plus agent docs, plans,
and PLC packets under `docs/llms/`.

## Rules

- Keep docs aligned with `README.md`, `CONTRIBUTING.md`, `SECURITY.md`,
  `Taskfile.yml`, `global.json`, project files, and current source.
- Link to source files instead of copying SDK versions, package versions,
  command lists, or task details that change.
- Use language tags on code fences.
- Start ordinary plans from `docs/llms/templates/IMPLEMENTATION_PLAN.md`.
- Label current behavior and historical or reviewed-design behavior clearly.
  Do not present an unimplemented idea as current user guidance.
- Do not present legacy recommendation, intent, simulation, Moxfield-network,
  or `plan`/`apply` behavior as the target architecture.
- Keep `north-star.md`, `design-goals.md`, and `heuristic-models.md` short,
  consistent with each other, and linked from the architecture docs.
- Describe source observations and heuristic outputs with their limits. Do not
  call popularity, community tags, or simulation results universal facts.
- Do not revive separate Tagger-site scraping, storage, adapter, prefix, or
  toolset guidance. Community tags come from official Scryfall data.
- Keep checked-in reference snapshots repeatable and record their source, date,
  and reason for each update.

## Checks

```sh
git diff --check
```

Also check links and rendered Markdown when formatting is not simple.
