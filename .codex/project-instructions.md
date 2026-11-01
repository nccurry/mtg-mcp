# Project Instructions

- Read `docs/rewrite-guide.md` before evidence-first rewrite work. Do not edit
  production code for a rewrite child until its README records approval and
  `Implementation authorized: Yes`.
- Stable `0.9.0` is an evidence/workflow server with
  `read-only`/`local`/`remote` modes and no advisor, intent, recommendation,
  weak-card, blended-score, or strategic-simulation surface.
- Use `Taskfile.yml` for common development workflows.
- Read `docs/architecture/mcp-surface.md` before changing a tool name,
  description, parameter, or result, and `docs/architecture/deck-model.md`
  before changing deck entries, zones, categories, or change kinds.
- Keep dependency-light domain logic in `MtgMcp.Core`.
- Keep MCP host, tool, resource, prompt, operation-mode, and server-info logic
  in `MtgMcp.App`.
- Keep third-party HTTP contracts in adapter projects.
- Guard mutating MCP tools with `OperationModeGuard`.
- Keep normal tests offline and free of real Archidekt mutations.
- Prefer fixture-backed adapter tests, fake HTTP, temporary files, and
  in-memory repositories.
- Add XML summary comments for new public C# declarations.
- Keep provider errors sanitized and never expose secrets in logs, errors,
  docs, config output, or tests.
- Use `docs/llms/plans/` for durable ordinary plans and `docs/llms/plcs/` for
  larger requirements-backed work.
