# Simulation Profile Evidence Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core simulation and MCP documentation maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

The resolver receives a canonical included-card sequence based on primary category. Evidence aggregation uses per-family card identity sets so role aliases cannot double count quantity. Built-in speculative routes are removed; explicit user intent remains descriptive. Stable sorting closes all ties.

## Goals, Non-Goals, And Design Drivers

Reuse existing helpers, preserve profile and intent formats, make evidence auditable, and keep selection deterministic. Execution behavior and profile storage are outside the design.

## Context And Scope

The catalog and resolver are Core-owned. App surfaces describe why a profile was selected. Conservative goldfish later consumes the selected profile but owns all action execution.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Tune thresholds only | Compensate for inflated counts | Minimal diff | Leaves wrong inputs and opaque routes | Rejected |
| Deduplicate globally | Count each card in only one family | Simple totals | Erases legitimate multi-signal evidence | Rejected |
| Deduplicate per family | One contribution per card per family | Exact and explainable | Requires explicit family keys | Chosen |

## Chosen Design

### Input selection

Enumerate cards with positive quantity whose primary category is included according to DeckCategoryInclusion. Secondary categories are ignored for inclusion. Normalize stable identity by snapshot/card ID with deterministic name fallback only where existing models lack an ID.

### Evidence aggregation

Each signal family owns a set of contributing card identities and a quantity total. A card matching Tokens and SacrificeFodder aliases inside the same token-density family adds its quantity once. If the same card also matches a distinct attack-trigger family, both families may add it once. Evidence rows list the family, count, threshold, contribution, and automatic-derived label in stable family order.

### Route policy

Built-in profiles have no automatic common routes. Explicit user-authored intent routes retain their input order and current serialized form, are labeled user-intent-descriptive when presented, and never become actions merely by existing.

### Selection and labels

Explicit profile selection wins and is labeled explicit-selection. Otherwise, candidates sort by descending score and then ordinal profile key. Evidence rows sort by configured family key and contributor identity. No dictionary enumeration participates in public ordering.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| Included-card selector | Primary-category input | Resolution call | Core internal | DeckCategoryInclusion | Category tests |
| Signal accumulator | Per-family deduped quantity | Resolution call | Evidence rows | Role facts | Count tests |
| Profile catalog | Built-in thresholds and metadata | Process lifetime | Profile keys/descriptions | Core models | Catalog snapshot |
| Resolver | Score, label, and tie selection | Resolution call | Selected profile result | Selector/catalog | Permutation tests |

## Runtime And Data Flow

The resolver selects included cards, evaluates all configured signal families, freezes evidence in stable order, scores profiles, and chooses the stable maximum. Explicit requests bypass auto scoring. Intent routes are copied into descriptive output after selection without changing scores.

## MCP Surface, Schemas, And Diagnostics

No request parameters or profile keys are removed. Descriptions clarify label meanings. If the existing evidence shape cannot carry the label, one additive string field evidenceKind is added with the three closed values specified by the SRD. Built-in route arrays are empty rather than null.

## Adapter And Provider Contracts

None.

## Cross-Cutting Concepts

All comparisons are ordinal and deterministic. Card quantities are nonnegative for evidence. Profile evidence is not the general source-evidence taxonomy; if presented together, this packet describes selection provenance while [mcp-trust-evidence](../mcp-trust-evidence/README.md) owns general evidence tiers.

## Project Boundaries

Core owns selection and evidence. App only serializes and documents it. No App or adapter reference enters Core.

## Readability And Documentation

Prefer named signal-family records and straightforward loops. Remove common-route constants and any secondary-category filter duplicated from DeckCategoryInclusion.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| SPE-REQ-001 | Canonical input selector | SPE-FIX-001 to SPE-FIX-003 |
| SPE-REQ-002, SPE-REQ-003 | Per-family identity sets | SPE-FIX-004 to SPE-FIX-006 |
| SPE-REQ-004, SPE-REQ-005 | Split built-in and intent route policy | SPE-FIX-007, SPE-FIX-008 |
| SPE-REQ-006 | Closed labels and stable sorting | SPE-FIX-009, SPE-FIX-010 |
| SPE-REQ-007 | App descriptions/docs | SPE-FIX-011 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core selector/accumulator/tests | SPE-REQ-001 to SPE-REQ-003 | Exact evidence matrix passes |
| 2 | Catalog/resolver/tests | SPE-REQ-004 to SPE-REQ-006 | Routes removed and permutations stable |
| 3 | App descriptions/docs/broad gates | SPE-REQ-007 | Surface/docs and task gates pass |

## Test Architecture

Minimal deck builders vary primary/secondary categories and overlapping tags. Exact-count assertions inspect families, not only the selected profile. Permutation tests shuffle cards and candidate profiles. Catalog snapshots prove no automatic route content remains. Intent tests round-trip existing JSON.

## Framework And External Notes

No external systems are involved. Profile behavior is deterministic local configuration.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Profile result may change | Decision | Corrected default selection | Document fixture deltas |
| Profile externalization | Deferred | Catalog remains compiled | Separate feature if needed |
| Route execution | Deferred | Intent stays descriptive | Goldfish effect support owns actions |

## Glossary

- Signal family: one independent class of deck evidence used for profile scoring.
- Descriptive route: user-supplied intent text that does not execute game actions.
