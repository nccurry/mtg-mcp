# MCP Capability Toolsets Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are startup toolset configuration, exact App registration, capability
reporting, mode intersection, surface tests, and migration of existing deck
tools. Runtime switching, semantic tool search, individual-tool configuration,
new provider behavior, and changing operation-mode authority are out of scope.

## Requirements

| ID | Priority | Type | Requirement | Acceptance criteria |
| --- | --- | --- | --- | --- |
| TSET-001 | Must | Configuration | The host shall accept omitted/default, `all`, `none`, or an explicit comma-separated implemented-toolset list through JSON, environment, and both CLI forms using existing precedence. | Configuration matrix returns one canonical selection for every valid source and sanitized failures for invalid input. |
| TSET-002 | Must | Validation | Names shall be exact lowercase identifiers; blanks, duplicates, unknown names, and reserved/explicit mixtures shall fail before transport. | Unit and process fixtures cover every rejected form without stdout or secret/path leakage. |
| TSET-003 | Must | Architecture | Every MCP tool shall belong to exactly one App-owned toolset descriptor; Core shall contain no MCP or toolset type. | Architecture scan rejects unassigned, multiply assigned, Core-owned, and assembly-scanned tools. |
| TSET-004 | Must | Visibility | Visible tools shall equal implemented tools intersected with selected toolsets and active operation-mode permission. | Official-client matrices reconcile names and counts for every selection/mode pair. |
| TSET-005 | Must | Safety | Registration filtering shall not replace invocation-time `OperationModeGuard` enforcement for writes. | Direct wrapper tests reject unauthorized calls even when constructed outside normal registration. |
| TSET-006 | Must | Defaults | `decks`, `scryfall`, and `stats` descriptors shall be default-enabled when implemented; `archidekt`, `playgroup`, and `tagger` shall require explicit selection. | Descriptor tests and final default-profile manifest match the approved registry. |
| TSET-007 | Must | Profiles | `default` shall expand to implemented default-enabled descriptors, `all` to all implemented stable descriptors, and `none` to zero tools; experimental capabilities shall never enter `default` or `all` implicitly. | Profile fixtures and forbidden-experimental scans pass. |
| TSET-008 | Must | Stability | Selection shall remain static for a session and the server shall not advertise tool `listChanged`. | Initialization and repeated discovery are byte-equivalent; selection change requires process restart. |
| TSET-009 | Must | Capability schema | Capability schema version 2 shall replace `modules` with ordered `toolsets` containing selection, the relevance/authority boundary, and implemented rows with name, availability, stability, enabled, default-enabled, visible tool count, and description. | Resource schema/order snapshots reconcile exactly with `tools/list` and expose no placeholder or secret. |
| TSET-010 | Must | Current migration | All local deck and interchange tools shall belong to `decks`; after catalog consolidation the current surface shall be 7/23/23 for default or all and 0/0/0 for none. | Source, official-client, and installed-package discovery tests pass. |
| TSET-011 | Must | Determinism | Descriptor, tool, capability-row, and diagnostic ordering shall be canonical and independent of caller list order. | Permuted explicit lists produce identical discovery and capability JSON. |
| TSET-012 | Must | Usability | Toolset descriptions and capability output shall let an LLM distinguish enabled relevance from mode authority and provider availability. | Schema review and one representative default-to-all workflow pass. |
| TSET-013 | Must | Simplicity | The implementation shall not add a generic action router, per-tool allowlist, runtime tool mutation, assembly discovery, or placeholder provider registration. | Architecture and forbidden-marker tests pass. |
| TSET-014 | Must | Quality | Normal tests shall remain offline and deterministic with at least 90 percent line coverage per production assembly. | Full repository gates pass. |

## Interfaces, States, And Modes

Selection states are `default`, `all`, `none`, or `explicit`. Toolset
availability is `available` or `unavailable`; enablement is independent.
Operation modes remain `read-only`, `local`, and `remote` and retain their
existing authority semantics.

Capability schema 2 adds:

```json
"toolsets": {
  "selection": "default",
  "authorityBoundary": "Toolsets control relevance; operation mode controls authority.",
  "items": [
    {
      "name": "decks",
      "status": "available",
      "stability": "stable",
      "enabled": true,
      "defaultEnabled": true,
      "visibleToolCount": 23,
      "description": "Local deck storage and manual interchange. Toolset selection controls relevance; operation mode separately controls local writes."
    }
  ]
}
```

Only implemented rows appear. Missing credentials may make an enabled provider
row unavailable, but do not silently enable or disable its toolset.
Experimental descriptors, if introduced by a separately approved PLC, remain
explicit-only and are labeled `experimental` in this projection.

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Model usability | Default discovery contains only implemented default-enabled toolsets. |
| Completeness | `all` exposes every implemented stable tool permitted by the mode. |
| Safety | Toolset selection grants no authority and direct guards remain effective. |
| Determinism | Same build/config/mode yields identical ordered discovery and capability JSON. |
| Simplicity | One static registry, no router, no dynamic list, no per-tool policy language. |

## Traceability

| Requirements | Design and validation |
| --- | --- |
| TSET-001, TSET-002 | SADD configuration flow; TSET-FIX-001 through 006 |
| TSET-003 through TSET-005, TSET-013 | SADD registry/composition; architecture and direct-guard tests |
| TSET-006 through TSET-008, TSET-010, TSET-011 | SADD profile resolution; complete discovery matrix |
| TSET-009, TSET-012 | Capability schema snapshots and representative workflow |
| TSET-014 | Lint, tests, coverage, package, and smoke gates |

## Definition Of Done

- [x] All current tools have one tested assignment.
- [x] Default/all/none and explicit lists pass in all modes.
- [x] Capability schema and documentation match runtime behavior.
- [x] No dynamic surface or generic router enters the stable design.
- [x] Full offline, coverage, package, and installed-tool gates pass.
