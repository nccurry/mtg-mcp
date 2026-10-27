# Simulation Profile Evidence Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| SPE-FIX-001 | Deck builder | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Included primary plus excluded secondary | Core | Preserve primary ownership |
| SPE-FIX-002 | Deck builder | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Excluded primary plus included secondary | Core | Preserve primary ownership |
| SPE-FIX-003 | Deck builder | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Missing category definition | Core | Follow DeckCategoryInclusion |
| SPE-FIX-004 | Role matrix | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Tokens plus SacrificeFodder same family | Core | Exact one contribution |
| SPE-FIX-005 | Role matrix | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Alias overlap within a family | Core | Exact one contribution |
| SPE-FIX-006 | Role matrix | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Same card in two distinct families | Core | One contribution per family |
| SPE-FIX-007 | Catalog snapshot | tests/MtgMcp.Core.Tests/SimulationProfileCatalogTests.cs | No built-in speculative routes | Core | Update only with accepted route policy |
| SPE-FIX-008 | User intent JSON | tests/MtgMcp.Core.Tests/Fixtures/SimulationProfiles/user-intent-routes.json | Descriptive route round-trip/non-scoring | Core | Preserve current format |
| SPE-FIX-009 | Resolver matrix | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Exact evidence labels | Core | Update with public label contract |
| SPE-FIX-010 | Permutation scenario | tests/MtgMcp.Core.Tests/SimulationProfileEvidenceTests.cs | Deterministic profile tie | Core | Shuffle cards and candidates |
| SPE-FIX-011 | Surface/docs snapshot | tests/MtgMcp.App.Tests/SimulationProfileSurfaceTests.cs | Evidence and route descriptions | App | Update with public copy |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| SPE-REQ-001 | SPE-FIX-001 to SPE-FIX-003 | Only included primary cards contribute | Resolver tests |
| SPE-REQ-002 | SPE-FIX-004, SPE-FIX-005 | Quantity counted once per family | Exact-count tests |
| SPE-REQ-003 | SPE-FIX-006 | Quantity appears once in each qualifying family | Exact-count test |
| SPE-REQ-004 | SPE-FIX-007 | Built-in route collections are empty | Catalog snapshot |
| SPE-REQ-005 | SPE-FIX-008 | Intent route unchanged and non-scoring | Round-trip/resolver tests |
| SPE-REQ-006 | SPE-FIX-009, SPE-FIX-010 | Labels exact; tie uses ordinal key | Permutation tests |
| SPE-REQ-007 | SPE-FIX-011 | Public copy distinguishes three provenance kinds | Surface/docs inspection |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| Profile-bearing simulation tools | Existing | Unchanged | Clarified selection evidence descriptions |
| Profile resources/prompts | Read-only | Visible | Built-in routes no longer described as deck facts |

## Provider Fixtures

None.

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| Auto-profile correction | SPE-FIX-001 through SPE-FIX-006 | Exact selected profile and evidence counts | Before/after resolver report |
