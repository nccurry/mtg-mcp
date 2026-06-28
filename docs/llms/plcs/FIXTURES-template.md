# <Feature Name> Fixtures And Acceptance Matrix

Use this document when implementation needs stable examples, provider payloads,
MCP surface inventories, calibration cases, or manual acceptance scenarios.
Delete it from a PLC packet when there are no durable fixtures.

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| FIX-001 | <HTTP payload/workspace/decklist/MCP response/calibration case> | <path or link> | <What this proves> | <Owner> | <When to update> |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| REQ-001 | FIX-001 | <Expected behavior> | <Test, command, or inspection> |

## MCP Surface Checks

Record expected tool/resource/prompt names, annotations, operation-mode
visibility, and detail-level behavior when this PLC changes the public MCP
surface.

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| <tool/resource/prompt> | <read-only/plan/apply> | <visible/hidden> | <Notes> |

## Provider Fixtures

Record provider payloads, auth-free fixture captures, cache entries, and
sanitized error examples when this PLC changes adapter behavior.

| Provider | Fixture | Scenario | Sanitization notes |
| --- | --- | --- | --- |
| <Provider> | <Fixture ID/path> | <Scenario> | <Secret or PII handling> |

## Calibration Or Performance Cases

Record deterministic seeds, deck/workspace inputs, expected metric ranges, and
benchmark smoke checks when this PLC changes Stats Lab, simulation, or hot-path
logic.

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| <Case> | <Deck/workspace/profile/seed> | <Expected range or invariant> | <Test or benchmark> |
