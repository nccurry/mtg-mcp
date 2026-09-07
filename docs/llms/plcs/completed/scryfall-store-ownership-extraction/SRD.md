# Scryfall Card Data Store Ownership Extraction Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Reviewers: independent design reviewer and adapter maintainer
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-07 | mtg-mcp | Initial Phase 1A child packet. |
| 2026-09-07 | mtg-mcp | The owner assigned all `corpus_state` reads and writes to ScryfallCardDataStore after independent review. |
| 2026-09-07 | mtg-mcp | The owner approved clear internal card-data names and record ownership. |
| 2026-09-07 | mtg-mcp | Independent review passed. Phase 1 direct-store characterization is authorized. |
| 2026-09-07 | mtg-mcp | Implementation, final validation, and the ownership audit completed. |

## Executive Summary

Before this child, MtgMcp.Scryfall had named stores that did not own their
declared behavior. Every store forwarded to ScryfallDatabase. This completed
child moved the SQLite work into those stores without changing the service,
MCP contract, or database.

The result is a real internal boundary. Card-data changes stay in
ScryfallCardDataStore. Snapshot changes stay in ScryfallSnapshotStore. Request
leases and pacing stay in ScryfallRequestCoordinationStore.

## Audience

This document is for the repository owner and implementation agents. A reader
needs familiarity with the evidence-first rewrite and the Scryfall adapter.

## References

