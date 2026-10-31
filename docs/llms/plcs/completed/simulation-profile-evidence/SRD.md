# Simulation Profile Evidence Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core simulation and MCP documentation maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Automatic profile selection can exclude included cards because of secondary categories and inflate evidence when one card carries overlapping role tags. Built-in profiles also attach generic win-route prose that looks deck-specific. This packet corrects those inference inputs without changing simulation execution.

## Audience

Maintainers and clients interpreting automatic simulation profile selection.

## References

- src/MtgMcp.Core/SimulationProfileCatalog.cs
- src/MtgMcp.Core/DeckCategoryInclusion.cs
- docs/simulation-profiles.md
- [Conservative goldfish dependency](../conservative-goldfish-v2/README.md)
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Correct input deck | Secondary excluded tags do not remove included-primary cards | Quantity counted once |
| Honest evidence | Overlapping tags do not multiply a card within one signal family | Labels state derivation |
| No invented routes | Built-in automatic profiles return no speculative common route | User intent is preserved |
| Repeatable selection | Ties resolve identically across orderings | Stable profile key order |

## System Overview

SimulationProfileCatalog defines built-in profiles and signal families. The resolver examines included deck cards and chooses a profile. Explicit user-authored intent may include descriptive routes, which remain metadata and are not automatic evidence.

## Scope And Non-Scope

- In scope: primary inclusion, signal deduplication, built-in route cleanup, intent-route preservation, evidence labels, and deterministic ties.
- Out of scope: goldfish combat/effects, profile externalization, and user intent format changes.
- Compatibility target: profile keys and explicit profile selection remain; automatic results may change as a correctness fix; removal of speculative automatic route text is documented.
- Explicit non-goals: changing profile thresholds without fixture evidence or treating routes as simulation actions.

## Stakeholders And Affected Systems

Core profile catalog/resolver, auto-profile response presenters, prompts/resources that describe profiles, simulation-profile docs, tests, and conservative-goldfish-v2 as a downstream dependency.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| SPE-REQ-001 | Must | Functional | Auto-profile input shall use DeckCategoryInclusion and primary categories only. | Secondary excluded tags are descriptive, not zone ownership. | SPE-FIX-001 to SPE-FIX-003 select the expected cards. |
| SPE-REQ-002 | Must | Semantics | Within each auto-profile signal family, each distinct card quantity shall contribute at most once regardless of overlapping roles or aliases. | Tag overlap must not create phantom copies. | SPE-FIX-004 and SPE-FIX-005 have exact evidence counts. |
| SPE-REQ-003 | Must | Semantics | Distinct signal families may each receive the same card quantity when the card independently satisfies each family. | Deduplication must not erase legitimate cross-family evidence. | SPE-FIX-006 proves separate counters. |
| SPE-REQ-004 | Must | Content | Built-in profiles shall not automatically attach speculative common win routes. | Generic route prose is not deck evidence. | SPE-FIX-007 finds no automatic route entries. |
| SPE-REQ-005 | Must | Compatibility | User-authored intent routes shall remain unchanged as descriptive metadata and shall not affect simulated actions unless separately supported. | Preserve explicit user intent without overclaiming execution. | SPE-FIX-008 round-trips and selection remains unchanged. |
| SPE-REQ-006 | Must | Determinism | Auto selection shall expose clear derived-evidence labels and use score, then stable profile key, as the deterministic tie-break order. | Results need repeatable explanation. | SPE-FIX-009 and SPE-FIX-010 are invariant to card/profile enumeration. |
| SPE-REQ-007 | Must | Documentation | MCP descriptions and simulation-profile docs shall distinguish automatic derived evidence, explicit profile selection, and user-authored descriptive routes. | Clients must interpret results correctly. | SPE-FIX-011 surface/docs inspection passes. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Requirements are atomic.
- [x] Quantities and tie ordering are explicit.
- [x] Implementation details only constrain shared ownership.
- [x] No unresolved items remain.

## Interfaces, Data, States, And Modes

Existing profile identifiers and request parameters remain. Automatic selection evidence rows retain their shape unless an additive label is required; labels use automatic-derived, explicit-selection, or user-intent-descriptive. Built-in route collections become empty. User-authored route JSON retains current schema and null rules.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Determinism | Reorder cards, categories, and catalog enumeration | Same selected key, score, and evidence ordering |
| Compatibility | Explicit profile or intent route is supplied | Existing value is preserved |
| Honesty | Automatic built-in profile selected | No speculative route is presented |
| Offline testability | Resolver suite | Local deck builders only |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Inclusion and counts | SPE-REQ-001 to SPE-REQ-003 | Category/overlap matrix passes |
| 2 | Routes and deterministic resolver | SPE-REQ-004 to SPE-REQ-006 | Route, labels, and tie tests pass |
| 3 | Surfaces and docs | SPE-REQ-007 and all | Surface report, docs, lint, test pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| SPE-REQ-001 | Input selection | Core resolver tests | SPE-FIX-001 to SPE-FIX-003 |
| SPE-REQ-002 | Evidence aggregation | Exact-count tests | SPE-FIX-004, SPE-FIX-005 |
| SPE-REQ-003 | Evidence aggregation | Cross-family test | SPE-FIX-006 |
| SPE-REQ-004 | Route policy | Catalog snapshot | SPE-FIX-007 |
| SPE-REQ-005 | Route policy | Intent round-trip/resolver test | SPE-FIX-008 |
| SPE-REQ-006 | Selection and labels | Permutation tests | SPE-FIX-009, SPE-FIX-010 |
| SPE-REQ-007 | Surface semantics | Surface/docs inspection | SPE-FIX-011 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Corrected scores change auto profile | Risk | Simulation defaults may move | Core | Snapshot old/new fixture results and document correctness change |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused resolver/catalog tests, App surface tests, task surface:report, task lint, task test, docs inspection, and git diff --check.

## Definition Of Done

- [ ] Must requirements implemented or explicitly deferred.
- [ ] Acceptance evidence recorded.
- [ ] Traceability current.
- [ ] SADD reflects implementation.
- [ ] Correctness-driven profile changes documented.
