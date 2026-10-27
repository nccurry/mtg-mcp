# Deck Count Contracts Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core and MCP surface maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Different surfaces currently use ambiguous excluded-card and maybeboard counts. This packet defines a single partition with explicit zone buckets and publishes it additively while retaining old fields.

## Audience

Maintainers and MCP clients that consume workspace or deck summary counts.

## References

- src/MtgMcp.Core/DeckCategoryInclusion.cs
- src/MtgMcp.App/Tools/Workspaces/WorkspaceTools.cs
- src/MtgMcp.Core/Analysis/DeckAnalysisModels.cs
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| One count truth | All new surfaces return identical cardCounts for the same workspace | Core owns the calculation |
| Exact partitions | Total equals included plus excluded; excluded equals three excluded buckets | Nonpositive quantities contribute zero |
| Safe adoption | Existing clients see unchanged legacy fields | Canonical replacements are documented |

## System Overview

Deck categories determine whether a primary category belongs to the playable deck or an excluded zone. A Core function converts card quantities and category definitions into one immutable summary. App presenters reuse it without reinterpreting names.

## Scope And Non-Scope

- In scope: DeckCardCountSummary, canonical partition rules, edge cases, additive cardCounts, tests, and documentation.
- Out of scope: profile selection, role-classifier redesign, and removal or semantic changes to legacy fields.
- Compatibility target: cardCounts is additive; maybeboardCards and roleCounts retain their 0.8 behavior through the full 0.9 line.
- Explicit non-goals: changing how cards are assigned categories or defining a future legacy-removal release.

## Stakeholders And Affected Systems

Core category/count helpers, workspace start/open presenters, deck_summarize, MCP JSON schema snapshots, E2E tests, README/tool documentation, and downstream clients.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| DCC-REQ-001 | Must | Data | Core shall own immutable DeckCardCountSummary fields total, included, excluded, maybeboard, sideboard, and otherExcluded. | Prevent surface-specific math. | DCC-FIX-001 serializes the exact nonnegative shape. |
| DCC-REQ-002 | Must | Functional | The partition shall satisfy total = included + excluded and excluded = maybeboard + sideboard + otherExcluded. | Counts must be auditable. | All DCC fixture cases satisfy both invariants. |
| DCC-REQ-003 | Must | Semantics | Bucketing shall use only the primary category, case-insensitive canonical aliases, category inclusion metadata, and zero contribution from nonpositive quantities. | Secondary tags and bad quantities must not distort zones. | DCC-FIX-002 through DCC-FIX-007 pass exact counts. |
| DCC-REQ-004 | Must | Edge case | An explicitly included Sideboard category shall count as included; missing category definitions shall follow DeckCategoryInclusion; unknown excluded names shall count as otherExcluded. | Names alone do not override inclusion. | DCC-FIX-004 through DCC-FIX-006 pass. |
| DCC-REQ-005 | Must | MCP | workspace start/open and deck_summarize shall expose the same additive cardCounts object with integer fields matching DCC-REQ-001. | Clients need a stable canonical surface. | DCC-FIX-008 surface and E2E tests match Core. |
| DCC-REQ-006 | Must | Compatibility | maybeboardCards and roleCounts shall retain their current names, values, and JSON types through 0.9, and docs shall identify cardCounts as canonical for zone counts. | Avoid unrelated breaking changes. | DCC-FIX-009 snapshots old fields before and after. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Every requirement states one behavior or constraint.
- [x] Measures and invariants are explicit.
- [x] Implementation constraints reflect ownership requirements.
- [x] No unresolved items remain.

## Interfaces, Data, States, And Modes

cardCounts is an object with six non-null JSON integer properties: total, included, excluded, maybeboard, sideboard, otherExcluded. It is present in successful workspace start/open and deck_summarize responses at the existing detail levels where legacy counts are returned. Tool visibility and operation modes do not change.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Consistency | Same workspace through three surfaces | Exact field equality |
| Compatibility | Existing 0.9 client reads legacy fields | Names, types, and values unchanged |
| Determinism | Category enumeration order varies | Identical summary |
| Safety | Quantity is zero or negative | No negative output; zero contribution |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core value and invariants | DCC-REQ-001 to DCC-REQ-004 | Full category matrix passes |
| 2 | Additive MCP integration | DCC-REQ-005, DCC-REQ-006 | Three surfaces and legacy snapshots pass |
| 3 | Compatibility docs and validation | All | Surface report, lint, test, and docs pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| DCC-REQ-001 | Public schema | Core value/serialization test | DCC-FIX-001 |
| DCC-REQ-002 | Partition algorithm | Invariant theory tests | DCC-FIX-001 to DCC-FIX-007 |
| DCC-REQ-003 | Partition algorithm | Category matrix | DCC-FIX-002, DCC-FIX-003, DCC-FIX-007 |
| DCC-REQ-004 | Partition algorithm | Edge-case tests | DCC-FIX-004 to DCC-FIX-006 |
| DCC-REQ-005 | Public schema | Surface and E2E tests | DCC-FIX-008 |
| DCC-REQ-006 | Compatibility | Snapshot/docs inspection | DCC-FIX-009 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Legacy names remain misleading | Risk | Clients may keep using them | App | Document cardCounts as canonical; defer removal |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused Core/App tests, task surface:report, task lint, task test, documentation inspection, and git diff --check. No network is required.

## Definition Of Done

- [ ] Must requirements are implemented or owner-deferred.
- [ ] Acceptance evidence is recorded.
- [ ] Traceability is current.
- [ ] SADD matches implementation.
- [ ] Residual risks are recorded.
