# Deck Count Contracts Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core and MCP surface maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

A dependency-light Core value and pure partition function become the sole source for canonical zone counts. App output adds that value directly. The design deliberately leaves legacy count and role fields untouched.

## Goals, Non-Goals, And Design Drivers

Exact invariants, category-helper reuse, deterministic output, no adapter work, and additive compatibility drive the design. Renaming fields and redefining roles are excluded.

## Context And Scope

Workspace and summary paths currently calculate excluded/maybeboard concepts differently. The new function consumes existing deck card/category models and DeckCategoryInclusion decisions.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Fix each presenter | Duplicate corrected expressions | Small local diffs | Drift remains likely | Rejected |
| Rename legacy fields | Make old names canonical | Cleaner eventual surface | Breaking and semantically unsafe | Rejected |
| Add Core summary | One calculation, additive output | Testable and compatible | Temporarily retains old fields | Chosen |

## Chosen Design

### Partition algorithm

For each card, clamp quantity contribution at zero and inspect only its primary category. Resolve inclusion with DeckCategoryInclusion. Included cards increment included. Excluded cards are classified by case-insensitive normalized aliases:

| Bucket | Aliases |
| --- | --- |
| maybeboard | Maybeboard, Maybe, Considering |
| sideboard | Sideboard |
| otherExcluded | Every other excluded or unresolved excluded name |

An explicitly included category wins even when its name is Sideboard. Secondary categories never affect the bucket. Missing definitions use the existing helper decision, then unknown excluded names fall into otherExcluded.

After accumulation, total is included plus excluded and excluded is the sum of its three child buckets. The constructor enforces nonnegative values and both invariants.

### Public schema

cardCounts has:

| Property | JSON type | Null | Meaning |
| --- | --- | --- | --- |
| total | integer | never | All positive-quantity cards |
| included | integer | never | Playable primary categories |
| excluded | integer | never | All excluded primary categories |
| maybeboard | integer | never | Excluded maybe aliases |
| sideboard | integer | never | Excluded Sideboard |
| otherExcluded | integer | never | Remaining excluded cards |

The same serialized value appears in workspace start/open and deck_summarize. maybeboardCards keeps its current all-excluded workspace meaning, and roleCounts keeps its current classifier output through 0.9.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| DeckCardCountSummary | Enforce count invariants | Response/value lifetime | Core public model | None | Value tests |
| Deck count partition | Classify primary category quantities | Call lifetime | Core method | DeckCategoryInclusion | Theory tests |
| App presenters | Attach cardCounts | MCP response | JSON fields | Core summary | Surface/E2E tests |

## Runtime And Data Flow

Each surface loads or receives a deck, calls the Core partition once, and passes the result to its response model. No cache or provider call is introduced. Invalid source quantities are ignored rather than propagated as negative counts.

## MCP Surface, Schemas, And Diagnostics

The three existing success responses gain cardCounts. Requests, operation modes, errors, and detail controls stay unchanged. No new warnings are needed because invalid quantities already remain source data; tests prove they cannot corrupt the canonical totals.

## Adapter And Provider Contracts

None. Adapters do not own category partition semantics.

## Cross-Cutting Concepts

Case normalization uses ordinal case-insensitive comparison. Enumeration order cannot affect results. The canonical value is safe to reuse later by conservative-goldfish-v2 but that packet does not block this one.

## Project Boundaries

Core owns the domain value and algorithm. App owns MCP response placement. No project references change.

## Readability And Documentation

Use a named domain value rather than dictionaries or tuples. XML comments state the two invariants. Delete presenter-local canonical count calculations once all new fields call Core.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| DCC-REQ-001, DCC-REQ-002 | Invariant-enforcing Core value | DCC-FIX-001 to DCC-FIX-007 |
| DCC-REQ-003, DCC-REQ-004 | Primary-category algorithm and alias table | Edge-case theory tests |
| DCC-REQ-005 | Shared response value | DCC-FIX-008 |
| DCC-REQ-006 | Additive surface only | DCC-FIX-009 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core models/helpers/tests | DCC-REQ-001 to DCC-REQ-004 | Partition matrix passes |
| 2 | App models/presenters/surface tests | DCC-REQ-005, DCC-REQ-006 | All response snapshots pass |
| 3 | Docs and broad gates | All | task surface:report, lint, test pass |

## Test Architecture

Table-driven Core tests cover alias casing, missing definitions, included Sideboard, secondary tags, unknown excluded names, and nonpositive quantities. App tests compare the full cardCounts object across all three surfaces and snapshot the legacy fields.

## Framework And External Notes

No external provider or framework behavior is involved.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Legacy semantics | Decision | Temporary duplicate concepts | Preserve through 0.9 and document canonical field |
| Removal schedule | Deferred | No cleanup release selected | Separate future compatibility decision |

## Glossary

- Primary category: the single category that owns a card for inclusion and zone counts.
- Other excluded: excluded cards not assigned a canonical maybe or sideboard alias.
