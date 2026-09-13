# Documentation Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `docs/`.

## Source Of Truth

- Keep docs aligned with `README.md`, `CONTRIBUTING.md`, `SECURITY.md`,
  `Taskfile.yml`, `global.json`, project files, and current source.
- Prefer links to the source files over duplicating changing SDK versions,
  package versions, command lists, or task details.
- Use language tags for code fences.

## Agent-Facing Docs

- Put durable ordinary implementation plans under `docs/llms/plans/`.
- Put larger Plan-Led Change packets under `docs/llms/plcs/`.
- Start ordinary implementation plans from
  `docs/llms/templates/IMPLEMENTATION_PLAN.md`.
- Keep PLC packets in `planned/`, `in-progress/`, or `completed/` based on their
  status.
- Treat completed PLCs as historical context; current code and tests still win.

## Product Direction

- Read [`rewrite-guide.md`](rewrite-guide.md) before editing durable product,
  architecture, provider, or agent guidance for the `0.9.0` rewrite.
- Label current-release behavior and historical/reviewed design behavior
  explicitly. Do not present an unimplemented idea as current user guidance.
- Conversely, do not present legacy recommendation, intent, simulation,
  Moxfield-network, or `plan`/`apply` behavior as target architecture.
- Keep `north-star.md`, `design-goals.md`, and `heuristic-models.md` concise,
  mutually consistent, and linked from architecture docs.
- Describe source observations and heuristic outputs with their limitations;
  do not call popularity, community tags, or simulation results universal facts.
- For Scryfall work, follow accepted AMEND-004 and the completed
  `scryfall-corpus-and-evidence` child. Do not revive separate Tagger-site
  scraping, storage, adapter, prefix, or toolset guidance.
- Accepted AMEND-005 and the completed hardening, exact-statistics, and
  categorization children govern the current runtime. The completed
  stabilization child records the `0.9.0` release boundary.

## Generated And Reference Data

- Do not hand-edit generated build files, package output, coverage output,
  benchmark output, or release archives.
- Keep checked-in reference snapshots repeatable and document their source, date,
  and update reason.
- For docs-only changes, run `git diff --check` and inspect rendered Markdown
  when formatting is not simple.
