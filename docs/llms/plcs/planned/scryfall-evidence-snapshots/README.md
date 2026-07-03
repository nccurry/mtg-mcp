# Scryfall Evidence Snapshots PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/scryfall-evidence-snapshots/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines explicit acquisition of official Scryfall reads into
immutable named snapshots stored in `scryfall.db`. Cached reads never contact
Scryfall, and a refresh creates a new linked snapshot rather than changing old
evidence. Results preserve the source object and expose a small normalized card
projection with snapshot provenance.

## Dependencies

- [Rewrite Foundation](../../in-progress/rewrite-skeleton-foundation/README.md)
- [Local Deck Store](../local-deck-store/README.md)
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

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
