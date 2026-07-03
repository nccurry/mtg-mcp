# Rewrite Skeleton And Repository Foundation PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/rewrite-skeleton-foundation/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines the clean `0.9.0` rewrite skeleton. It removes the audited
legacy product surface on a dedicated future branch while preserving Git
history, repository quality gates, packaging, documentation infrastructure,
and a minimal compilable MCP host. It adds no deck, provider, statistics, or
Tagger capability.

## Dependencies

- Parent: [Evidence-First MCP Rewrite Program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)
- Audit: [Legacy Surface Audit And Disposition](../legacy-surface-audit-and-disposition/README.md)

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Use a normal branch and sibling worktree, not an orphan history. | Proposed | Existing history remains available for evidence and rollback. |
| Start with only `MtgMcp.Core` and `MtgMcp.App` production projects. | Proposed | Capability projects should appear only when their child is implemented. |
| Expose standard MCP server information and `mtg://server/capabilities`, with no tools or prompts. | Proposed | The skeleton proves hosting and contracts without retaining a redundant non-program-prefix tool. |
| Replace modes with `read-only`, `local`, and `remote`; default `local`. | Proposed | Local deck/cache work is useful without remote mutation authority. |
| Use a versioned `v0.9` data directory and no legacy migration. | Proposed | The rewrite is an explicit clean break. |
| Publish previews as `0.9.0-preview.N`. | Proposed | Reviewable packages can coexist with the stable legacy release. |

The `v0.9` data-root token is the schema-family directory and remains stable
across all `0.9.x` packages. `0.9.0-preview.N` is the independently versioned
package/server identity; the two tokens are intentionally not identical.

## Project And Surface Impact

Implementation will replace legacy production and test projects in the rewrite
branch, retain repository infrastructure, and establish boundaries later
children must follow. It does not change `main` until the eventual cutover.

## Guardrail Conformance

The skeleton contains no prompts, recommendations, simulations, provider calls,
or deck decisions. Core remains dependency-light, all results are typed and
evidence-aware, and normal validation is offline.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Validation Evidence

| Date | Check | Result |
| --- | --- | --- |
| 2026-07-03 | Audit dependency and guardrail review | Passed for draft |
| 2026-07-03 | Packet structure and traceability | Passed after final docs review |

## Completion Notes

Implementation begins only after explicit authorization, creation of the named
worktree, approval of the audit disposition matrix, and movement of this packet
to `in-progress/`.
