# Local Deck Domain And SQLite Store Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Add Core deck contracts and operation unions. | DECK-001 through DECK-010 | Pure unit tests pass. | Complete |
| 2 | Add SQLite schema, migrations, and repository. | DECK-001 through DECK-012, DECK-014, DECK-015 | Temporary-DB integration tests pass. | Complete |
| 3 | Add transactional service and backup/restore. | DECK-007 through DECK-014 | Conflict, rollback, backup, and corruption tests pass. | Complete |
| 4 | Add exact `deck_*` MCP surface over the shared transactional service. | DECK-009, DECK-013, DECK-016 through DECK-018 | Schema, granular/batch equivalence, mode, and mocked E2E tests pass. | Complete |
| 5 | Wire the new assembly and run full validation. | All | Applicable task lists include Decks; lint, tests, per-assembly coverage, package, and docs pass. | Complete |

## Rules

- Move this packet to `in-progress/` before implementation.
- Do not add import/export/provider calls or semantic category inference.
- Commit migrations and their rollback/failure fixtures together.
- Keep the database usable after every phase.

## Rollback

Before public use, rollback removes the new module/database. After a preview is
published, schema changes roll forward; restore uses verified backups rather
than down migrations.

## Completion Criteria

- [x] Every requirement is implemented and traced.
- [x] Schema v1 and backup format are documented.
- [x] All writes are revision-guarded and transactional.
- [x] Full offline validation passes.
