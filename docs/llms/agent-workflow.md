# Agent Workflow

## First Pass

1. Read the relevant code, tests, project files, and docs before editing.
2. Check `Taskfile.yml` for existing repo operations.
3. Prefer existing project boundaries, models, helpers, and naming.
4. Make the smallest coherent change that satisfies the request.

## Planning

- Use no durable plan for tiny single-area fixes unless the user asks for one.
- Use `docs/llms/plans/` for ordinary plans that should travel with the repo.
- Use `docs/llms/plcs/` for larger requirements-backed work that crosses
  project boundaries, changes public MCP shape, changes adapter contracts,
  affects persistence formats, or needs phased delivery.
- Keep ignored `/plans/` for local scratch only.
- Start durable plans and PLC packets from `docs/llms/templates/`.

## Editing

- Keep dependency-light behavior in `MtgMcp.Core`.
- Keep MCP host and tool surface behavior in `MtgMcp.App`.
- Keep third-party HTTP contracts in adapter projects.
- Guard mutating MCP tools with `OperationModeGuard`.
- Avoid unrelated formatting churn.
- Do not edit generated output under `artifacts/`, `bin/`, `obj/`, `coverage/`,
  package output, or release archives.

## Validation

- Run the narrow relevant Task first.
- Run `task lint` or `task test` for shared behavior, public APIs, operation
  modes, adapter contracts, project files, or tool/resource/prompt shape.
- For docs-only agent guidance changes, run `git diff --check` and inspect the
  changed docs.
- If the pinned .NET SDK or local tools are unavailable, state what was missing
  and which checks could not run.
