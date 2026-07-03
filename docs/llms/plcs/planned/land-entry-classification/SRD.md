# Land Entry Classification Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core, Stats Lab, and simulation maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

The classifier misses common conditional wording such as Fortified Village: a player may reveal a card, and if they do not, the land enters tapped. This PLC fixes that narrow classification defect and proves all consumers use the same result.

## Audience

Maintainers of mana analysis, Stats Lab, and goldfish simulation.

## References

- src/MtgMcp.Core/Analysis/LandEntryClassifier.cs
- docs/stats-lab-metrics.md
- [Conservative goldfish dependency](../conservative-goldfish-v2/README.md)
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Reveal lands are not overcounted | Fortified Village returns conditional | Equivalent wording matches |
| Existing classes stay stable | Always-tapped, shock, and untapped cases retain expected states | Regression matrix is explicit |
| Consumers agree | Stats Lab and goldfish classification adapters return the same enum | No local text parser |

## System Overview

LandEntryClassifier converts oracle text, including a selected land face, into AlwaysTapped, ConditionallyTapped, or NormallyUntapped. Consumers may later decide whether a condition is satisfied; this classifier only describes the printed entry restriction.

## Scope And Non-Scope

- In scope: reveal/pay/discard followed by if-you-do-not enters-tapped wording, precedence, multi-face text, tests, consumer verification, and docs.
- Out of scope: evaluating a hand to satisfy conditions and a general Magic rules parser.
- Compatibility target: no MCP schema change; corrected classification may alter analysis results and is documented as a correctness fix.
- Explicit non-goals: modeling replacement-effect layers or every historical wording.

## Stakeholders And Affected Systems

Core analysis helpers, Stats Lab simulations/calibration, goldfish mana sequencing, unit tests, and metrics/simulation documentation.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| LEC-REQ-001 | Must | Functional | LandEntryClassifier shall classify reveal, pay, or discard choices followed by an if-you-do-not enters-tapped consequence as ConditionallyTapped. | Current text matching misses this family. | LEC-FIX-001 to LEC-FIX-003 return conditional. |
| LEC-REQ-002 | Must | Regression | Explicit unconditional enters-tapped text shall remain AlwaysTapped, including text with unrelated conditional clauses. | Conditional matching must not weaken hard restrictions. | LEC-FIX-004 and LEC-FIX-005 return always tapped. |
| LEC-REQ-003 | Must | Regression | Pay-life/shock and other optional untapped conditions shall remain ConditionallyTapped, while unconditional untapped lands remain NormallyUntapped. | Preserve existing distinctions. | LEC-FIX-006 to LEC-FIX-008 pass. |
| LEC-REQ-004 | Must | Data | Multi-face cards shall classify from the land face oracle text without contamination from nonland faces. | Face text is the relevant contract. | LEC-FIX-009 passes both face orders. |
| LEC-REQ-005 | Must | Consistency | Stats Lab and goldfish consumers shall call LandEntryClassifier rather than implement parallel text detection. | Shared semantics prevent drift. | LEC-FIX-010 architecture/consumer tests pass. |
| LEC-REQ-006 | Must | Documentation | Calibration and behavior docs shall identify the correction and affected consumers without promising hand-condition evaluation. | Metric changes need explanation. | LEC-FIX-011 documentation inspection passes. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Requirements are atomic.
- [x] Classification states are measurable.
- [x] Pattern implementation is constrained only by shared ownership.
- [x] No unresolved items remain.

## Interfaces, Data, States, And Modes

The existing classifier result retains three states: AlwaysTapped, ConditionallyTapped, and NormallyUntapped. No serialized schema or operation mode changes. Null or empty relevant text follows existing normally-untapped behavior unless other card metadata proves otherwise.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Precision | Matrix of positive and negative phrases | Every fixture has exact expected enum |
| Maintainability | New consumer needs entry class | It calls the shared classifier |
| Determinism | Same normalized text under casing/line breaks | Same result |
| Offline testability | All verification | No network or live decks |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Correct classifier | LEC-REQ-001 to LEC-REQ-004 | Full text matrix passes |
| 2 | Verify consumers/calibration impact | LEC-REQ-005 | No duplicate parser and focused consumer tests pass |
| 3 | Docs and broad validation | LEC-REQ-006 and all | Docs, lint, and tests pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| LEC-REQ-001 | Ordered classification | Unit theory | LEC-FIX-001 to LEC-FIX-003 |
| LEC-REQ-002 | Ordered classification | Negative regression | LEC-FIX-004, LEC-FIX-005 |
| LEC-REQ-003 | Ordered classification | Regression matrix | LEC-FIX-006 to LEC-FIX-008 |
| LEC-REQ-004 | Face selection | Multi-face tests | LEC-FIX-009 |
| LEC-REQ-005 | Consumer integration | Architecture/focused tests | LEC-FIX-010 |
| LEC-REQ-006 | Documentation | Docs inspection | LEC-FIX-011 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Unseen historical wording | Risk | Some lands remain misclassified | Core | Add exact fixture only when demonstrated |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused classifier and consumer tests, task lint, task test, docs inspection, and git diff --check.

## Definition Of Done

- [ ] Must requirements are implemented or explicitly deferred.
- [ ] Acceptance evidence is recorded.
- [ ] Traceability is current.
- [ ] SADD matches implementation.
- [ ] Residual wording risks are recorded.
