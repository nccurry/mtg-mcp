# Archidekt Ownership Cleanup Software Requirements Document

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Purpose

Move the real Archidekt code into the types whose names describe that code. The
result must make future deck, folder, snapshot, authentication, pacing, and
route changes easy to locate without changing any public behavior.

## References

- [Parent requirements](../../planned/evidence-first-deckbuilding-evolution/SRD.md#requirements)
- [Parent Phase 1B plan](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-1b-archidekt-ownership-extraction)
- [Parent architecture](../../planned/evidence-first-deckbuilding-evolution/SADD.md#building-blocks)
- [Archidekt adapter instructions](../../../../../src/AGENTS.md)
- [Current service facade](../../../../../src/MtgMcp.Archidekt/ArchidektFacade.cs)
- [Current workflow owner](../../../../../src/MtgMcp.Archidekt/ArchidektService.cs)
- [Current transport facade](../../../../../src/MtgMcp.Archidekt/ArchidektTransportFacade.cs)
- [Current transport owner](../../../../../src/MtgMcp.Archidekt/ArchidektTransport.cs)
- [Current fake-HTTP tests](../../../../../tests/MtgMcp.Archidekt.Tests/ArchidektServiceTests.cs)

## Current State

ArchidektService exposes a stable public API. It creates three named operation
classes, but those classes only forward into ArchidektOperationContext.
ArchidektTransport does the same for three named route classes and
ArchidektTransportContext. The two Context types hold the real code.

The workflow context holds deck creation and deletion, target application,
folder reads and writes, snapshot restore, typed result conversion, request
limits, confirmations, and read-back checks. The transport context holds every
provider route as well as the HTTP client, credentials, authentication lock,
in-memory token, pacing, retries, error translation, and disposal.

## Outcomes

| Outcome | Evidence |
| --- | --- |
| A deck change has one obvious workflow and route home. | ArchidektDeckOperations and ArchidektDeckTransport contain the current deck code. |
| A folder change has one obvious workflow and route home. | ArchidektFolderOperations and ArchidektFolderTransport contain the current folder code. |
| A snapshot change has one obvious workflow and route home. | ArchidektSnapshotOperations and ArchidektSnapshotTransport contain the current snapshot code. |
| Shared provider state has one small home. | ArchidektSession owns the client, credentials, token, pacing, retries, and disposal. |
| Existing callers see no change. | Current service, fake-HTTP, architecture, surface, and coverage gates pass. |

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| AOC-001 | Must | ArchidektSession owns shared HTTP, authentication, pacing, retry, request-execution, and disposal behavior. | No deck, folder, or snapshot route exists in ArchidektSession. Offline authentication, retry, pacing, timeout, redaction, and disposal tests pass. |
| AOC-002 | Must | Each named transport owns its provider routes and response mapping. | Deck, folder, and snapshot route methods live in their named transport files. The old transport context and forwarding transport facade are gone. |
| AOC-003 | Must | Each named operation owner contains its guarded workflow behavior. | Deck, folder, and snapshot workflow methods and their helpers live in named operation files. The old operation context and forwarding operation facade are gone. |
| AOC-004 | Must | ArchidektService remains the public facade and composition root. | Its public constructors and methods retain their current signatures and outputs. It owns session disposal and delegates only to named operation owners. |
| AOC-005 | Must | Current safety and reliability behavior is unchanged. | Request budgets, confirmation phrases, fingerprints, read-back checks, auth refresh, 429 handling, safe retries, typed failures, and cancellation have current offline behavior. |
| AOC-006 | Must | The cleanup adds no public surface or provider-contract change. | MCP surface report is unchanged. No route, header, configuration, project, package, mode, or schema change appears in the diff. |
| AOC-007 | Must | Normal tests remain deterministic and offline. | New tests use the existing fake HTTP handler and configured test pacers. No new Live test is added. |
| AOC-008 | Must | The code tree stays easy to scan. | No Context or forwarding-only facade type remains. Any helper has one narrow name and a real shared use. |
| AOC-009 | Should | The stale architecture-test summary says 93 tools. | The summary and assertion agree without changing the surface test's behavior. |
| AOC-010 | Must | The child and parent documents record design, validation, and completion. | Links resolve, `git diff --check` passes, and all final evidence is recorded. |

## Non-Goals

- Add a generic provider, HTTP, repository, routing, or adapter framework.
- Change any Archidekt HTTP route, payload, header, credential source, retry,
  pacing, operation budget, confirmation, read-back, or error policy.
- Change MCP tool registration, toolsets, modes, schemas, or descriptions.
- Add a feature, source, package, configuration key, persistence format, or
  live test.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Maintainability | A developer changes a folder move. | The workflow and its provider route are in folder-named files. |
| Testability | A developer changes an authentication retry. | A fake-HTTP session test proves the request count and sanitized failure. |
| Reliability | A provider mutation has an ambiguous failure. | Existing partial-result and read-back behavior remains unchanged. |
| Safety | A caller requests a destructive remote operation. | Current confirmation, fingerprint, and request-budget guards remain exact. |
| Compatibility | An MCP client uses any current Archidekt tool. | Service output and MCP surface stay unchanged. |

## Use Cases

| ID | Trigger | Expected result |
| --- | --- | --- |
| CASE-001 | A maintainer changes a deck route. | They edit ArchidektDeckTransport and focused tests. |
| CASE-002 | A maintainer changes a folder move safety rule. | They edit ArchidektFolderOperations and focused tests. |
| CASE-003 | A maintainer changes authentication or pacing. | They edit ArchidektSession and its focused tests. |
| CASE-004 | An existing MCP client invokes a tool. | It receives the same output, error category, and operation-mode behavior. |

## Traceability

| Requirement | Design | Test evidence |
| --- | --- | --- |
| AOC-001 | [Session](SADD.md#archidekt-session) | Existing transport and pacer tests plus direct session tests. |
| AOC-002 | [Provider routes](SADD.md#provider-routes) | Direct named transport fake-HTTP tests. |
| AOC-003 | [Guarded workflows](SADD.md#guarded-workflows) | Direct named operation and existing service tests. |
| AOC-004 | [Service facade](SADD.md#service-facade) | Service construction and public behavior tests. |
| AOC-005 | [Failure handling](SADD.md#failure-handling) | Existing fake-HTTP safety and failure tests. |
| AOC-006 | [Compatibility](SADD.md#compatibility) | Surface report and source review. |
| AOC-007 | [Test design](SADD.md#test-design) | Test fixture review and non-Live Task run. |
| AOC-008 | [File ownership](SADD.md#file-ownership) | Reflection boundary test and source audit. |
| AOC-009 | [Documentation](SADD.md#documentation) | Architecture test source inspection. |
| AOC-010 | [Validation](IMPLEMENTATION_PLAN.md#phase-5-close-the-child) | Link and diff checks. |

## Risks And Controls

| Risk | Control |
| --- | --- |
| Moving a helper changes request order or retry behavior. | Characterize request counts first and move helpers with their current callers. |
| Shared state leaks into a named route class. | Keep all token, pacing, retry, and HTTP methods in ArchidektSession. |
| The service facade becomes another hidden workflow owner. | Limit it to construction, stable public forwarding, operation-scope creation, and disposal. |
| A direct test only proves forwarding. | Construct named session, transport, and operation types in new focused fake-HTTP tests. |
| A source-boundary test becomes too brittle. | Assert the retired Context types are absent and the service remains a facade; use behavior tests as the main proof. |

## Validation

Run focused Archidekt non-Live tests after each code phase. Before completion,
run `task lint`, `task test`, `task coverage`, `task surface:report`, local
Markdown-link inspection, `git diff --check`, and the bounded code audit.

## Definition Of Done

- [ ] All Must requirements have passing evidence.
- [ ] Deck, folder, and snapshot route code has a named home.
- [ ] Deck, folder, and snapshot workflow code has a named home.
- [ ] One small session owns shared HTTP and authentication state.
- [ ] Both Context types and forwarding-only facade files are gone.
- [ ] Current public behavior, MCP surface, and provider policy are unchanged.
- [ ] Normal tests are offline and deterministic.
- [ ] The stale 90-tool wording is corrected.
- [ ] The parent and child packets record final validation.
