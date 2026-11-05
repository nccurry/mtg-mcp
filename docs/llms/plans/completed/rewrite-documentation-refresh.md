# Rewrite Documentation Refresh

## Status

Completed on 2026-07-12.

## Goal

Make current documentation match the implemented `0.9.0-preview.1` MCP.
Keep instructions short, task-oriented, and explicit about safety boundaries.

## Findings

- Current guidance still described a 90-tool runtime.
- Categorization was still labeled planned after implementation.
- Live-acceptance documentation predated the final 93-tool run.
- The stabilization PLC was still blocked on completed prerequisites.
- The README mixed current usage, implementation detail, and historical status.

## Changes

1. Rewrite the README around setup, configuration, and common workflows.
2. Synchronize the 32/54/54 default and 57/80/93 complete surfaces.
3. Document deterministic categorization and final live-acceptance dispositions.
4. Move the planning umbrella to `completed/` and activate stabilization.
5. Keep stable release, cross-platform installation, and rollback approval open.
6. Remove stale links and active-language references to removed behavior.

## Validation

- Search current guidance for stale tool counts and lifecycle states.
- Validate relative Markdown links.
- Run `git diff --check`.
- Run the repository lint, test, coverage, package, and MCP smoke gates.
