# Archidekt Essentials And Synchronization Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 0 | Carry forward the accepted provider-risk record and establish discoverable live-test scaffolding. | ARCH-016, ARCH-017 | The 2026-07-03 owner decision is linked; `Category=Live` discovers a safely skipped test with no credentials. |
| 1 | Re-verify/pin observed contracts and repeat the early private create/delete cleanup spike through the new adapter. | ARCH-001 through ARCH-004, ARCH-010 through ARCH-013, ARCH-016, ARCH-017 | Sanitized fixtures pass; the configured-credential throwaway create/delete leaves no residual deck; pacing evidence stays within 30 starts per minute. |
| 2 | Implement safe auth/read client, mapping, pacing, and request preflight. | ARCH-001 through ARCH-004, ARCH-012, ARCH-013 | Adapter, fake-clock, and zero-write cap tests pass. |
| 3 | Implement canonical diff and transactional pull. | ARCH-005 through ARCH-009, ARCH-019 | Conflict/state/local transaction tests pass. |
| 4 | Implement create, primitive push/delete, and optional proven bulk path. | ARCH-006, ARCH-008 through ARCH-014, ARCH-018, ARCH-019 | Request sequence, bulk equivalence/disablement, and partial failure tests pass. |
| 5 | Add MCP surface and complete live proof. | ARCH-015 through ARCH-019 | Surface/E2E, combined DB/HTTP, live cleanup, and full offline gates pass. |

## Rules

- Recheck Archidekt behavior before coding because the web API is not a stable
  public specification.
- Treat contract drift as adapter maintenance; do not add compatibility aliases
  or guess replacement endpoints.
- Do not expand to folders, snapshots, collaboration, or account features.
- Never weaken guards to make a flaky live test pass.
- Keep live tests opt-in and cleanup-first.

## Rollback

Disable/unregister the adapter without changing local decks. Provider bindings
and baselines remain local evidence; users may continue with manual export.