- [Parent implementation plan](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-1a-scryfall-ownership-extraction)
- [Parent requirements](../../planned/evidence-first-deckbuilding-evolution/SRD.md#requirements)
- [Parent architecture](../../planned/evidence-first-deckbuilding-evolution/SADD.md#building-blocks)
- [Rewrite guide](../../../../rewrite-guide.md)
- [Scryfall adapter instructions](../../../../../src/MtgMcp.Scryfall/AGENTS.md)
- [Database owner](../../../../../src/MtgMcp.Scryfall/ScryfallDatabase.cs)
- [Card-data store](../../../../../src/MtgMcp.Scryfall/ScryfallCardDataStore.cs)
- [Snapshot store](../../../../../src/MtgMcp.Scryfall/ScryfallSnapshotStore.cs)
- [Request-coordination store](../../../../../src/MtgMcp.Scryfall/ScryfallRequestCoordinationStore.cs)
- [Current Scryfall test fixture](../../../../../tests/MtgMcp.Scryfall.Tests/ScryfallTestFixture.cs)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| A maintainer can find card-data SQL in one named owner. | ScryfallCardDataStore contains card-data workflow methods, helpers, and every `corpus_state` read and write. | The database owner has no card-data workflow methods. |
| A maintainer can find snapshot SQL in one named owner. | ScryfallSnapshotStore contains snapshot lookup, write, replay, list, and delete methods. | The database owner has no snapshot workflow methods. |
| A maintainer can find request coordination in one named owner. | ScryfallRequestCoordinationStore contains leases and provider-start pacing. | The card-data store retains every `corpus_state` write. |
| Existing callers receive the same results. | Focused behavior tests pass before and after the physical move. | The child does not change public results. |
| An installed Scryfall database remains valid. | The schema version, checksum, tables, indexes, and state transitions are unchanged. | No migration runs. |

## System Overview

ScryfallCardEvidenceOperations creates one ScryfallDatabase, three named
stores, and the provider client. ScryfallService creates
ScryfallCardEvidenceOperations plus lifecycle and snapshot operation classes.
Those operation classes receive ScryfallCardEvidenceOperations. Each store
contains its own SQLite workflow code and uses the database owner only for
connections and schema validation.

This child keeps that setup. It changes where the existing SQL and domain
helpers live. MtgMcp.Core, MtgMcp.App, and all MCP registration stay out of
scope.

## Assumptions, Dependencies, And Constraints

- Phase 0 in the parent PLC must complete before production edits begin.
- The user selected direct integration on main.
- The existing .NET 11 toolchain and nullable settings stay in force.
- Normal tests remain offline. They use temporary SQLite directories and fake HTTP.
- ScryfallDatabase stays the only type that creates paths, opens connections,
  initializes SQLite, validates the schema, and disposes its initialization gate.
- The stores use a concrete ScryfallDatabase dependency. No repository interface
  or generic persistence layer is permitted.
- Cancellation tokens pass through every moved asynchronous path.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | A maintainer changes a card-data query. | The maintainer changes ScryfallCardDataStore and its focused tests. |
| CASE-002 | A maintainer changes snapshot replay. | The maintainer changes ScryfallSnapshotStore and snapshot tests. |
| CASE-003 | A maintainer changes global request pacing. | The maintainer changes ScryfallRequestCoordinationStore and coordination tests. |
| CASE-004 | An existing MCP client calls a Scryfall tool. | The tool gives the same schema, output, errors, cache behavior, and source meaning. |
| CASE-005 | A user retains an existing scryfall.db. | The new process opens the database without a migration or data change. |

## Scope And Non-Scope

### In Scope

- Move card-data generation, card, ruling, tag, import, activation, rollback,
  deletion, and metadata-check SQL into ScryfallCardDataStore.
- Move immutable request-snapshot SQL into ScryfallSnapshotStore.
- Move acquisition leases and provider-start pacing into
  ScryfallRequestCoordinationStore.
- Keep ScryfallDatabase as the concrete path, connection, schema, and disposal owner.
- Put shared SQLite value codecs in a small local ScryfallSql helper only when
  more than one real store needs the same codec. It must not run a query.
- Move the shared stable hash into ScryfallHash. It keeps the current UTF-8,
  lowercase SHA-256 behavior without a database dependency.
- Move the shared tag-weight rule into ScryfallTagWeight.
- Move StoredCorpusObject, StoredCorpusCollection, StoredTag,
  StoredTagAssignment, and StoredCardsByTag beside ScryfallCardDataStore.
- Move StoredSnapshotHeader and StoredSnapshot beside ScryfallSnapshotStore.
- Rename internal card-data store names. Keep public `ScryfallCorpus*` result
  names and `corpus_*` SQLite names unchanged.
- Rename StoredCorpusObject and StoredCorpusCollection to StoredCardDataObject
  and StoredCardDataCollection.
- Remove the unused active-generation GetDirectTagsAsync method after a source
  reference check and compiler proof.
- Split the aggregate ScryfallStores.cs file into type-named store files.
- Add direct-store characterization and ownership tests.

### Out Of Scope

- New or changed MCP tools, resources, prompts, toolsets, modes, schemas, or descriptions.
- A Scryfall API, bulk-data, header, retry, cache, or pacing-policy change.
- A SQLite schema, migration, table, index, retention, or database-file change.
- A local arbitrary-query engine, new tags, or Tagger-site acquisition.
- A generic repository, data-access interface, service locator, or provider framework.
- Package upgrades, project-reference changes, benchmark work, or App/Core refactoring.

### Compatibility Target

The completed child preserves the pre-change MCP surface report exactly. It
also preserves Scryfall tool results, typed failure cases, operation-mode
behavior, Scryfall HTTP traffic, SQLite contents, and existing cache lifecycles.

## Stakeholders And Affected Systems

- Players and MCP clients that use current Scryfall tools.
- ScryfallCardEvidenceOperations and ScryfallService.
- The Scryfall card-data, snapshot, and coordination stores.
- The existing scryfall.db file and its shared-process pacing state.
- Offline Scryfall adapter and architecture test suites.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| SSO-001 | Must | Architecture | ScryfallCardDataStore contains the current card-data workflow implementations, including every `corpus_state` read and write. | The active generation, previous generation, and metadata-check time change as one card-data state. | The database declares no card-data workflow method or stored card-data record. Focused card-data tests pass. |
| SSO-002 | Must | Architecture | ScryfallSnapshotStore contains the current snapshot workflow implementations and stored snapshot records. | Snapshot changes need an isolated home. | The database declares no snapshot workflow method or stored snapshot record. Snapshot tests pass. |
| SSO-003 | Must | Architecture | ScryfallRequestCoordinationStore contains lease and pacing implementations. | Cross-process request coordination has one owner. | Coordination tests pass and the coordination store does not access `corpus_state`. |
| SSO-004 | Must | Architecture | ScryfallDatabase owns only path, connection, schema, and disposal concerns. | It must not remain a hidden workflow owner. | An ownership test rejects card-data, snapshot, and coordination workflow methods on the database type. |
| SSO-005 | Must | Compatibility | The child preserves the Scryfall public surface and persisted data format. | This is a behavior-preserving refactor. | Surface reports match. Schema version and checksum stay unchanged. Existing fixture databases work. |
| SSO-006 | Must | Reliability | The child preserves typed outcomes, atomic card-data state, snapshot immutability, lease ownership, pacing, and cancellation behavior. | Ownership movement must not change failure or concurrency behavior. | Existing and new characterization tests pass before and after the move. |
| SSO-007 | Must | Testability | The child uses deterministic offline tests for every moved data domain. | Refactoring needs behavior evidence, not only line coverage. | Tests use fake HTTP and temporary directories. No new Live test is added. |
| SSO-008 | Must | Documentation | The packet and parent roadmap record the selected child, boundary, validation, and completion status. | Future work needs an accurate source of truth. | Links resolve and git diff --check passes. |

## Interfaces, Data, States, And Modes

This child changes only internal classes in MtgMcp.Scryfall.

| Interface or state | Change |
| --- | --- |
| Scryfall MCP tools, resources, prompts, toolsets, and operation modes | No change. |
| ScryfallService and Scryfall facade public methods | No change. |
| Scryfall provider requests, response mapping, headers, retries, and cache policy | No change. |
| ScryfallDatabase constructor and Exists property | Retained as internal concrete support. |
| Store constructors and internal methods | Rewritten to contain the current SQL behavior. |
| Card-data metadata-check write | Stays in the ScryfallCardDataStore API because it updates `corpus_state`. |
| scryfall.db schema and contents | No change. |

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Maintainability | A card-data-only change is reviewed. | Card-data SQL and helpers have one type-owned location. |
| Compatibility | A process reopens a pre-existing fixture database. | The schema checksum test passes without migration. |
| Reliability | A sync fails or a cancellation occurs. | Active card-data state remains atomic. |
| Concurrency | Two processes acquire or pace requests. | Lease and pacing tests retain the same ordering and ownership. |
| Testability | The child runs in normal CI. | Focused tests use no network or real provider mutation. |
| Performance | The refactor moves existing code only. | No benchmark is added. The child adds no new hot path. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- |
| 1 | Add direct-store behavior characterization. | SSO-005 to SSO-007 | New tests pass against the forwarding baseline. |
| 2 | Move card-data ownership. | SSO-001, SSO-004, SSO-006 | Card-data methods and helpers leave the database. Focused card-data tests pass. |
| 3 | Move snapshot and coordination ownership. | SSO-002 to SSO-006 | Snapshot and coordination methods leave the database. Focused tests pass. |
| 4 | Close the child. | SSO-001 to SSO-008 | Broad tests, coverage, surface report, audit, and documentation checks pass. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| SSO-001 | [Building blocks](SADD.md#building-blocks) | Card-data characterization and ownership test | Scryfall store tests |
| SSO-002 | [Building blocks](SADD.md#building-blocks) | Snapshot characterization and ownership test | Scryfall store tests |
| SSO-003 | [Building blocks](SADD.md#building-blocks) | Coordination tests and source ownership test | ScryfallCoordinationTests |
| SSO-004 | [Chosen design](SADD.md#chosen-design) | Reflection or source ownership assertion | Architecture or Scryfall tests |
| SSO-005 | [Data design](SADD.md#data-design) | Surface report and schema checksum test | Task gates |
| SSO-006 | [Error handling](SADD.md#error-handling-and-failure-modes) | Existing and direct-store behavior tests | Scryfall test suite |
| SSO-007 | [Test architecture](SADD.md#test-architecture) | Fixture inspection and non-Live test run | Scryfall test suite |
| SSO-008 | [Readability and documentation](SADD.md#readability-and-documentation) | Markdown links and diff check | PLC packet |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| SQL helpers can move with the wrong data domain. | Risk | A future change can again cross unrelated files. | Implementer | Move each helper with its only caller. Use ScryfallSql only for shared value codecs. |
| The metadata-check timestamp lives in corpus_state. | Resolved decision | A split owner can break atomic card-data state changes. | Owner | Keep every `corpus_state` read and write in ScryfallCardDataStore. |
| Existing tests call database workflow methods directly. | Resolved | Tests can preserve the wrong boundary. | Implementer | Tests now construct the named store around the database owner. |
| A source-only ownership test can become too brittle. | Risk | Refactors can fail without a behavior defect. | Implementer | Assert only the declared method boundary. Keep behavior tests as the primary proof. |
| Technical decision needed | Resolved | None remain. | Owner | The owner approved the concrete-store design and the `corpus_state` boundary. |

## Validation

Run the focused Scryfall test project after each move. Then run:

1. task lint.
2. task test.
3. task coverage.
4. task surface:report.
5. git diff --check and local Markdown-link inspection.
6. The aggregate audit-codebase gate after the final code block.

## Definition Of Done

- [x] All Must requirements have passing objective evidence.
- [x] ScryfallDatabase has no card-data, snapshot, or coordination workflow implementation.
- [x] The three stores own their declared SQL domains.
- [x] No public surface or persisted data format changed.
- [x] Normal tests remain deterministic and offline.
- [x] The phase-close audit passes.
- [x] The parent and child documentation record final validation and disposition.
