# Scryfall Corpus And Evidence Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Add lossless provider contracts, normalized card/face/tag/ruling evidence, exact tool schemas, and dated official fixtures. | SCRY-001, SCRY-013 through SCRY-015, SCRY-021 through SCRY-024 | Contract, serialization, source-label, and architecture tests pass. | Complete |
| 2 | Add versioned SQLite schema, request snapshots, cursors, and current/previous corpus lifecycle. | SCRY-003, SCRY-004, SCRY-011, SCRY-012, SCRY-018 through SCRY-020 | Migration, replay, activation, rollback, delete, and corruption tests pass. | Complete |
| 3 | Add streaming four-dataset bulk synchronization and validation. | SCRY-001, SCRY-002, SCRY-005, SCRY-020 | Bounded-memory JSONL, disk, cancellation, and atomic failure tests pass. | Complete |
| 4 | Add official HTTP policy, TTL behavior, local-first identity reads, authoritative searches, and collection partitioning. | SCRY-006 through SCRY-010, SCRY-016, SCRY-017 | Fake HTTP/clock and multi-process coordination tests pass. | Complete |
| 5 | Register the eighteen-tool `scryfall` surface and composed deck-evidence workflow. | SCRY-021 through SCRY-026 | Toolset/mode, official-client, docs, coverage, package, installed-tool, and opt-in real-corpus gates pass. | Complete |

## Rules

- AMEND-004 and this packet must be approved before implementation.
- Move the packet to `in-progress/` before production edits.
- Keep normal tests offline; never download the real corpus in `task test`.
- Add no Tagger transport, category mapper, random operation, background sync,
  generic provider router, or local Scryfall query evaluator.
- Preserve current behavior until the complete validated surface is registered;
  do not expose half-built tools.

## Rollout And Rollback

This is clean-break preview data with no legacy import. A failed corpus sync
leaves the active generation unchanged. A bad activated generation can be
explicitly rolled back to the retained predecessor. Removing the module
unregisters only its tools; deleting `scryfall.db` requires explicit user
consent and does not affect `decks.db`.

## Completion Criteria

- [x] Every Must requirement maps to implementation and tests.
- [x] The opt-in real-corpus acceptance workflow passes outside normal CI.
- [x] Current/previous generations and immutable request snapshots coexist.
- [x] Separate MCP processes safely reuse one database.
- [x] All audits and full repository/package gates pass.
