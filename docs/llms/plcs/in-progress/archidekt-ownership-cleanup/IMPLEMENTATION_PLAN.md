# Archidekt Ownership Cleanup Implementation Plan

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-1b-archidekt-ownership-extraction)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)
- Implementation authorized: Yes

## Strategy

First lock the behavior at the actual named types. Then move shared HTTP work,
deck work, and folder/snapshot work in separate commits. Every code phase keeps
ArchidektService stable and has no provider or MCP behavior change.

The work stays on main because the user selected direct integration. The route,
session, and workflow code has shared safety rules, so a parallel split would
create avoidable merge risk.

## Phase Summary

| Phase | Goal | Requirements | Areas | Validation | Exit |
| --- | --- | --- | --- | --- | --- |
| 1 | Characterize named-owner behavior. | AOC-005, AOC-007, AOC-008 | Archidekt tests | Focused non-Live tests | Tests prove current behavior before physical moves. |
| 2 | Move shared session and provider routes. | AOC-001, AOC-002, AOC-005 to AOC-008 | Session, transports, tests | Focused transport/session tests | Context transport code is gone; named transports contain routes. |
| 3 | Move deck workflows. | AOC-003 to AOC-008 | Deck operations, service, tests | Deck and service tests | Deck workflow code leaves the operation context. |
| 4 | Move folder and snapshot workflows. | AOC-003 to AOC-008 | Folder/snapshot operations, service, tests | Folder/snapshot and service tests | Operation context and both forwarding facade files are gone. |
| 5 | Close the child. | AOC-001 to AOC-010 | Tests and PLC docs | Task gates and code audit | The combined diff has passing evidence and no unresolved P1/P2 audit finding. |

## Phase 1: Characterize Named-Owner Behavior

- Add direct fake-HTTP tests that construct ArchidektSession plus the relevant
  named transport and operation owner.
- Preserve existing service tests as public-behavior coverage.
- Add a narrow ownership test plan: after the move it will reject both Context
  types and the stale forwarding files.
- Record exact behavior for request count, authentication refresh, rate limit,
  retry, owned-versus-borrowed client disposal, confirmation, fingerprint,
  read-back, and typed failure cases.
- Exit: focused non-Live tests pass against the forwarding baseline.

## Phase 2: Move Shared Session And Provider Routes

- Create ArchidektSession and move common client, credentials, token, login,
  pacing, retries, request sending, HTTP failure mapping, JSON parsing, and
  disposal code into it without changing behavior.
- Create named deck, folder, and snapshot transport files and move their route
  code, response mapping, and route-only helpers.
- Create ArchidektProviderId only for the current shared provider-ID conversion.
- Move deck format conversion to the deck transport.
- Remove ArchidektTransportContext and ArchidektTransportFacade.cs.
- During this transition, ArchidektOperationContext may directly hold the
  session and named transports until later phases remove it. Do not add another
  transport wrapper or leave a route method on the context.
- Update test construction to use the session and named transports directly.
- Exit: no provider route remains in a shared type; focused tests preserve the
  current request sequence and failures.

## Phase 3: Move Deck Workflows

- Move deck listing/get/create/delete/apply workflows, payload helpers,
  category/card mutations, request bounds, and deck-list verification into
  ArchidektDeckOperations.
- Move shared typed-result conversion into ArchidektOperationResults only if it
  removes the existing repeated boundary mapping without adding policy.
- Keep ArchidektService's public deck methods unchanged.
- Exit: deck code no longer lives in ArchidektOperationContext; focused deck
  and service tests pass.

## Phase 4: Move Folder And Snapshot Workflows

- Move folder tree, enrichment, create/update/move/delete, parent/cycle checks,
  and read-back behavior into ArchidektFolderOperations.
- Move snapshot list/get/create/update/delete, restore preview/apply, and
  restore helpers into ArchidektSnapshotOperations.
- Move ArchidektService into ArchidektService.cs as the small public facade and
  composition root.
- Remove ArchidektOperationContext, ArchidektFacade.cs, and any forwarding-only
  wrapper left by the transition.
- Correct the stale architecture-test summary that says ninety tools.
- Exit: named operations contain their real workflows and both Context types
  are absent.

## Phase 5: Close The Child

- Run the focused Archidekt non-Live project.
- Run `task lint`, `task test`, `task coverage`, and `task surface:report`.
- Run `git diff --check` and local Markdown-link inspection.
- Run the bounded code audit. Fix P1/P2 findings; fix cheap P3 findings or
  record them with a reason and follow-up.
- Update the parent PLC with the completed evidence, then move this packet to
  completed.
- Exit: all Must requirements are verified, coverage gates pass, the surface is
  unchanged, and the aggregate audit passes.

## Risks

| Risk | Phase | Control |
| --- | --- | --- |
| A code move changes request order. | 2 to 4 | Direct fake-HTTP request-count tests before and after each move. |
| A session helper grows routes. | 2 | Boundary test and narrow session API. |
| A workflow loses a fingerprint or confirmation guard. | 3 and 4 | Keep existing service tests and add direct named-operation tests. |
| Shared error mapping becomes a generic framework. | 3 | Keep it Archidekt-only and limited to current exception-to-result conversion. |
| A documentation update implies a public change. | 5 | Compare the surface report and document the no-change result. |

## Completion Criteria

- [ ] Every Must requirement has passing evidence.
- [ ] ArchidektSession is the only shared HTTP/authentication owner.
- [ ] Named transports contain provider routes.
- [ ] Named operations contain guarded workflows.
- [ ] Both Context types and forwarding-only facade files are gone.
- [ ] No public contract or provider behavior changed.
- [ ] Normal tests remain offline.
- [ ] The parent and child PLC records are current.
