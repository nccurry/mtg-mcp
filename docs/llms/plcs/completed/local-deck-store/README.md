# Local Deck Domain And SQLite Store PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/local-deck-store/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: Phases 1 through 5 complete

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
| Use `MtgMcp.Decks` with hand-written `Microsoft.Data.Sqlite` persistence. | Accepted | One cohesive module owns deck transactions without an ORM. |
| Keep zones separate from categories. | Accepted | Functional tags must not silently change deck inclusion/counts. |
| Allow name-only cards with explicit unresolved identity. | Accepted | Local editing must not require network access or invented IDs. |
| Require `expectedRevision` for every existing-deck mutation. | Accepted | Agents receive deterministic conflict refusal instead of lost updates. |
| Permit multiple entries for the same card. | Accepted | Printing, finish, language, zone, and quantity can differ. |
| Keep provider bindings canonical and provider-neutral. | Accepted | Adapters sync through local snapshots without storing transport payloads in Core. |

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

- Status: Approved
- Reviewed by: Nick Curry, repository owner
- Review date: 2026-07-03
- Reviewed revision: `c15476d`
- Implementation authorized: Yes

## Completion Notes

Manual text/JSON interchange is intentionally owned by the next child.

Implementation adds immutable provider-neutral Core contracts, the isolated
`MtgMcp.Decks` SQLite assembly, schema/migration fingerprint checks,
revision-guarded transactions, guarded opaque backups, four read tools, and
fifteen local-write tools. No provider, legality, interchange, recommendation,
or compatibility behavior entered the child.

## Validation Evidence

| Date | Check | Result |
| --- | --- | --- |
| 2026-07-04 | Unit, integration, architecture, and official-client E2E tests | Passed; 95 offline tests, including an all-tools disposable Commander lifecycle and an independent raw-table audit of every schema-v1 column and cascade. |
| 2026-07-04 | Per-assembly line coverage | Passed; App 95.74%, Core 100.00%, Decks 93.53%. |
| 2026-07-03 | MCP surface and mode matrix | Passed; 4/19/19 tools in read-only/local/remote, one resource, zero prompts. |
| 2026-07-03 | Abstraction, code quality, dead code, dependency, test, visual, and docs audits | Passed after fixes; no unresolved finding remains. |
| 2026-07-03 | Requirement and fixture reconciliation | Passed; DECK-001 through DECK-018 trace to implemented tests and behavior. |
