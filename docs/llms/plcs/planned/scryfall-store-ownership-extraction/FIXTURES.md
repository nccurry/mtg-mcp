# Scryfall Store Ownership Extraction Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| SSO-FIX-001 | Fake Scryfall HTTP and compressed JSONL | tests/MtgMcp.Scryfall.Tests/ScryfallTestFixture.cs | Corpus installation, cards, rulings, tags, and provider responses. | Scryfall tests | Update only when the supported official contract changes. |
| SSO-FIX-002 | Temporary SQLite directory | TemporaryScryfallDirectory in ScryfallTestFixture.cs | Isolated database lifecycle and reopen behavior. | Scryfall tests | Keep per-test isolation. |
| SSO-FIX-003 | Corpus lifecycle scenario | ScryfallServiceTests.CorpusLifecycle_RetainsTwoGenerationsAndGuardsMutation | Active, previous, rollback, and delete guards. | Scryfall tests | Retain exact behavior. |
| SSO-FIX-004 | Snapshot scenario | ScryfallServiceTests.ProviderReads_CaptureReplayAndDeleteImmutableSnapshots | Immutable write, list, replay, checksum, and guarded delete behavior. | Scryfall tests | Retain exact behavior. |
| SSO-FIX-005 | Coordination scenario | ScryfallCoordinationTests | Lease ownership, expiry, and global pacing. | Scryfall tests | Retain exact behavior. |
| SSO-FIX-006 | Schema-corruption scenario | ScryfallCoordinationTests.Database_RejectsMismatchedMigrationChecksum | Existing schema checksum rejection. | Scryfall tests | Retain exact behavior. |
| SSO-FIX-007 | Import-failure scenario | ScryfallServiceTests.FailedCorpusSyncAndCancellation_LeaveActiveGenerationAtomic | Atomic staging and cancellation behavior. | Scryfall tests | Retain exact behavior. |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| SSO-001 | SSO-FIX-001, SSO-FIX-003, SSO-FIX-007 | Corpus behavior remains unchanged after SQL moves. | Direct-store and service tests. |
| SSO-002 | SSO-FIX-004 | Snapshot order, checksums, replay, and deletion guards remain unchanged. | Direct-store and service tests. |
| SSO-003 | SSO-FIX-005 | Lease and pacing results retain the existing order and owner rules. | Coordination tests. |
| SSO-004 | Source ownership scenario | Database type exposes no domain workflow method. | Architecture or reflection test. |
| SSO-005 | SSO-FIX-006 and surface report | Existing databases open. MCP surface matches pre-change report. | Schema test and task surface:report. |
| SSO-006 | SSO-FIX-003 through SSO-FIX-007 | Typed outcomes, atomicity, and cancellation remain unchanged. | Focused Scryfall tests. |
| SSO-007 | All fixture entries | Normal tests use fake HTTP and temporary files only. | Test code inspection and non-Live test run. |
| SSO-008 | PLC packet | Parent and child records state the selected scope and validation. | Link and diff inspection. |

## Direct-Store Characterization Cases

| Case | Store | Expected result |
| --- | --- | --- |
| Absent corpus status | ScryfallCorpusStore | It reports the current not-cached state without creating a database. |
| Installed corpus reads | ScryfallCorpusStore | Cards, printings, rulings, tags, and ordering match the current service result. |
| Corpus lifecycle | ScryfallCorpusStore | Import, activation, rollback, and guarded deletion retain current atomic state. |
| Snapshot replay | ScryfallSnapshotStore | The snapshot ID, member order, checksum, and pagination remain stable. |
| Snapshot deletion | ScryfallSnapshotStore | Incorrect acknowledgement or checksum gives the current typed failure. |
| Lease ownership | ScryfallRequestCoordinationStore | A different owner cannot release or acquire an active lease. |
| Provider pacing | ScryfallRequestCoordinationStore | Two database instances reserve the current global timeline. |
| Metadata check | ScryfallRequestCoordinationStore | A successful metadata check updates the current timestamp with no corpus replacement. |

## MCP Surface Checks

This child has no MCP surface change. The required result is equality between
the pre-change and post-change task surface:report output.

## Provider Fixtures

This child uses the existing sanitized fixture. It does not add a live call,
capture a new payload, or change provider retention.

## Calibration Or Performance Cases

No performance measurement applies. The child introduces no new hot path.
