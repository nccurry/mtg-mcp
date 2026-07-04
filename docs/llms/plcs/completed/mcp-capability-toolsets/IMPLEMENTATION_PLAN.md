# MCP Capability Toolsets Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Implement the shared App contract before any provider child. Keep each phase
green and preserve current deck behavior while changing only what is visible
under an explicit selection.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | Add parsing and canonical selection. | TSET-001, TSET-002, TSET-006, TSET-007, TSET-011 | App configuration/CLI | Focused unit tests | Completed |
| 2 | Add explicit descriptor registry and deck assignment. | TSET-003 through TSET-005, TSET-010, TSET-013 | App hosting/deck composition | App and architecture tests | Completed |
| 3 | Project capability schema version 2. | TSET-008, TSET-009, TSET-012 | Capability resource/contracts | Resource snapshots | Completed |
| 4 | Complete profile/mode and installed-package validation. | All | E2E, tasks, docs | Full repository and package gates | Completed |

## Phase Details

### Phase 1: Configuration

- Extend the shared loader rather than adding a second configuration system.
- Accept both CLI forms and existing JSON/environment precedence.
- Reject duplicate keys and invalid lists before transport.
- Add no tool registration yet.

### Phase 2: Registration

- Replace hand-maintained host counts with descriptor-derived composition.
- Assign every current tool to `decks` exactly once.
- Preserve invocation-time operation-mode guards.
- Add no provider placeholders or assembly scanning.

### Phase 3: Capability Projection

- Replace `modules` with the schema-version-2 `toolsets` projection.
- Keep property and collection order deterministic and path/secret free.
- Reconcile visible counts with actual official-client discovery.

### Phase 4: Closure

- Exercise default, all, none, explicit, and invalid selection in every mode.
- Update README, compatibility, architecture, rewrite guidance, `llms.txt`,
  changelog, smoke tasks, and cutover planning baselines.
- Run lint, tests, surface report, coverage, package, process smoke, MCP smoke,
  installed-tool smoke, dependency scans, and applicable audits.

## Completion Criteria

- [x] Every Must requirement appears in a phase.
- [x] Existing deck workflows remain behaviorally unchanged when `decks` is enabled.
- [x] All current tools have exactly one assignment.
- [x] Default/all/none discovery and capability output reconcile in all modes.
- [x] No later provider child was implemented before this packet was approved and complete.
