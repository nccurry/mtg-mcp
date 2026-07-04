# Archidekt Decks, Folders, Snapshots, And Synchronization Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 0 | Carry forward the accepted provider-risk record and establish discoverable live-test scaffolding. | ARCH-016, ARCH-017 | The 2026-07-03 owner decision is linked; `Category=Live` discovers a safely skipped test with no credentials. |
| 1 | Re-verify/pin observed deck, folder, and snapshot contracts and repeat early disposable cleanup spikes through the new adapter. | ARCH-001 through ARCH-004, ARCH-010 through ARCH-013, ARCH-016, ARCH-017, ARCH-027, ARCH-028 | Sanitized route fixtures pass; configured-credential deck, empty-folder, and snapshot lifecycle probes leave no residual object; pacing evidence stays within 30 starts per minute. |
| 2 | Implement safe auth/read client, canonical deck/folder/snapshot mapping, pacing, and request preflight. | ARCH-001 through ARCH-004, ARCH-012, ARCH-013, ARCH-020, ARCH-024, ARCH-027 | Adapter, unknown-field, fake-clock, and zero-write cap tests pass. |
| 3 | Implement canonical diff and transactional pull. | ARCH-005 through ARCH-009, ARCH-019 | Conflict/state/local transaction tests pass. |
| 4 | Implement create, primitive push/delete, and optional proven bulk path. | ARCH-006, ARCH-008 through ARCH-014, ARCH-018, ARCH-019 | Request sequence, bulk equivalence/disablement, and partial failure tests pass. |
| 5 | Implement guarded folder organization and named-snapshot lifecycle/restore. | ARCH-020 through ARCH-028 | Folder tree/cycle/empty-delete and snapshot identity/restore/partial-failure tests pass. |
| 6 | Add the opt-in `archidekt` toolset, prove the north-star workflow, and complete disposable live proof. | ARCH-015 through ARCH-029 | Profile/mode surface, composed workflow, combined DB/HTTP, deck/folder/snapshot cleanup, and full offline gates pass. |

## Rules

- Recheck Archidekt behavior before coding because the web API is not a stable
  public specification.
- Treat contract drift as adapter maintenance; do not add compatibility aliases
  or guess replacement endpoints.
- Do not expand named snapshots into automatic activity logs/recent-change
  history, or folder organization into packages, deck tags, collaboration,
  social, or account features.
- Never pass the provider's generic item-delete operation through directly;
  stable folder deletion is empty-only and cannot submit deck items.
- Snapshot restore always uses preview/apply and never silently rewrites the
  local deck or synchronization baseline.
- Never weaken guards to make a flaky live test pass.
- Keep live tests opt-in and cleanup-first.
- Do not add aliases or a generic provider router to compensate for opt-in
  discovery.

## Rollback

Disable/unregister the adapter without changing local decks. Provider bindings
and baselines remain local evidence; users may continue with manual export.
