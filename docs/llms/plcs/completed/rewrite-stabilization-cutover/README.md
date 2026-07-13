# Rewrite Stabilization And 0.9.0 Cutover PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/rewrite-stabilization-cutover/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-12
- Current phase: `0.9.0` released and lifecycle closed

## Summary

This packet defines the integration, verification, packaging, release, rollback,
and PLC-lifecycle gates for the evidence-first `0.9.0` rewrite. It adds no
capability. Its job is to prove that the eleven prerequisite child implementations agree
on architecture and MCP contracts, remove the prohibited legacy surface, and can
be released without changing or deleting legacy user data.

All eleven prerequisites are complete. Packaged live acceptance is recorded in
[LIVE_ACCEPTANCE.md](LIVE_ACCEPTANCE.md). Stable publishing and rollback
approval remain separate release gates.

The current preview gate results are recorded in
[VALIDATION.md](VALIDATION.md).

## Dependencies

- [Legacy Surface Audit And Disposition](../../completed/legacy-surface-audit-and-disposition/README.md)
- [Rewrite Skeleton And Repository Foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Local Deck Domain And SQLite Store](../../completed/local-deck-store/README.md)
- [Manual Deck Interchange](../../completed/manual-deck-interchange/README.md)
- [MCP Contract And Adapter Hardening](../../completed/mcp-contract-and-adapter-hardening/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Scryfall Corpus And Evidence](../../completed/scryfall-corpus-and-evidence/README.md)
- [Archidekt Decks, Folders, Snapshots, And Synchronization](../../completed/archidekt-deck-sync/README.md)
- [Playgroup Official API](../../completed/playgroup-public-api/README.md)
- [Exact Deck Statistics](../../completed/exact-deck-statistics/README.md)
- [Deterministic Deck Categorization](../../completed/deterministic-deck-categorization/README.md)
- [Rewrite program](../../completed/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Derive the release surface from the approved capability children and validate it exactly. | Accepted update | The accepted AMEND-005 baseline has 93 tools in the remote `all` profile; it is a consistency check, not a compatibility target or design constraint. |
| Require ordinary Git history-preserving integration. | Accepted | The rewrite is a clean product break, not a repository-history rewrite. |
| Require preview releases and cross-platform smoke proof before `0.9.0`. | Accepted | Packaging and host failures must be found before the stable cutover. |
| Keep legacy releases and legacy data directories available for rollback. | Accepted | Rollback must not translate or destroy user data. |
| Block on unresolved priority-1/priority-2 defects and contract drift. | Accepted | Known material defects are incompatible with a stable evidence server. |
| Keep popularity and experimental decision-adjacent work post-cutover. | Accepted | Those topics are outside the stable rewrite acceptance boundary. |

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

This packet incorporates accepted AMEND-004 and AMEND-005. It adds no product
capability.

## Planning Approval

- Status: Approved; implementation and release completed
- Reviewed by: Repository owner
- Review date: 2026-07-12
- Reviewed revision: `e0d68e7cf897430f9c43b4657307fd520469cbf7`
- Implementation authorized: Yes
- Release authorized: Yes, by repository owner on 2026-07-12
