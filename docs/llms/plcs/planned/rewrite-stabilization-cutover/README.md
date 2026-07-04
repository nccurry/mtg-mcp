# Rewrite Stabilization And 0.9.0 Cutover PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/rewrite-stabilization-cutover/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: AMEND-004 re-review

## Summary

This packet defines the integration, verification, packaging, release, rollback,
and PLC-lifecycle gates for the evidence-first `0.9.0` rewrite. It adds no
capability. Its job is to prove that the ten prerequisite child implementations agree
on architecture and MCP contracts, remove the prohibited legacy surface, and can
be released without changing or deleting legacy user data.

Cutover is blocked by an incomplete child, missing verified Archidekt
deck/folder/snapshot cleanup, unresolved high-severity defects, schema drift,
inadequate coverage, or a failed required offline gate.

## Dependencies

- [Legacy Surface Audit And Disposition](../../completed/legacy-surface-audit-and-disposition/README.md)
- [Rewrite Skeleton And Repository Foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Local Deck Domain And SQLite Store](../../completed/local-deck-store/README.md)
- [Manual Deck Interchange](../../in-progress/manual-deck-interchange/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Scryfall Corpus And Evidence](../scryfall-corpus-and-evidence/README.md)
- [Archidekt Decks, Folders, Snapshots, And Synchronization](../archidekt-deck-sync/README.md)
- [Playgroup Official API](../playgroup-public-api/README.md)
- [Exact Deck Statistics](../exact-deck-statistics/README.md)
- [Deterministic Deck Categorization](../deterministic-deck-categorization/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Derive the release surface from the approved capability children and validate it exactly. | Accepted | The proposed AMEND-004 baseline has 91 tools in the remote `all` profile; it is a consistency check, not a compatibility target or design constraint. |
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
modes, versioned databases, clean-break version, offline coverage, prohibited
surface removal, and no-migration policy without authorizing implementation.

## Toolset And North-Star Acceptance

- Toolset scope: validate the default, `all`, `none`, and representative explicit
  profiles across every operation mode. Cutover adds no toolset of its own.
- User question answered: does the released server provide a coherent set of
  evidence and explicit workflows that an LLM can reliably compose for deck
  building without carrying irrelevant provider surfaces?
- Evidence type: the release keeps provider facts, community evidence, exact
  derivations, parser classifications, heuristics, and unavailable states
  visibly distinct.
- Replay boundary: canonical manifests, profile/mode matrices, schema snapshots,
  package versions, fixture revisions, and acceptance evidence identify what
  was released.
- Unknown boundary: provider drift, missing credentials, stale evidence,
  partial operations, unsupported behavior, and deferred features remain
  visible and release-blocking where their child PLC requires it.
- Decision boundary: no stable tool, prompt, resource, router, or profile makes
  deckbuilding judgments for the client LLM.
- Complete LLM workflow: default sessions support local deck work, official
  card and tag evidence, deterministic caller-configured categorization, and
  exact statistics; explicitly enabled provider toolsets add their bounded
  workflows without widening operation-mode authority.

This packet incorporates proposed umbrella amendment AMEND-004 for planning
consistency. Neither AMEND-004 nor this child is approved for implementation.

## Planning Approval

- Status: Draft; AMEND-004 re-review required
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
