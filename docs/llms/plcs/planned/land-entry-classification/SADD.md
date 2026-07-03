# Land Entry Classification Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core, Stats Lab, and simulation maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Extend the existing Core classifier with normalized, ordered phrase families. Hard enters-tapped statements take precedence, followed by conditional entry restrictions, then normally untapped. This is simpler and safer than adding another parser or evaluating game state here.

## Goals, Non-Goals, And Design Drivers

The change must be narrow, deterministic, reusable, offline-testable, and preserve existing enum semantics. It must not become a hand evaluator or broad oracle-text engine.

## Context And Scope

Stats Lab and goldfish both need printed entry classification. Multi-face snapshots provide distinct oracle text per face. Consumer-specific action logic remains outside the classifier.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Consumer-specific fixes | Patch Stats and goldfish separately | Fast locally | Divergent semantics | Rejected |
| General rules parser | Parse all replacement effects | Extensible | Large and unnecessary | Rejected |
| Ordered phrase families | Extend shared classifier | Small and testable | Requires curated fixtures | Chosen |

## Chosen Design

Normalize case, whitespace, apostrophe variants, and line breaks without changing word order. Apply classifications in this order:

1. Explicit unconditional enters-tapped statements return AlwaysTapped.
2. Existing optional untapped and pay-life patterns return ConditionallyTapped.
3. A reveal, pay, or discard permission whose consequence clause says if you do not or if you cannot, this land enters tapped returns ConditionallyTapped.
4. Otherwise return NormallyUntapped.

Pattern recognition requires both the choice family and the entry consequence in the same relevant face text. It does not assume the condition can be satisfied.

### Face selection

If a land face is supplied, only that face text is classified. A nonland face cannot force a land face into a different class. Existing root-card fallback remains for single-face cards.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| LandEntryClassifier | Printed entry restriction | Stateless | Core enum/method | Card text only | Theory tests |
| Stats Lab caller | Use class in heuristic simulation | Simulation run | Internal | Classifier | Focused integration |
| Goldfish compiler/caller | Use class in sequencing | Compiled deck/run | Internal | Classifier | Focused integration |

## Runtime And Data Flow

Consumers select the relevant land face and call the classifier once when compiling or analyzing the deck. The result may be cached in consumer-owned immutable facts. Conditional satisfaction is evaluated only if that consumer supports it; otherwise it uses its documented conservative policy.

## MCP Surface, Schemas, And Diagnostics

No request or response shape changes. Corrected metrics and simulations retain their existing diagnostic mechanisms. Documentation describes the correctness correction.

## Adapter And Provider Contracts

None. This packet assumes oracle text is supplied by card snapshots; metadata completeness is owned by [card-snapshot-integrity](../card-snapshot-integrity/README.md).

## Cross-Cutting Concepts

All matching is ordinal and deterministic. Patterns are bounded constants with named tests. No regex backtracking risk or provider calls are introduced.

## Project Boundaries

The classifier remains in MtgMcp.Core. App and adapters do not gain parsing logic. Future goldfish implementation depends on this packet rather than copying it.

## Readability And Documentation

Keep phrase families visibly grouped with concise comments only for oracle wording quirks. Prefer readable conditionals to dense multi-line LINQ or one opaque expression.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| LEC-REQ-001 to LEC-REQ-003 | Ordered phrase precedence | LEC-FIX-001 to LEC-FIX-008 |
| LEC-REQ-004 | Explicit relevant-face input | LEC-FIX-009 |
| LEC-REQ-005 | Shared Core ownership | LEC-FIX-010 |
| LEC-REQ-006 | Calibration and behavior notes | LEC-FIX-011 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core classifier/tests | LEC-REQ-001 to LEC-REQ-004 | Exact matrix passes |
| 2 | Stats/simulation callers and tests | LEC-REQ-005 | Reuse inspection and focused tests pass |
| 3 | Docs and broad gates | LEC-REQ-006 | task lint/test and docs inspection pass |

## Test Architecture

Theory data stores exact oracle snippets and expected states. Negative cases combine unrelated choice words with tapped text to prevent broad false positives. Face tests swap ordering and include a nonland front face. Consumer tests assert the shared result rather than duplicate text expectations.

## Framework And External Notes

Oracle wording fixtures are local text excerpts used as facts, not live Scryfall dependencies.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| No hand evaluation | Decision | Conditional does not mean untapped | Consumer policy remains explicit |
| Historical wording | Risk | New phrases may appear | Add minimal failing fixture before extending |

## Glossary

- Conditional entry: printed text allows or requires a condition that changes whether the land enters tapped.
- Relevant face: the face used as a land in the current card representation.
