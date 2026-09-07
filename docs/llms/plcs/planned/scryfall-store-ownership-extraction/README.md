# Scryfall Store Ownership Extraction PLC Packet

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/scryfall-store-ownership-extraction/
- Parent PLC: [Evidence-First Deckbuilding Evolution](../evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Planning review
- Owner selection: Phase 1A of the parent PLC
- Owner authorization: Recorded from the request to implement the PLC phase by phase in main.
- Independent design review: Pending
- Implementation authorized: No. This value changes to Yes only after the independent review passes.

## Summary

This child makes the Scryfall corpus, snapshot, and coordination stores own
their SQLite work. Today, each named store forwards every call to one large
ScryfallDatabase class.

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
| Put corpus SQL in ScryfallCorpusStore. | Proposed | Corpus generations, cards, rulings, and tags are one data domain. | [SADD](SADD.md#building-blocks) |
| Put immutable request-snapshot SQL in ScryfallSnapshotStore. | Proposed | Snapshot lookup, storage, replay, listing, and deletion have one lifecycle. | [SADD](SADD.md#building-blocks) |
| Put leases, pacing, and metadata-check writes in ScryfallRequestCoordinationStore. | Proposed | These rows coordinate acquisition across processes. | [SADD](SADD.md#building-blocks) |
| Add no repository interface or provider-wide persistence framework. | Proposed | The stores use direct concrete SQLite access inside one adapter project. | [SADD](SADD.md#alternatives-considered) |
| Preserve the schema byte-for-byte. | Proposed | This child changes code ownership, not persisted data or compatibility. | [SRD](SRD.md#scope-and-non-scope) |

## Project And Surface Impact

| Area | Impact |
| --- | --- |
| MtgMcp.Scryfall | Refactor ScryfallDatabase and split ScryfallStores into type-owned source files. |
| MtgMcp.Scryfall.Tests | Add direct-store characterization tests. Update tests that call database workflow methods. |
| MtgMcp.Architecture.Tests | Add a focused ownership assertion if the existing adapter tests cannot state the boundary clearly. |
| MCP tools, resources, prompts, and operation modes | No change. The post-change surface report must match the pre-change report. |
| Scryfall provider contract, headers, pacing policy, cache policy, and retries | No change. |
| scryfall.db, SchemaVersion, SchemaChecksum, tables, indexes, and data retention | No change. |
| Configuration, package references, and Core/App project boundaries | No change. |

## Current Open Questions

No owner decision blocks this child. The selected design keeps direct concrete
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
- [ ] The independent design review has passed.

## Implementation Checklist

- [ ] Move this packet to in-progress after the independent review passes.
- [ ] Add behavior characterization before the physical move.
- [ ] Move corpus ownership.
- [ ] Move snapshot and coordination ownership.
- [ ] Remove forwarding methods and the obsolete aggregate stores file.
- [ ] Run the focused and broad validation gates.
- [ ] Run the phase-close audit and record its result.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Source and test inspection | Passed | ScryfallStores forwards 27 workflow calls to ScryfallDatabase. Existing tests cover corpus lifecycle, snapshots, leases, pacing, cancellation, and typed outcomes. |
| 2026-09-07 | Public-surface inspection | Passed | This child has no tool, mode, configuration, provider, or persistence-format change. |
| 2026-09-07 | Independent design review | Pending | The review must finish before the packet becomes implementation authority. |

## Completion Notes

Not complete. The parent PLC remains the cross-child guardrail. This child only
addresses physical Scryfall persistence ownership.
