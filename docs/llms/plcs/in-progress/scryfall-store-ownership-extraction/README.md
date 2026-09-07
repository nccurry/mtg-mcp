# Scryfall Card Data Store Ownership Extraction PLC Packet

## Lifecycle

- Status: In progress
- Folder: docs/llms/plcs/in-progress/scryfall-store-ownership-extraction/
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Phase 3: snapshot and request-coordination ownership
- Owner selection: Phase 1A of the parent PLC
- Owner authorization: Recorded from the request to implement the PLC phase by phase in main.
- Independent design review: Passed. The final review confirmed the corrected service setup wording.
- Implementation authorized: Yes. The independent review passed on 2026-09-07.

## Summary

This child makes the Scryfall card-data, snapshot, and request-coordination
stores own their SQLite work. Today, each named store forwards every call to
one large ScryfallDatabase class.

The refactor keeps the current database file and all observable behavior. It
does not add a tool, change a tool, call a new Scryfall endpoint, or migrate
data. It removes a misleading internal layer so that a maintainer can change
one persistence domain without navigating unrelated domains.

## Packet Contents

- [SRD.md](SRD.md): scope, requirements, acceptance criteria, and validation.
- [SADD.md](SADD.md): selected ownership design and test architecture.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): ordered implementation work and phase exits.
- [FIXTURES.md](FIXTURES.md): existing fixtures and behavior cases that lock the refactor.

## Decision Snapshot

| Decision | Status | Rationale | Detail |
| --- | --- | --- | --- |
| Keep ScryfallDatabase as a concrete connection and schema owner. | Proposed | It already owns the file path, SQLite initialization, and schema validation. | [SADD](SADD.md#chosen-design) |
| Put all card-data SQL in ScryfallCardDataStore. | Owner approved | Card-data generations, cards, rulings, tags, and the full `corpus_state` row have one lifecycle. | [SADD](SADD.md#building-blocks) |
| Put immutable request-snapshot SQL in ScryfallSnapshotStore. | Proposed | Snapshot lookup, storage, replay, listing, and deletion have one lifecycle. | [SADD](SADD.md#building-blocks) |
| Put leases and provider-start pacing in ScryfallRequestCoordinationStore. | Owner approved | These rows coordinate provider requests across processes. | [SADD](SADD.md#building-blocks) |
| Add ScryfallHash for shared stable hashes. | Proposed | Card data, snapshots, and evidence operations use the same UTF-8 SHA-256 value. | [SADD](SADD.md#shared-sqlite-values) |
| Limit ScryfallSql to value conversion. | Proposed | A helper that runs queries would become another hidden data owner. | [SADD](SADD.md#shared-sqlite-values) |
| Add ScryfallTagWeight for shared tag-weight rules. | Proposed | The service and the card-data store both use the same fixed Scryfall tag-weight order. | [SADD](SADD.md#shared-sqlite-values) |
| Add no repository interface or provider-wide persistence framework. | Proposed | The stores use direct concrete SQLite access inside one adapter project. | [SADD](SADD.md#alternatives-considered) |
| Preserve the schema byte-for-byte. | Proposed | This child changes code ownership, not persisted data or compatibility. | [SRD](SRD.md#scope-and-non-scope) |

## Project And Surface Impact

| Area | Impact |
| --- | --- |
| MtgMcp.Scryfall | Refactor ScryfallDatabase and split ScryfallStores into one source file per store. |
| MtgMcp.Scryfall.Tests | Add direct-store characterization tests. Update tests that call database workflow methods. |
| MtgMcp.Architecture.Tests | Add a focused ownership assertion if the existing adapter tests cannot state the boundary clearly. |
| Internal names | Rename ScryfallCorpusStore, CorpusStore, ScryfallCorpusLifecycleOperations, and their source names to clear card-data names. Public `ScryfallCorpus*` result types and `corpus_*` SQLite names stay unchanged. |
| MCP tools, resources, prompts, and operation modes | No change. The post-change surface report must match the pre-change report. |
| Scryfall provider contract, headers, pacing policy, cache policy, and retries | No change. |
| scryfall.db, SchemaVersion, SchemaChecksum, tables, indexes, and data retention | No change. |
| Configuration, package references, and Core/App project boundaries | No change. |

## Current Open Questions

No owner decision blocks this child. The owner approved this rule: the card-data
store owns every `corpus_state` field, including the metadata-check time. This
keeps activation and deletion atomic. The selected design keeps direct concrete
SQLite dependencies within MtgMcp.Scryfall. It does not require a new interface
or a public contract choice.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements have measurable acceptance criteria.
- [x] The concrete source and test ownership gaps were inspected.
- [x] The persistence and public-surface impacts are explicit.
- [x] Alternatives and their tradeoffs are recorded.
- [x] The Core/App/adapter boundaries remain unchanged.
- [x] The provider contract, offline fixture rule, and cancellation rule remain unchanged.
- [x] The implementation plan has ordered phase exits.
- [x] Deferred work is visible and not required for this child.
- [x] The independent design review has passed.

## Implementation Checklist

- [x] Move this packet to in-progress after the independent review passes.
- [x] Add behavior characterization before the physical move.
- [x] Move card-data ownership.
- [ ] Move snapshot and coordination ownership.
- [ ] Remove forwarding methods and the obsolete aggregate stores file.
- [ ] Run the focused and broad validation gates.
- [ ] Run the phase-close audit and record its result.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Source and test inspection | Passed | ScryfallStores forwards 29 workflow calls to ScryfallDatabase. Existing tests cover card-data lifecycle, snapshots, leases, pacing, cancellation, and typed outcomes. |
| 2026-09-07 | Public-surface inspection | Passed | This child has no tool, mode, configuration, provider, or persistence-format change. |
| 2026-09-07 | Independent design review | Passed | The review found an atomic-state ownership problem, a shared tag-weight rule, incomplete state-test coverage, misplaced stored records, unclear internal names, and one service setup wording error. The fixes passed a final review. |
| 2026-09-07 | Phase 1 focused tests | Passed | The offline Scryfall test suite passed 38 of 38 tests, including direct card-data and snapshot store coverage. |
| 2026-09-07 | Phase 1 test review | Passed | The test review found no missing coverage in the changed paths. Existing service tests retain guard, failure, and cancellation coverage. |
| 2026-09-07 | Phase 2 card-data ownership | Passed | Card-data SQL and its internal records now live in ScryfallCardDataStore. ScryfallDatabase has no card-data workflow method. |
| 2026-09-07 | Phase 2 focused tests | Passed | The installed .NET 11 preview 6 built the targeted project, and the offline Scryfall suite passed 45 of 45 tests. The project-selected preview SDK is not available locally yet, so its normal run remains part of Phase 4 validation. |
| 2026-09-07 | Phase 2 naming and ownership audit | Passed | Internal names now say card data. Public ScryfallCorpus names, corpus SQLite names, source values, and error codes stay unchanged for compatibility. |

## Completion Notes

Not complete. The parent PLC remains the cross-child guardrail. This child only
addresses physical Scryfall persistence ownership.
