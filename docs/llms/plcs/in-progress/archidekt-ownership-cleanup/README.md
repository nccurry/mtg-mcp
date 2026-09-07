# Archidekt Ownership Cleanup PLC Packet

## Lifecycle

- Status: In progress
- Folder: docs/llms/plcs/in-progress/archidekt-ownership-cleanup/
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Parent phase: Phase 1B: Archidekt ownership
- Owner selection: The request to implement the PLC phase by phase on main.
- Independent design review: Passed on 2026-09-07.
- Implementation authorized: Yes

## Summary

ArchidektService already has useful deck, folder, and snapshot classes. Today,
they only forward work to two large classes with vague names:
ArchidektOperationContext and ArchidektTransportContext.

This child gives the named classes their real work. One small ArchidektSession
will own HTTP, authentication, pacing, retries, and disposal. It will charge
each provider start to the caller's existing request budget.
Deck, folder, and snapshot transports will own their provider routes. Deck,
folder, and snapshot operations will own their guarded workflows.

The public ArchidektService API, MCP tools, modes, provider routes, request
budget rules, retry policy, and typed results stay unchanged. This is a code
ownership cleanup, not a behavior change.

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance criteria.
- [SADD.md](SADD.md): selected structure, data flow, and test design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): ordered phases and exits.
- [FIXTURES.md](FIXTURES.md): current fake-HTTP behavior coverage.

## Selected Design

| Decision | Status | Why |
| --- | --- | --- |
| Create ArchidektSession as the one shared HTTP and authentication owner. | Accepted | Authentication, pacing, retry, request execution, and owned-client disposal are one shared lifetime. |
| Move route code into deck, folder, and snapshot transport files. | Accepted | Each route should live with the data it reads or writes. |
| Move guarded workflows into deck, folder, and snapshot operation files. | Accepted | Confirmation, fingerprint, read-back, restore, and folder-tree rules belong with the operation they protect. |
| Keep ArchidektService as the public facade and composition root. | Accepted | Existing callers keep their stable API while internal ownership becomes visible. |
| Remove both Context types and forwarding facade files. | Accepted | They hide the actual change boundary and make the file tree harder to scan. |
| Keep only small named shared helpers for provider IDs, deck format IDs, and result mapping. | Accepted | They remove real repeated code without creating a generic provider framework. |
| Correct the stale "ninety-tool" architecture-test summary. | Accepted | The test already checks 93 tools; the summary should say the same thing. |

## Scope

| In scope | Out of scope |
| --- | --- |
| Internal Archidekt service, session, route, and workflow ownership. | New Archidekt endpoints, tool names, schemas, or descriptions. |
| Existing fake-HTTP tests and direct named-owner tests. | Changes to operation modes, confirmations, request limits, pacing, retries, or provider semantics. |
| Removal of the two Context types and forwarding-only files. | New configuration, packages, project references, local persistence, or network tests in normal CI. |
| One stale architecture-test summary. | Archidekt account, social, or collaboration features. |

## Design Readiness

- [x] The current forwarding and large-owner problem is confirmed in source.
- [x] Public and provider behavior that must not change is listed.
- [x] Existing fake-HTTP, pacing, request-budget, and service tests were inspected.
- [x] The target file ownership and dependency direction are explicit.
- [x] Each phase has a focused behavior and boundary check.
- [x] No owner technical decision is currently needed.
- [x] Independent design review is complete.
- [x] Implementation is authorized.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Source inspection | Passed | ArchidektOperationContext owns deck, folder, snapshot, and apply workflows. ArchidektTransportContext owns every provider route plus shared HTTP state. The named classes in the two facade files forward to them. |
| 2026-09-07 | Existing test inspection | Passed | Offline tests cover fake HTTP routes, request counts, authentication retry, rate handling, confirmation, fingerprints, read-back verification, folder safety, snapshots, redaction, and typed failures. |
| 2026-09-07 | Parent baseline | Passed | The full non-live Task suite, coverage gates, and MCP surface report passed before this child was planned. |
| 2026-09-07 | Independent design review | Findings fixed | The review clarified that a session charges, but does not own, the public operation budget; added client-ownership disposal coverage; and made the temporary Phase 2 composition boundary explicit. |

## Completion Notes

Active. Phase 1 adds direct named-owner characterization before production
ownership moves begin.
