# Land Entry Classification Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| LEC-FIX-001 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Fortified Village reveal wording | Core | Preserve exact functional clauses |
| LEC-FIX-002 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Equivalent reveal-land wording | Core | Add only demonstrated variants |
| LEC-FIX-003 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Pay/discard then if-you-do-not wording | Core | Keep both condition families |
| LEC-FIX-004 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Simple always-tapped land | Core | Stable regression |
| LEC-FIX-005 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Always tapped plus unrelated condition | Core | Proves precedence |
| LEC-FIX-006 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Shock/pay-life land | Core | Stable regression |
| LEC-FIX-007 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Existing optional untapped condition | Core | Stable regression |
| LEC-FIX-008 | Oracle text | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Unconditional untapped land | Core | Negative control |
| LEC-FIX-009 | Multi-face snapshot | tests/MtgMcp.Core.Tests/Analysis/LandEntryClassifierTests.cs | Land face and nonland face text | Core | Test both face orderings |
| LEC-FIX-010 | Consumer scenario | tests/MtgMcp.Core.Tests/Analysis/LandEntryConsumerTests.cs | Shared classifier use | Core | Update when consumers move |
| LEC-FIX-011 | Documentation case | docs/stats-lab-metrics.md and docs/simulation-profiles.md | Classification and calibration note | Docs | Update with behavior text |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| LEC-REQ-001 | LEC-FIX-001 to LEC-FIX-003 | ConditionallyTapped | Core theory |
| LEC-REQ-002 | LEC-FIX-004, LEC-FIX-005 | AlwaysTapped | Negative regression |
| LEC-REQ-003 | LEC-FIX-006, LEC-FIX-007 | Conditional; LEC-FIX-008 normal | Core theory |
| LEC-REQ-004 | LEC-FIX-009 | Only land face determines class | Face tests |
| LEC-REQ-005 | LEC-FIX-010 | Both consumers reuse classifier | Inspection/focused tests |
| LEC-REQ-006 | LEC-FIX-011 | Docs state corrected scope and limits | Docs review |

## MCP Surface Checks

No MCP shape changes. Existing tools may return corrected downstream results.

## Provider Fixtures

None. Oracle text is supplied through local test data.

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| Reveal-land impact | Existing Stats Lab calibration decks containing reveal lands | Only entry-class-dependent metrics may move; change is recorded | Calibration report comparison |
