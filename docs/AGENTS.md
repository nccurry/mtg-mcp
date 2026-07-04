# Documentation Instructions

Root `AGENTS.md` remains authoritative. This file adds defaults for `docs/`.

## Source Of Truth

- Keep docs aligned with `README.md`, `CONTRIBUTING.md`, `SECURITY.md`,
  `Taskfile.yml`, `global.json`, project files, and current source.
- Prefer links to source-of-truth files over duplicating volatile SDK versions, package versions, command lists, or workflow details.
- Use language tags for code fences.

## Agent-Facing Docs

- Put durable ordinary implementation plans under `docs/llms/plans/`.
- Put larger Plan-Led Change packets under `docs/llms/plcs/`.
- Start reusable plans and PLCs from `docs/llms/templates/`; keep templates out
  of lifecycle folders.
- Keep PLC packets in `planned/`, `in-progress/`, or `completed/` according to their lifecycle.
- Treat completed PLCs as historical context; current code and tests still win.

## Product Direction

- Read [`rewrite-guide.md`](rewrite-guide.md) before editing durable product,
  architecture, provider, or agent guidance for the `0.9.0` rewrite.
- Label current-release behavior and rewrite-target behavior explicitly. Do not
  rewrite current user instructions as if unimplemented target behavior exists.
- Conversely, do not present legacy recommendation, intent, simulation,
  Moxfield-network, or `plan`/`apply` behavior as target architecture.
- Keep `north-star.md`, `design-goals.md`, and `heuristic-models.md` concise,
  mutually consistent, and linked from architecture docs.
- Describe source observations and heuristic outputs with their limitations;
  do not call popularity, community tags, or simulation results universal facts.
- For Scryfall planning, route future authority through proposed AMEND-004 and
  the `scryfall-corpus-and-evidence` child. Do not revive separate Tagger-site
  scraping, storage, adapter, prefix, or toolset guidance.

## Generated And Reference Data

- Do not hand-edit generated build artifacts, package output, coverage output, benchmark output, or release archives.
- Keep checked-in reference snapshots deterministic and document their source, date, and update reason.
- For docs-only changes, run `git diff --check` and inspect the rendered markdown when formatting is non-trivial.
