# Scryfall Evidence Snapshots PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/scryfall-evidence-snapshots/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: draft review

## Summary

This packet defines explicit acquisition of official Scryfall reads into
immutable named snapshots stored in `scryfall.db`. Cached reads never contact
Scryfall, and a refresh creates a new linked snapshot rather than changing old
evidence. Results preserve the source object and expose a small normalized card
projection with snapshot provenance.

## Dependencies

- [Rewrite Foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Local Deck Store](../../completed/local-deck-store/README.md)
- [MCP Capability Toolsets](../mcp-capability-toolsets/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Snapshot Scryfall's own query results instead of reimplementing its query language. | Proposed | Query semantics remain authoritative. |
| Store raw response pages plus typed projections. | Proposed | Fidelity and ergonomic deck workflows are both available. |
| Make snapshots immutable; refresh creates lineage. | Proposed | Same snapshot ID always returns the same evidence. |
| Exclude random-card operations. | Proposed | Randomness violates the stable evidence contract. |
| Serialize provider requests at least 125 ms apart. | Proposed | Conservatively remains under Scryfall's published 10 requests/second guidance. |
| Stop on 403/429 rather than retrying through blocks. | Proposed | Provider safety outranks acquisition completion. |

## Public Surface

Reads: `scryfall_snapshot_list`, `scryfall_snapshot_get`,
`scryfall_snapshot_objects`, and `scryfall_snapshot_card`.

Local acquisition/mutation: `scryfall_snapshot_create`,
`scryfall_snapshot_refresh`, and `scryfall_snapshot_delete`.

The bulk request case snapshots Scryfall's bulk-data metadata only. It does not
download, ingest, index, or locally reproduce the bulk card dataset.

## Guardrail Conformance

Every result identifies snapshot, request, retrieval time, source URI,
completeness, and limitations. Live source changes do not alter an existing
snapshot, and no card role or deck recommendation is inferred.

## Toolset And North-Star Acceptance

- Toolset: `scryfall`, enabled by the default profile.
- Surface rule: parameterized query, acquisition, and cached-read operations
  remain explicit tools because their inputs and failure states differ. No
  parallel discovery alias or generic provider router is permitted.
- User question answered: what official Scryfall card, printing, ruling, set,
  catalog, or query evidence is available for this deckbuilding question?
- Evidence type: attributable provider facts preserved in an immutable named
  snapshot, never a quality judgment.
- Replay boundary: snapshot identity, request, source pages, retrieval metadata,
  checksum, and lineage make the same cached read reproducible.
- Unknown boundary: missing, partial, failed, canceled, too-large, deleted, and
  stale evidence remain explicit and never become invented card facts.
- Decision boundary: the tools never recommend cards or infer card roles.
- Complete LLM workflow: inspect a local deck, resolve its cards or run a
  declared Scryfall query, read the immutable evidence, and let the client LLM
  explain or decide using that evidence.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
