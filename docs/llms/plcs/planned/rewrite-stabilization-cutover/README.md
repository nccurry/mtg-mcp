# Rewrite Stabilization And 0.9.0 Cutover PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/rewrite-stabilization-cutover/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines the integration, verification, packaging, release, rollback,
and PLC-lifecycle gates for the evidence-first `0.9.0` rewrite. It adds no
capability. Its job is to prove that the nine stable child implementations agree
on architecture and MCP contracts, remove the prohibited legacy surface, and can
be released without changing or deleting legacy user data.

Cutover is blocked by an incomplete child, missing verified Archidekt
deck/folder/snapshot cleanup, unresolved high-severity defects, schema drift,
inadequate coverage, or a failed required offline gate.

## Dependencies

- [Legacy Surface Audit And Disposition](../legacy-surface-audit-and-disposition/README.md)
- [Rewrite Skeleton And Repository Foundation](../rewrite-skeleton-foundation/README.md)
- [Local Deck Domain And SQLite Store](../local-deck-store/README.md)
- [Manual Deck Interchange](../manual-deck-interchange/README.md)
- [Scryfall Evidence Snapshots](../scryfall-evidence-snapshots/README.md)
- [Archidekt Decks, Folders, Snapshots, And Synchronization](../archidekt-deck-sync/README.md)
- [Playgroup Official API](../playgroup-public-api/README.md)
- [Exact Deck Statistics](../exact-deck-statistics/README.md)
- [Scryfall Tagger Cache](../scryfall-tagger-cache/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Derive the release surface from the approved capability children and validate it exactly. | Accepted | The current 84-tool count is a drift-detection baseline, not a compatibility target or design constraint. |
| Require ordinary Git history-preserving integration. | Proposed | The rewrite is a clean product break, not a repository-history rewrite. |
| Require preview releases and cross-platform smoke proof before `0.9.0`. | Proposed | Packaging and host failures must be found before the stable cutover. |
| Keep legacy releases and legacy data directories available for rollback. | Proposed | Rollback must not translate or destroy user data. |
| Block on unresolved priority-1/priority-2 defects and contract drift. | Proposed | Known material defects are incompatible with a stable evidence server. |
| Keep popularity and experimental decision-adjacent work post-cutover. | Proposed | Those topics are outside the stable rewrite acceptance boundary. |

## Non-Goals

- Implementing or changing any child capability.
- Migrating legacy databases, configuration, or tool calls.
- Publishing, tagging, pushing, or merging without explicit release authority.
- Adding popularity, simulation, weakness, replacement, or advisor features.
- Preserving legacy public schemas under compatibility aliases.

## Current-State Disposition

The current production implementation remains reference evidence until the
approved children replace it. Existing task, analyzer, coverage, packaging, and
release wiring may be retained only as allowed by the audit and foundation
children. Legacy feature assemblies and public surfaces are removal targets,
not compatibility layers for this cutover.

## Guardrail Conformance

This child adds no product capability and cannot make deckbuilding decisions.
It verifies the program's project boundaries, evidence distinctions, operation
modes, separate databases, clean-break version, offline coverage, prohibited
surface removal, and no-migration policy without authorizing implementation.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
