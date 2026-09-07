# Scryfall Card Data Store Ownership Extraction Implementation Plan

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-1a-scryfall-ownership-extraction)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)
- Implementation authorized: Yes

## Implementation Strategy

First, add direct-store characterization tests while the stores still forward.
Then move one data domain at a time. Each move keeps the public service shape
and the SQLite schema unchanged.

The code stays on main because the user selected direct integration. This child
has one tightly coupled data boundary. It has no safe parallel implementation
split.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Characterize direct store behavior. | SSO-005 to SSO-007 | Scryfall tests | Focused non-Live Scryfall tests | Tests pass against the forwarding baseline. | In progress |
| 2 | Move card-data ownership. | SSO-001, SSO-004, SSO-006 | Card-data store, database, tests | Focused card-data tests and audit | Card-data workflow code leaves the database. | Planned |
| 3 | Move snapshot and coordination ownership. | SSO-002 to SSO-006 | Snapshot store, coordination store, database, tests | Focused snapshot and coordination tests | Workflow code leaves the database. | Planned |
| 4 | Close the child. | SSO-001 to SSO-008 | Tests and PLC docs | Task gates and phase-close audit | The combined diff passes all declared gates. | Planned |

## Phase Details

### Phase 1: Characterize Direct Store Behavior

- Problems solved: Existing tests prove service behavior but several tests call
  database workflow methods directly.
- Included requirements: SSO-005, SSO-006, SSO-007.
- Out of scope: Production ownership changes.
- Expected edits: Add a direct-store test class. Change database-direct tests
  to construct the relevant store around the concrete database.
- Tests added: Card-data, snapshot, and coordination cases named in FIXTURES.md.
- Tests added: One fixed-time card-data state test. It proves activation,
  unchanged metadata checks, rollback, and deletion keep the expected three
  `corpus_state` fields together. Add ScryfallHash and ScryfallTagWeight tests.
- Validation: Run the Scryfall non-Live test project before the next phase.
- Exit criteria: The new tests pass while the existing stores still forward.
- Rollback or fallback: Keep the test-only commit if the next phase finds an
  ownership boundary that needs more characterization.
- Cleanup: None.

### Phase 2: Move Card-Data Ownership

- Problems solved: ScryfallCardDataStore is a forwarding wrapper with an unclear name.
- Included requirements: SSO-001, SSO-004, SSO-006.
- Out of scope: Snapshot and coordination SQL, except shared value codecs.
- Expected edits:
  - Create ScryfallCardDataStore.cs.
  - Move card-data read, import, lifecycle, tag, and metadata-check SQL with
    its helpers.
  - Keep every `corpus_state` read and write in ScryfallCardDataStore.
  - Create ScryfallHash with the current UTF-8, lowercase SHA-256 behavior.
  - Create ScryfallSql only for shared UUID and UTC value conversion.
  - Create ScryfallTagWeight for the shared fixed tag-weight order.
  - Move stored card-data records beside ScryfallCardDataStore.
  - Rename the internal CorpusStore property and lifecycle operation type to
    CardDataStore and ScryfallCardDataLifecycleOperations.
  - Rename StoredCorpusObject and StoredCorpusCollection to
    StoredCardDataObject and StoredCardDataCollection.
  - Keep public `ScryfallCorpus*` result names and `corpus_*` SQLite names.
  - Remove the uncalled active-generation GetDirectTagsAsync method after
    source and compiler confirmation.
- Tests added: Add only a missing card-data behavior case discovered in Phase 1.
- Validation: Run focused card-data tests and the Scryfall non-Live project.
- Exit criteria: ScryfallDatabase declares no card-data workflow method.
- Rollback or fallback: Revert the card-data-only move. The database format stays valid.
- Cleanup: Remove card-data forwarding methods and stale summaries.

### Phase 3: Move Snapshot And Coordination Ownership

- Problems solved: SnapshotStore and RequestCoordinationStore are forwarding wrappers.
- Included requirements: SSO-002, SSO-003, SSO-004, SSO-005, SSO-006.
- Out of scope: Card-data behavior and provider policy changes.
- Expected edits:
  - Create ScryfallSnapshotStore.cs and ScryfallRequestCoordinationStore.cs.
  - Move snapshot lookup, storage, replay, listing, and delete SQL.
  - Move lease and pacing SQL.
  - Keep the facade call to CardDataStore.RecordMetadataCheckAsync unchanged.
  - Move stored snapshot records beside ScryfallSnapshotStore.
  - Remove ScryfallStores.cs after all three type-owned files compile.
- Tests added: Add only a missing snapshot or coordination behavior case
  discovered in Phase 1.
- Validation: Run focused snapshot and coordination tests, then the Scryfall
  non-Live project.
- Exit criteria: ScryfallDatabase declares no snapshot or coordination workflow method.
- Rollback or fallback: Revert this phase without a data migration.
- Cleanup: Delete forwarding bodies and stale direct database test calls.

### Phase 4: Close The Child

- Problems solved: Ownership work needs proof at the combined boundary.
- Included requirements: SSO-001 to SSO-008.
- Out of scope: A public contract, dependency, or provider change.
- Expected edits: Add a narrow ownership assertion. Update this packet and the
  parent PLC with the completed evidence.
- Tests added: No behavior test unless the phase-close audit finds a gap.
- Validation:
  - Run the Scryfall non-Live test project.
  - Run task lint.
  - Run task test.
  - Run task coverage.
  - Run task surface:report.
  - Run git diff --check and local Markdown-link inspection.
  - Run audit-codebase on the combined diff.
- Exit criteria:
  - The aggregate audit result is PASS.
  - The public surface report matches the pre-change report.
  - Coverage remains at or above the repository gate.
  - The packet records all validation.
- Rollback or fallback: Revert the isolated child. No migration or surface
  compatibility cleanup is required.
- Cleanup: Move the completed packet only after all evidence is recorded.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| A moved SQL helper changes ordering or atomicity. | 2 and 3 | Characterize behavior first. Move helpers with their operation. | Implementer |
| Direct database test calls keep the old abstraction alive. | 1 to 4 | Rewrite tests to call the named store. | Implementer |
| Shared codecs become a hidden repository. | 2 and 3 | Limit ScryfallSql to fixed SQLite value conversion. | Implementer |
| The metadata timestamp writer moves incorrectly. | 2 | Keep all `corpus_state` reads and writes in ScryfallCardDataStore. | Implementer |
| Stored records stay in the database file. | 2 and 3 | Move card-data and snapshot records with their owning store. | Implementer |
| An audit asks for a broader design change. | 4 | Stop and ask the owner before extending scope. | Implementer |

## Completion Criteria

- [ ] Each Must requirement has passing evidence.
- [ ] The child has no unreviewed architecture finding.
- [ ] Each named store contains its real SQLite workflow code.
- [ ] ScryfallDatabase contains only infrastructure support.
- [ ] No generic persistence abstraction was added.
- [ ] No provider, MCP, configuration, package, or database format changed.
- [ ] All normal tests remain offline.
- [ ] The parent Phase 1A status and child completion notes are current.
