# Scryfall Store Ownership Extraction Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Reviewers: independent design reviewer and adapter maintainer
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: No

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-07 | mtg-mcp | Initial Phase 1A child packet. |

## Executive Summary

MtgMcp.Scryfall has named stores, but they do not own their declared behavior.
Every store forwards to ScryfallDatabase. This child moves the current SQLite
work into those stores without changing the service, MCP contract, or database.

The result is a real internal boundary. Corpus changes stay in the corpus store.
Snapshot changes stay in the snapshot store. Acquisition coordination stays in
the coordination store.

## Audience

This document is for the repository owner and implementation agents. A reader
needs familiarity with the evidence-first rewrite and the Scryfall adapter.

## References

- [Parent implementation plan](../evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-1a-scryfall-ownership-extraction)
- [Parent requirements](../evidence-first-deckbuilding-evolution/SRD.md#requirements)
- [Parent architecture](../evidence-first-deckbuilding-evolution/SADD.md#building-blocks)
- [Rewrite guide](../../../../rewrite-guide.md)
- [Scryfall adapter instructions](../../../../../src/MtgMcp.Scryfall/AGENTS.md)
- [Current database owner](../../../../../src/MtgMcp.Scryfall/ScryfallDatabase.cs)
- [Current forwarding stores](../../../../../src/MtgMcp.Scryfall/ScryfallStores.cs)
- [Current Scryfall test fixture](../../../../../tests/MtgMcp.Scryfall.Tests/ScryfallTestFixture.cs)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| A maintainer can find corpus SQL in one named owner. | ScryfallCorpusStore contains the corpus workflow methods and helpers. | The database owner has no corpus workflow methods. |
| A maintainer can find snapshot SQL in one named owner. | ScryfallSnapshotStore contains snapshot lookup, write, replay, list, and delete methods. | The database owner has no snapshot workflow methods. |
| A maintainer can find acquisition coordination in one named owner. | ScryfallRequestCoordinationStore contains leases, pacing, and metadata-check writes. | Corpus status can still read the metadata-check timestamp. |
| Existing callers receive the same results. | Focused behavior tests pass before and after the physical move. | The child does not change public results. |
| An installed Scryfall database remains valid. | The schema version, checksum, tables, indexes, and state transitions are unchanged. | No migration runs. |

## System Overview

ScryfallCardEvidenceOperations composes one ScryfallDatabase and three named
stores. The service and facade already call the stores. The stores currently
delegate every operation to the database owner.

This child keeps the composition shape. It changes where the existing SQL and
domain helpers live. MtgMcp.Core, MtgMcp.App, and all MCP registration stay out
of scope.

## Assumptions, Dependencies, And Constraints

- Phase 0 in the parent PLC must complete before production edits begin.
- The user selected direct integration on main. The target worktree is clean.
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
| CASE-001 | A maintainer changes a corpus query. | The maintainer changes ScryfallCorpusStore and its focused tests. |
| CASE-002 | A maintainer changes snapshot replay. | The maintainer changes ScryfallSnapshotStore and snapshot tests. |
| CASE-003 | A maintainer changes global request pacing. | The maintainer changes ScryfallRequestCoordinationStore and coordination tests. |
| CASE-004 | An existing MCP client calls a Scryfall tool. | The tool gives the same schema, output, errors, cache behavior, and source meaning. |
| CASE-005 | A user retains an existing scryfall.db. | The new process opens the database without a migration or data change. |

## Scope And Non-Scope

### In Scope

- Move corpus-generation, card, ruling, tag, import, activation, rollback, and
  deletion SQL into ScryfallCorpusStore.
- Move immutable request-snapshot SQL into ScryfallSnapshotStore.
- Move acquisition leases, provider-start pacing, and metadata-check writes into
  ScryfallRequestCoordinationStore.
- Keep ScryfallDatabase as the concrete path, connection, schema, and disposal owner.
- Put shared SQLite value codecs in a small local ScryfallSql helper only when
  more than one real store needs the same codec.
- Move the hash helper used only by ScryfallCardEvidenceOperations into that type.
- Move the tag-weight ordering helper into ScryfallCorpusStore.
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
- The Scryfall corpus, snapshot, and coordination stores.
- The existing scryfall.db file and its shared-process pacing state.
- Offline Scryfall adapter and architecture test suites.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| SSO-001 | Must | Architecture | ScryfallCorpusStore contains the current corpus workflow implementations. | A named owner must own its behavior. | The database declares no corpus workflow method. Focused corpus tests pass. |
| SSO-002 | Must | Architecture | ScryfallSnapshotStore contains the current snapshot workflow implementations. | Snapshot changes need an isolated home. | The database declares no snapshot workflow method. Snapshot tests pass. |
| SSO-003 | Must | Architecture | ScryfallRequestCoordinationStore contains lease, pacing, and metadata-check write implementations. | Cross-process acquisition state has one owner. | Coordination tests pass and the corpus store does not write the metadata-check timestamp. |
| SSO-004 | Must | Architecture | ScryfallDatabase owns only path, connection, schema, and disposal concerns. | It must not remain a hidden workflow owner. | An ownership test rejects corpus, snapshot, and coordination workflow methods on the database type. |
| SSO-005 | Must | Compatibility | The child preserves the Scryfall public surface and persisted data format. | This is a behavior-preserving refactor. | Surface reports match. Schema version and checksum stay unchanged. Existing fixture databases work. |
| SSO-006 | Must | Reliability | The child preserves typed outcomes, atomic corpus state, snapshot immutability, lease ownership, pacing, and cancellation behavior. | Ownership movement must not change failure or concurrency behavior. | Existing and new characterization tests pass before and after the move. |
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
| Corpus metadata-check write | Moves from the corpus store API to the coordination store API. |
| scryfall.db schema and contents | No change. |

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Maintainability | A corpus-only change is reviewed. | Corpus SQL and helpers have one type-owned location. |
| Compatibility | A process reopens a pre-existing fixture database. | The schema checksum test passes without migration. |
| Reliability | A sync fails or a cancellation occurs. | Active corpus state remains atomic. |
| Concurrency | Two processes acquire or pace requests. | Lease and pacing tests retain the same ordering and ownership. |
| Testability | The child runs in normal CI. | Focused tests use no network or real provider mutation. |
| Performance | The refactor moves existing code only. | No benchmark is added. The child adds no new hot path. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- |
| 1 | Add direct-store behavior characterization. | SSO-005 to SSO-007 | New tests pass against the forwarding baseline. |
| 2 | Move corpus ownership. | SSO-001, SSO-004, SSO-006 | Corpus methods and helpers leave the database. Focused corpus tests pass. |
| 3 | Move snapshot and coordination ownership. | SSO-002 to SSO-006 | Snapshot and coordination methods leave the database. Focused tests pass. |
| 4 | Close the child. | SSO-001 to SSO-008 | Broad tests, coverage, surface report, audit, and documentation checks pass. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| SSO-001 | [Building blocks](SADD.md#building-blocks) | Corpus characterization and ownership test | Scryfall store tests |
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
| The metadata-check timestamp lives in corpus_state. | Risk | Its writer can look like corpus ownership. | Implementer | Assign the write to coordination because it records acquisition scheduling. Keep status reads in the corpus store. |
| Existing tests call database workflow methods directly. | Risk | Tests can preserve the wrong boundary. | Implementer | Rewrite those tests to call the named store. |
| A source-only ownership test can become too brittle. | Risk | Refactors can fail without a behavior defect. | Implementer | Assert only the declared method boundary. Keep behavior tests as the primary proof. |
| Technical decision needed | Question | None found. | Owner | The selected concrete-store design is sufficient for this child. |

## Validation

Run the focused Scryfall test project after each move. Then run:

1. task lint.
2. task test.
3. task coverage.
4. task surface:report.
5. git diff --check and local Markdown-link inspection.
6. The aggregate audit-codebase gate after the final code block.

## Definition Of Done

- [ ] All Must requirements have passing objective evidence.
- [ ] ScryfallDatabase has no corpus, snapshot, or coordination workflow implementation.
- [ ] The three stores own their declared SQL domains.
- [ ] No public surface or persisted data format changed.
- [ ] Normal tests remain deterministic and offline.
- [ ] The phase-close audit passes.
- [ ] The parent and child documentation record final validation and disposition.
