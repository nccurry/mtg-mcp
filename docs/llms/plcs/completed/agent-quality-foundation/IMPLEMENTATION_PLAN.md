# Agent Quality Foundation Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phase Summary

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Activate the durable packet. | All | Packet is in `in-progress/`. | Completed |
| 2 | Land docs, guidance, orientation, and templates. | REQ-001–REQ-004 | Links and documentation checks pass. | Completed |
| 3 | Make plan mode the least-privilege default. | REQ-008 | Focused App and E2E tests pass. | Completed |
| 4 | Enable strict formatting and analyzers. | REQ-005–REQ-006 | `task lint` is green without a blanket baseline. | Completed |
| 5 | Reach 90 percent per production assembly. | REQ-007, REQ-009 | `task coverage` passes all eight gates. | Completed |
| 6 | Create follow-up packets and close validation. | REQ-010 | Full validation passes and evidence is recorded. | Completed |

## Phase Rules

- Preserve unrelated worktree files and existing PLC packets.
- Run the narrow affected tests before broad tasks.
- Keep every phase green before advancing.
- Use fixture, fake HTTP, temporary-file, or in-memory tests.
- Remove temporary ratchets and advisory configuration before completion.

## Rollback And Cleanup

The operation-mode change can be bypassed by explicitly configuring `apply`.
Analyzer packages are build-only and can be removed without runtime migration.
Template moves require updating links but do not alter existing packets.
Temporary coverage reports remain ignored build artifacts.
