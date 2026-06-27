# Changelog

## Unreleased

- Unified MCP `detailLevel` parsing across presenters while keeping legacy
  `compact` and `includeWorkspace` inputs during the deprecation window.
- Added method-level MCP toolset and operation-mode filtering so deployments
  advertise only selected tools that can run in the configured mode.
- Added report-only MCP surface metrics, README surface parity coverage, a
  pre-1.0 versioning/deprecation policy, and initial ADR process docs.
- Documented the full registered MCP tool/resource/prompt surface in the README.
- Added explicit ramp-evaluator applicability metadata to `deck_evaluate_card`
  and labeled non-ramp cards with `not-applicable` instead of relying on a bare
  zero score.
- Added `RngKind` metadata to the heuristic goldfish result family, labeled as
  `system-random` to distinguish it from the Stats Lab deterministic RNG.
- Aligned default app assembly/package version metadata with `server.json` so
  local smoke output reports the intended pre-release version unless release
  tasks override it.
- Initial MCP server scaffold with Scryfall research, local workspaces, Archidekt writeback, checkpoints, prompts, resources, Taskfile workflows, and tests.
