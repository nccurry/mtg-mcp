# Changelog

## Unreleased

- Removed the audited legacy product implementation from the isolated rewrite
  branch while retaining ordinary Git history and released artifacts.
- Reduced the production solution to dependency-light `MtgMcp.Core` and a
  minimal resources-only `MtgMcp.App` stdio server with no provider surface.
- Rebuilt focused unit, architecture, and process tests and reconciled task,
  CI, coverage, package, release, and smoke wiring with the foundation project
  set.
- Added closed operation-result and evidence-descriptor unions with stable,
  case-specific JSON contracts.
- Added `read-only`, `local`, and `remote` mode enforcement; JSON, environment,
  and command-line configuration; a non-creating `mtg-mcp/v0.9` data root; and
  sanitized legacy-data and startup boundaries.
- Added official MCP initialization and the deterministic
  `mtg://server/capabilities` resource with zero tools and prompts.
- Split the one-shot process probe from official-client MCP smoke coverage,
  added installed-package MCP validation, and made the App project version the
  default for package and release automation.
- Added immutable format-neutral deck contracts, revisioned SQLite persistence,
  stable pagination, transactional entry/category mutations, and
  provider-neutral synchronization bindings.
- Added guarded opaque deck backup creation, inventory, restore, rollback, and
  deletion with integrity, schema, and database-fingerprint checks.
- Added the exact nineteen-tool local `deck_*` surface, read-only mode filtering,
  official-client schema/annotation tests, and representative MCP workflows.

## 0.8.0 - 2026-06-28

- Audited package boundaries, release metadata, docs, and local build coverage
  for the refactor release.
- Added MCP host observability with redacted per-tool completion logs,
  OpenTelemetry-ready tool-call metrics/traces, `logging/setLevel` handling,
  and `mcpLoggingLevel` in server diagnostics.
- Added report-only performance ratchet output for release review.
- Fixed local release smoke and install validation to use the repo-pinned .NET
  runtime when validating the packaged tool.
- Added observability, MCP compatibility, performance ratchet, and 1.0 readiness documentation.
- Replaced persisted deck edit operations and several closed-set statuses with
  typed union/enum models while preserving legacy JSON strings and flat plan
  operation payloads.
- Removed unsupported Reddit discussion and Spicerack source integrations from
  registration, CLI auth help, docs, and surface tests.
- Added MCP protocol conformance improvements: object-root typed tools now
  advertise titles and output schemas with structured content, tool validation
  errors return a coded structured payload, `workspace_list` and
  `deck_plan_list` use cursor-paged envelopes, and `mtg://workspaces` is
  discoverable as a resource.
- Unified MCP `detailLevel` parsing across presenters while keeping legacy
  `compact` and `includeWorkspace` inputs during the deprecation window.
- Added method-level MCP toolset and operation-mode filtering so deployments
  advertise only selected tools that can run in the configured mode.
- Added report-only MCP surface metrics, README surface parity coverage, a
  pre-1.0 versioning/deprecation policy.
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
