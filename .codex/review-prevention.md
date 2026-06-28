# Review Prevention

Use this checklist before handing off broad or risky changes.

## Boundaries And Surfaces

- Core remains dependency-light and does not reference adapters or the MCP host.
- Adapter-specific HTTP payloads, auth, and provider contracts stay in adapter
  projects.
- MCP tools/resources/prompts keep accurate names, descriptions, annotations,
  and operation-mode visibility.
- Architecture and surface tests are updated when boundaries or public MCP shape
  intentionally change.

## Abstractions And Dead Code

- New abstractions pay rent through real duplication removal, clearer ownership,
  or an established local pattern.
- Pass-through layers, one-implementation interfaces, speculative factories, and
  broad helpers are removed or avoided.
- Replaced code paths, compatibility shims, old projections, and unused wrappers
  are deleted unless they remain part of the current API.

## Tests And Validation

- Tests assert behavior, not setup.
- Narrow tests run before broad gates.
- Normal tests do not require network access or real Archidekt mutations.
- Fixture-backed adapter changes include representative success and failure
  payloads.
- Stats Lab, simulation, calibration, and hot-path changes include benchmark or
  calibration evidence when performance is part of the risk.
- Skipped commands are reported with reasons.

## Docs And Generated Artifacts

- Human docs stay aligned with `Taskfile.yml`, `global.json`,
  `Directory.Build.props`, `.editorconfig`, and project files.
- Generated artifacts are not edited by hand.
- Comments describe current code, not planning context or change history.

## Secrets And Providers

- Secrets are not committed, logged, copied into fixtures, or exposed in errors.
- Provider permission sensitivity, cache behavior, and live-test boundaries are
  documented when they change.
- Archidekt mutation paths use safeguards and tests avoid real deck writes.
