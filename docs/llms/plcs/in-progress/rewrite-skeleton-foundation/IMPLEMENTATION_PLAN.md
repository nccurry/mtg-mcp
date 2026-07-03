# Rewrite Skeleton And Repository Foundation Implementation Plan

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phase Summary

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 0 | Confirm implementation entry gate. | FND-014 | Audit disposition is approved, this child is authorized/in progress, and the accepted mode guardrail is recorded. | Completed |
| 1 | Create isolated rewrite branch/worktree. | FND-001, FND-002 | Ancestry and worktree safety checks pass. | Authorized; not started |
| 2A | Remove only audit-approved product projects/code. | FND-003, FND-014 | Project removal and lifecycle documentation diff match the audit allowlist. | Not started |
| 2B | Restore a minimal compiling Core/App solution. | FND-003, FND-004 | Focused build and architecture tests pass. | Not started |
| 2C | Reconcile repository tasks and tests with the new project set. | FND-011 | Coverage conveniences, integration lists, surface filters, lint, tests, coverage, package, and smoke reference no removed project. | Not started |
| 3 | Add common result/evidence contracts and modes. | FND-006 through FND-010, FND-012 | Core/App focused tests pass. | Not started |
| 4 | Expose minimal MCP surface. | FND-005 | Surface snapshot and process E2E pass. | Not started |
| 5 | Package preview and close validation. | FND-011, FND-013 | Full offline gates and preview smoke pass. | Not started |

## Implementation Rules

- Move this packet to `in-progress/` before Phase 0.
- Do not begin Phase 1 until the audit disposition matrix is approved.
- The accepted program guardrail intentionally replaces historical
  `read-only`/`plan`/`apply` vocabulary with `read-only`/`local`/`remote`; any
  different mode set requires an umbrella amendment before code edits.
- Never modify the primary worktree's uncommitted files.
- Remove legacy code only from the rewrite branch and only according to the
  approved audit.
- Do not add empty capability projects or compatibility aliases.
- Keep every phase green and commit reviewable boundaries.
- Keep project removal, minimal build restoration, and task/test rewiring in
  separate reviewable commits or equivalent review units.

## Validation

Run narrow Core/App tests first, then `task lint`, `task test`, `task coverage`,
`task pack`, and `task smoke:mcp`. Verify the MCP surface is exactly zero tools,
one resource, and zero prompts. Inspect package contents for legacy assemblies
and data migration code.

## Rollback

Delete the linked rewrite worktree and local branch only after confirming no
uncommitted work. Main and the stable legacy release remain unchanged.
