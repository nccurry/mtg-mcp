# Rewrite Stabilization And 0.9.0 Cutover Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-12
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Entry Gate

Do not begin this plan until the eleven prerequisite child PLCs are approved,
implemented, accepted, and completed. Reopen the owning child instead of fixing
capability behavior opportunistically in this cutover.

## Phases

| Phase | Goal | Requirements | Exit criteria | Status |
| --- | --- | --- | --- | --- |
| 1 | Confirm child completion and final architecture. | CUT-001, CUT-002, CUT-014 | Dependency/revision ledger and module tests pass. | Completed |
| 2 | Reconcile toolsets, the approved public surface, package metadata, and documentation. | CUT-003 through CUT-005, CUT-013, CUT-016, CUT-020, CUT-021 | Derived profile/mode manifests, capability snapshots, forbidden scans, docs review, and preview version checks pass. | Completed for preview |
| 3 | Run complete offline, security, data-isolation, and coverage gates. | CUT-006 through CUT-008, CUT-012 | Final-commit offline evidence bundle passes. | Completed locally and remotely |
| 4 | Run provider-specific opt-in live acceptance. | CUT-009 through CUT-011 | Required live proof and cleanup gates pass with redacted evidence. | Completed with reviewed dispositions |
| 5 | Integrate latest `main` and repeat phases 1 through 4. | CUT-015 | Conflict review and complete post-integration validation pass. | Completed by fast-forward merge and final validation |
| 6 | Build, install, smoke, and approve stable artifacts. | CUT-007, CUT-013, CUT-016 | Cross-platform packaged-server and documentation gates pass. | Completed by local and remote release pipelines |
| 7 | Rehearse rollback and close release/PLC records. | CUT-017, CUT-018 | Rollback proof, approval record, release notes, and lifecycle updates pass. | Completed |

## Execution Rules

- Use supported `task --list` commands and record the exact commands selected.
- Generate every final artifact from the same accepted commit.
- Keep ordinary tests offline; never make provider availability a unit-test
  prerequisite.
- Reopen a child PLC for contract or capability changes; this packet cannot
  silently amend another child.
- Update the child matrix, regenerate the cutover crosswalk/totals, and update
  schema snapshots in one reviewed change whenever a public tool changes. Do
  not preserve a tool or count solely for backward compatibility.
- Prove the default end-to-end deckbuilding evidence workflow and every opt-in
  provider workflow; a count-only surface check cannot satisfy CUT-021.
- Record provider proof as passed, approved skip, unsupported, or failed; use
  only the waiver classes allowed by the SADD and never relabel a skip as pass.
- Do not waive Archidekt verified deletion, secret exposure, residual remote
  state, an incomplete child, or a failed rollback rehearsal.
- Publishing, tagging, pushing, merging to `main`, and changing PLC lifecycle
  state require the appropriate explicit repository-owner/release authority.

## Rollback Procedure

1. Stop the `0.9.0` host and preserve its versioned data directory unchanged.
2. Reinstall the recorded prior stable package from the release artifact store.
3. Restore the prior configuration selector without copying new databases.
4. Start the prior server and run its packaged smoke check.
5. Record the trigger, versions, commands, results, and follow-up defect.

A rollback failure blocks stable release. The procedure never deletes either
data root and never attempts a backward schema migration.
