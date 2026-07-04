# Local Deck Domain And SQLite Store PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/local-deck-store/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines `decks.db` as the durable local source of truth. It models
format-neutral decks, stable entries, zones, functional categories, exact
printing references, optimistic revisions, and provider-neutral sync bindings.
It exposes explicit `deck_*` reads and local mutations without legality,
classification, recommendation, or provider behavior.

## Dependencies

- [Rewrite Skeleton And Repository Foundation](../../completed/rewrite-skeleton-foundation/README.md)
- Parent [rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Use `MtgMcp.Decks` with hand-written `Microsoft.Data.Sqlite` persistence. | Proposed | One cohesive module owns deck transactions without an ORM. |
| Keep zones separate from categories. | Proposed | Functional tags must not silently change deck inclusion/counts. |
| Allow name-only cards with explicit unresolved identity. | Proposed | Local editing must not require network access or invented IDs. |
| Require `expectedRevision` for every existing-deck mutation. | Proposed | Agents receive deterministic conflict refusal instead of lost updates. |
| Permit multiple entries for the same card. | Proposed | Printing, finish, language, zone, and quantity can differ. |
| Keep provider bindings canonical and provider-neutral. | Proposed | Adapters sync through local snapshots without storing transport payloads in Core. |

## Public Surface

Reads: `deck_list`, `deck_get`, `deck_validate`, `deck_backup_list`.

Local writes: `deck_create`, `deck_update`, `deck_delete`, `deck_entry_add`,
`deck_entry_update`, `deck_entry_remove`, `deck_category_create`,
`deck_category_update`, `deck_category_delete`, `deck_category_assign`,
`deck_category_unassign`, `deck_apply_changes`, `deck_backup_create`,
`deck_backup_restore`, and `deck_backup_delete`.

## Guardrail Conformance

The module stores caller choices and exact identities only. It does not choose
cards, assign semantic categories, infer legality, or contact a provider.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Completion Notes

Manual text/JSON interchange is intentionally owned by the next child.
