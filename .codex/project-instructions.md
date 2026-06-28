# Project Instructions

- Use `Taskfile.yml` for common development workflows.
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
