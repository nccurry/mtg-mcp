# Rewrite Skeleton And Repository Foundation PLC Packet

## Lifecycle

- Status: In progress
- Folder: `docs/llms/plcs/in-progress/rewrite-skeleton-foundation/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: Phase 3 complete; Phase 4 not started

## Summary

This packet defines the clean `0.9.0` rewrite skeleton. It removes the audited
legacy product surface on a dedicated future branch while preserving Git
history, repository quality gates, packaging, documentation infrastructure,
and a minimal compilable MCP host. It adds no deck, provider, statistics, or
Tagger capability.

## Dependencies

- Parent: [Evidence-First MCP Rewrite Program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)
- Audit: [Legacy Surface Audit And Disposition](../../completed/legacy-surface-audit-and-disposition/README.md)

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Use a normal branch and sibling worktree, not an orphan history. | Accepted | Existing history remains available for evidence and rollback. |
| Start with only `MtgMcp.Core` and `MtgMcp.App` production projects. | Accepted | Capability projects should appear only when their child is implemented. |
| Expose standard MCP server information and `mtg://server/capabilities`, with no tools or prompts. | Accepted | The skeleton proves hosting and contracts without retaining a redundant non-program-prefix tool. |
| Replace modes with `read-only`, `local`, and `remote`; default `local`. | Accepted | Local deck/cache work is useful without remote mutation authority. |
| Use a versioned `v0.9` data directory and no legacy migration. | Accepted | The rewrite is an explicit clean break. |
| Publish previews as `0.9.0-preview.N`. | Accepted | Reviewable packages can coexist with the stable legacy release. |

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

- Status: Approved
- Reviewed by: Two independent PLC reviewers; accepted by Nick Curry, repository owner
- Review date: 2026-07-03
- Reviewed revision: `9b6bfbd`
- Implementation authorized: Yes

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Audit dependency and guardrail review | Passed for draft | The approved audit remains the removal and reuse authority. |
| 2026-07-03 | Packet structure and traceability | Passed after final docs review | All foundation requirements remain mapped to design and validation evidence. |
| 2026-07-03 | Phase 0 implementation entry gate | Passed | Audit disposition approved/completed; foundation decisions accepted; `read-only`/`local`/`remote` with `local` default recorded; owner authorization received. |
| 2026-07-03 | Phase 1 isolated branch/worktree | Passed | `ncurry/evidence-first-mcp-rewrite` was created at `C:/Users/Nick Curry/Programming/github.com/nccurry/mtg-mcp-evidence-first-rewrite` from `main` commit `c2aeec8`; HEAD and merge base match, the new worktree is clean, and the primary/other worktrees were untouched. |
| 2026-07-03 | Phase 2A audit-approved removal | Passed | Removed the legacy production implementation and product tests only on the rewrite branch. Retained Git history, repository guidance/infrastructure, and the audit's existing annotate-only legacy PLC dispositions; no legacy lifecycle move or unapproved source abstraction was introduced. |
| 2026-07-03 | Phase 2B minimal solution | Passed | The solution contains only `MtgMcp.Core` and `MtgMcp.App` production projects. Architecture tests prove Core has no package/project references, App references only Core and has no runtime package, and legacy MCP registrations are absent. |
| 2026-07-03 | Phase 2C repository reconciliation | Passed | `task lint`, `task test` (10 tests), `task surface:report`, `task coverage`, `task pack`, `task smoke:mcp`, and `task release:tool-smoke VERSION=0.9.0-preview.1` passed. App/Core line coverage is 100%; package inspection contains only App/Core assemblies and required tool assets. |
| 2026-07-03 | Phase 2 post-implementation audits | Passed after fixes | Abstraction, code quality, dead code, dependency, test coverage, test quality, visual readability, and docs-sync audits were run. Findings fixed stale guidance/task references, incomplete wiring assertions, an unbounded process test, an unsupported-argument case, and outdated package pins. Final dependency scans report no vulnerable, deprecated, or outdated packages. |
| 2026-07-03 | Phase 3 contracts, modes, configuration, and clean-break behavior | Passed | Added exhaustive Core result and evidence unions, the `read-only`/`local`/`remote` permission matrix, layered configuration and versioned data-root resolution, path-free status projection and redaction, and read-only legacy detection. `task lint`, `task test` (38 tests), `task surface:report`, `task coverage`, `task pack`, `task smoke:mcp`, and `task release:tool-smoke VERSION=0.9.0-preview.1` passed. App/Core line coverage is 96.06%/100%. |
| 2026-07-03 | Phase 3 post-implementation audits | Passed after fixes | Abstraction, code quality, dead code, dependency, test coverage, test quality, visual readability, and docs-sync audits found and resolved permissive CLI parsing, overly broad configuration ownership, ambiguous evidence naming, incomplete process cleanup and startup-path tests, converter edge cases, and an unexercised redaction boundary. Dependency scans report no vulnerable, deprecated, or outdated packages. |

## Phase 2 Reconciliation

| Phase | Exit criterion | Result |
| --- | --- | --- |
| 2A | Project removal and lifecycle documentation match the approved audit allowlist. | Passed; removed legacy product/runtime/test implementations, preserved history and infrastructure, and left annotate-only PLC dispositions intact. |
| 2B | Minimal Core/App build and architecture tests pass. | Passed; exactly two production projects remain and the enforced dependency graph is App to Core only. |
| 2C | Coverage conveniences, integration lists, surface filters, lint, tests, coverage, package, release, and smoke name no removed project. | Passed; static wiring tests and Task-based validation cover every listed path. |

## Phase 3 Reconciliation

| Requirement | Result |
| --- | --- |
| FND-006 | Passed; operation mode defaults to `local`, parses only the three accepted names, and enforces the complete read/provider-read/local-write/remote-write permission matrix. |
| FND-007 | Passed; the exhaustive `OperationResult<T>` union distinguishes success, not found, not cached, unsupported, unavailable, conflict, and invalid input with stable JSON discriminators and explicit payloads. |
| FND-008 | Passed; the exhaustive evidence union preserves source fact, source evidence, exact derivation, parser classification, heuristic estimate, and sampled estimate as visibly distinct cases with case-specific metadata. |
| FND-009 | Passed; JSON, `MTGMCP__*` environment variables, and command-line configuration use documented precedence, the default root resolves beneath platform application data at `mtg-mcp/v0.9`, overrides are supported, and resolution creates no storage. |
| FND-010 | Passed; public configuration status is path-free, loader and CLI failures are sanitized, and the error boundary redacts supplied credentials, tokens, cookies, and absolute paths. |
| FND-012 | Passed; startup detects legacy sibling data conservatively, reports the clean-break state without parsing or migration, preserves legacy bytes, and does not create the `v0.9` root. |

## Completion Notes

Phases 0 through 3 are complete. The branch is a compiling, packaged process
skeleton with common contracts, modes, configuration, and clean-break behavior,
but still has no MCP surface. Phase 4 is the next unimplemented phase and will
add only the minimal server information and capability resource defined here.
