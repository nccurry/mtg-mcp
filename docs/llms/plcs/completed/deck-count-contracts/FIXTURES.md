# Deck Count Contracts Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| DCC-FIX-001 | Core value | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Simple included/excluded invariant | Core | Keep minimal |
| DCC-FIX-002 | Category case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Case-insensitive Maybe aliases | Core | Update only with alias policy |
| DCC-FIX-003 | Category case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Secondary Maybeboard on included primary | Core | Keep primary ownership |
| DCC-FIX-004 | Category case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Explicitly included Sideboard | Core | Keep inclusion precedence |
| DCC-FIX-005 | Category case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Missing category definition | Core | Follow DeckCategoryInclusion |
| DCC-FIX-006 | Category case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Unknown excluded category | Core | Must land in otherExcluded |
| DCC-FIX-007 | Quantity case | tests/MtgMcp.Core.Tests/DeckCardCountSummaryTests.cs | Zero and negative quantities | Core | Must contribute zero |
| DCC-FIX-008 | MCP response | tests/MtgMcp.App.Tests/DeckCardCountSurfaceTests.cs | Three cardCounts outputs | App | Update with public schema only |
| DCC-FIX-009 | MCP response | tests/MtgMcp.App.Tests/DeckCardCountCompatibilityTests.cs | Legacy maybeboardCards and roleCounts | App | Freeze through 0.9 |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| DCC-REQ-001 | DCC-FIX-001 | Six exact non-null integer fields | Core serialization test |
| DCC-REQ-002 | DCC-FIX-001 to DCC-FIX-007 | Both partition equations hold | Theory tests |
| DCC-REQ-003 | DCC-FIX-002, DCC-FIX-003, DCC-FIX-007 | Case-insensitive primary-only nonnegative counts | Core tests |
| DCC-REQ-004 | DCC-FIX-004 to DCC-FIX-006 | Inclusion precedence and fallback bucket are exact | Core tests |
| DCC-REQ-005 | DCC-FIX-008 | start/open/summarize values are identical | Surface/E2E tests |
| DCC-REQ-006 | DCC-FIX-009 | Legacy fields retain names, types, and values | Compatibility snapshot |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| workspace start | Existing | Unchanged | Adds cardCounts |
| workspace open | Existing | Unchanged | Adds cardCounts |
| deck_summarize | Read-only | Visible | Adds cardCounts |

## Provider Fixtures

None. This packet has no provider contract changes.

## Calibration Or Performance Cases

None. Count partitioning is deterministic domain logic with unit and E2E coverage.
