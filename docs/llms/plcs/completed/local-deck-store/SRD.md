# Local Deck Domain And SQLite Store Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are the provider-neutral local deck model, `decks.db`, migrations,
transactions, revisions, categories, provider bindings, backup/restore, and the
listed MCP tools. Import/export formats, remote sync, Scryfall acquisition,
format legality, collection ownership, recommendations, and rules simulation
are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| DECK-001 | Must | Every deck, entry, category, and provider binding shall have a stable UUID. | Round-trip and mutation tests preserve IDs. |
| DECK-002 | Must | A deck shall store name, description, format identifier, revision, and timestamps. | Schema and CRUD tests cover every field. |
| DECK-003 | Must | An entry shall store quantity, name, optional Oracle/printing identity, set/collector, language, finish, zone, and order. | Name-only and fully identified fixtures round-trip losslessly. |
| DECK-004 | Must | Zones shall be independent normalized strings with `main` as default. | Moving zones never changes category assignments. |
| DECK-005 | Must | Categories shall support name, optional color, order, and many-to-many entry assignments with at most one primary assignment per entry. | Constraint and ordering tests pass. |
| DECK-006 | Must | Categories shall never determine whether an entry counts in a zone. | Count fixtures remain unchanged when categories change. |
| DECK-007 | Must | Mutations of an existing deck shall require the current `expectedRevision` and increment revision once per successful transaction. | Stale writes return conflict and change no rows. |
| DECK-008 | Must | `deck_apply_changes` shall apply an explicit ordered operation union atomically. | Any invalid operation rolls back the full batch. |
| DECK-009 | Must | Reads shall return canonical ordering and stable pagination. | Repeated reads over unchanged data are byte-equivalent after serialization. |
| DECK-010 | Must | Provider bindings shall store provider ID, remote ID/URI, canonical baseline fingerprint, remote version, and sync timestamps without provider payload types. | Multiple-provider and conflict fixtures pass. |
| DECK-011 | Must | SQLite shall enable foreign keys, WAL, busy timeout, and transactional numbered migrations. | Integration tests inspect pragmas and migration rollback. |
| DECK-012 | Must | A pre-migration backup shall be created before a destructive schema change. | Failed migration leaves original DB and recoverable backup. |
| DECK-013 | Must | Backup tools shall use opaque backup IDs and never return absolute local paths. | Surface and redaction tests pass. |
| DECK-014 | Must | Normal operations shall remain offline and cancellation-aware. | Cancellation and no-network architecture tests pass. |
| DECK-015 | Must | The module shall not import legacy workspace JSON automatically. | Legacy files remain unchanged and invisible to `deck_list`. |
| DECK-016 | Must | Every write tool shall require `local` or `remote`; reads shall be visible in all modes. | Mode/surface tests cover every tool. |
| DECK-017 | Must | `deck_validate` shall report only local schema, reference, quantity, category-primary, zone, and Commander fixture-structure invariants; it shall not determine format legality, semantic roles, provider validity, or strategic quality. | Validation fixtures and forbidden-dependency tests pass. |
| DECK-018 | Must | Granular mutation tools shall execute the same operation cases and transactional service used by `deck_apply_changes`; no second mutation semantics may exist. | Single-operation granular and batch calls produce equivalent rows, revision, and result. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Durability | Atomic transaction or no state change; backup before destructive migration. |
| Concurrency | Stale revisions fail deterministically; busy timeout is bounded. |
| Determinism | Canonical ordering and stable serialized projections. |
| Portability | SQLite file and versioned native model use platform-neutral values. |
| Safety | No provider calls, rules inference, secret paths, or hidden coalescing. |

## Traceability

| Requirements | Design | Validation |
| --- | --- | --- |
| DECK-001 through DECK-006 | Domain/schema design | Unit and SQLite round-trip fixtures |
| DECK-007 through DECK-009 | Transaction flow | Conflict, rollback, ordering tests |
| DECK-010 | Binding/baseline design | Multi-provider fixtures |
| DECK-011 through DECK-015 | Database lifecycle | Migration, backup, cancellation, legacy-isolation tests |
| DECK-016, DECK-017, DECK-018 | MCP mode/validation/mutation design | Surface, equivalence, forbidden-dependency, and E2E tests |

## Definition Of Done

- [ ] All requirements and migration tests pass.
- [ ] `MtgMcp.Decks` maintains 90-percent line coverage.
- [ ] No provider or decision dependency enters the module.
- [ ] Interchange and remote behavior remain unimplemented.
