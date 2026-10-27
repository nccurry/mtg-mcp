# Card Snapshot Integrity Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| CSI-FIX-001 | Workspace JSON | tests/MtgMcp.Core.Tests/Fixtures/CardSnapshotIntegrity/v1-workspace.json | Old snapshot migration | Core | Change only with migration contract |
| CSI-FIX-002 | Workspace JSON | tests/MtgMcp.Core.Tests/Fixtures/CardSnapshotIntegrity/v2-known-empty.json | Legitimate empty rules/mana values | Core | Change only with coverage schema |
| CSI-FIX-003 | Workspace JSON | tests/MtgMcp.Core.Tests/Fixtures/CardSnapshotIntegrity/multiface-partial.json | Root/face partial coverage | Core | Change only with face semantics |
| CSI-FIX-004 | Workspace JSON | tests/MtgMcp.Core.Tests/Fixtures/CardSnapshotIntegrity/future-version.json | Future schema rejection | Core | Keep version above supported maximum |
| CSI-FIX-005 | HTTP payload | tests/MtgMcp.Archidekt.Tests/Fixtures/card-metadata-coverage.json | Archidekt narrow mapping gaps | Archidekt | Refresh from sanitized read-only response |
| CSI-FIX-006 | HTTP payload | tests/MtgMcp.Moxfield.Tests/Fixtures/card-metadata-coverage.json | Moxfield colors/mana/coverage | Moxfield | Refresh from sanitized read-only response |
| CSI-FIX-007 | HTTP payload | tests/MtgMcp.Scryfall.Tests/Fixtures/card-metadata-coverage.json | Scryfall group ownership | Scryfall | Refresh from public sanitized data |
| CSI-FIX-008 | Deck snapshot | tests/MtgMcp.Core.Tests/CardMetadataReadinessTests.cs | Dynamic stats and excluded-to-included readiness | Core | Keep minimal |
| CSI-FIX-009 | MCP request matrix | tests/MtgMcp.App.Tests/CardMetadataRefreshSurfaceTests.cs | Refresh scopes and no-work errors | App | Update with public schema |
| CSI-FIX-010 | Service scenario | tests/MtgMcp.Core.Tests/CardMetadataHydrationTests.cs | Raw save before failed hydration | Core | Keep ordered call assertions |
| CSI-FIX-011 | Service scenario | tests/MtgMcp.Core.Tests/CardMetadataHydrationTests.cs | Cancellation propagation | Core | Keep deterministic cancellation point |
| CSI-FIX-012 | Service scenario | tests/MtgMcp.Core.Tests/CardMetadataHydrationTests.cs | Partial/missing/failed hydration and redaction | Core | Include omitted warning count |
| CSI-FIX-013 | Snapshot matrix | tests/MtgMcp.Core.Tests/CardSnapshotIntegrityTests.cs | Clone, quality, and fingerprint behavior | Core | Update only with fingerprint version |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| CSI-REQ-001 | CSI-FIX-001 to CSI-FIX-003 | Exact root/face group states round-trip | Core serialization tests |
| CSI-REQ-002 | CSI-FIX-001, CSI-FIX-004 | Conservative upgrade; clear future-version rejection | Compatibility tests |
| CSI-REQ-003 | CSI-FIX-005 | Valid fields map; malformed shapes stay unknown | Archidekt mapping tests |
| CSI-REQ-004 | CSI-FIX-006, CSI-FIX-007 | Provider-owned coverage is exact | Adapter mapping tests |
| CSI-REQ-005 | CSI-FIX-002, CSI-FIX-003, CSI-FIX-008 | Known-empty and unsupported remain distinct | Readiness tests |
| CSI-REQ-006 | CSI-FIX-009 | Closed scopes, primary inclusion, no network on error | App tests |
| CSI-REQ-007 | CSI-FIX-010, CSI-FIX-011 | Raw import persists; cancellation propagates | Service integration tests |
| CSI-REQ-008 | CSI-FIX-012 | Accurate mixed states and bounded redacted warnings | Service tests |
| CSI-REQ-009 | CSI-FIX-013 | Coverage survives copy and affects summaries/fingerprint | Core tests |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| deck_refresh_card_metadata | Existing mode policy | Unchanged | Adds analysis-needed; rejects unknown scopes |
| workspace start/open quality output | Read-only | Visible | Additive coverage summaries only |

## Provider Fixtures

| Provider | Fixture | Scenario | Sanitization notes |
| --- | --- | --- | --- |
| Archidekt | CSI-FIX-005 | Nested produced mana, root colors, direct nested stats, faces | Remove user identifiers and authorization |
| Moxfield | CSI-FIX-006 | Root/face colors and produced mana | Public deck payload only |
| Scryfall | CSI-FIX-007 | Known-empty and populated groups | Public card data; no secrets |

## Calibration Or Performance Cases

None. This packet changes metadata integrity, not calibrated metrics or hot-path performance.
