# Archidekt Ownership Cleanup Fixtures And Acceptance Matrix

## Existing Test Assets

| ID | Asset | Location | What it proves |
| --- | --- | --- | --- |
| AOC-FIX-001 | Fake HTTP handler | tests/MtgMcp.Archidekt.Tests/ArchidektTestHttpHandler.cs | Exact queued response order, request captures, headers, and payloads. |
| AOC-FIX-002 | Service behavior tests | tests/MtgMcp.Archidekt.Tests/ArchidektServiceTests.cs | Deck, folder, snapshot, confirmation, fingerprint, read-back, and typed-result behavior. |
| AOC-FIX-003 | Transport tests | tests/MtgMcp.Archidekt.Tests/ArchidektTransportTests.cs | Authentication, retries, rate handling, route mapping, timeouts, and error redaction. |
| AOC-FIX-004 | Pacer tests | tests/MtgMcp.Archidekt.Tests/ArchidektRequestPacerTests.cs | Spacing, rolling window, cooldown, cancellation, and operation budgets. |
| AOC-FIX-005 | App coordinator tests | tests/MtgMcp.App.Tests/ArchidektCoordinatorTests.cs | Existing internal service construction and tool-facing workflows. |
| AOC-FIX-006 | Live tests | tests/MtgMcp.Archidekt.Tests/ArchidektLiveTests.cs | Opt-in provider contract checks; excluded from normal validation. |

## Direct Named-Owner Cases To Add

| Case | Construction | Expected proof |
| --- | --- | --- |
| Session authentication retry and disposal | Session plus fake HTTP | One failed authenticated request refreshes once and keeps request-budget accounting. Owned clients are disposed; borrowed clients remain usable. |
| Deck route and workflow | Session, deck transport, deck operations | Current routes, create/read-back, apply request order, and typed conflicts remain exact. |
| Folder route and workflow | Session, folder transport, folder operations | Current tree, move/cycle, confirmation, and read-back behavior remains exact. |
| Snapshot route and workflow | Session, snapshot transport, snapshot operations | Current snapshot mutation and restore guards remain exact. |
| Boundary | Assembly reflection | ArchidektOperationContext and ArchidektTransportContext are absent after the move. |

## Requirement Matrix

| Requirement | Evidence | Expected result |
| --- | --- | --- |
| AOC-001 | AOC-FIX-003, AOC-FIX-004, direct session case | Shared session behavior is unchanged and route-free. |
| AOC-002 | AOC-FIX-001, direct named transport cases | Each provider route lives in a named transport and sends the current request. |
| AOC-003 | AOC-FIX-002, direct named operation cases | Each safety workflow lives in its named operation owner. |
| AOC-004 | AOC-FIX-002, AOC-FIX-005 | ArchidektService keeps its public behavior and composes the owners. |
| AOC-005 | AOC-FIX-002 through AOC-FIX-004 | All existing guards, retries, pacing, and typed outcomes remain. |
| AOC-006 | Surface report and source review | MCP surface and provider contract do not change. |
| AOC-007 | Fixture review and non-Live test run | New tests use fake HTTP and no normal test reaches Archidekt. |
| AOC-008 | Reflection boundary test and source audit | Both Context types and forwarding-only files are gone. |
| AOC-009 | FoundationArchitectureTests source | Summary says 93 tools. |
| AOC-010 | PLC packet | Links, phases, and final evidence are current. |

## Test Rules

- Keep all normal tests deterministic and offline.
- Do not add a provider mutation to a normal test.
- Use exact request counts and request bodies when moving transport behavior.
- Assert public behavior and safety outcomes before asserting file placement.
- Mark any real provider test `Category=Live`.
