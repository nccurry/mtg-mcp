# Card Snapshot Integrity Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core, adapter, and MCP surface maintainers
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Core gains a small immutable coverage value attached to root and face snapshots. Adapters set only the groups their authoritative payloads prove. Persistence upgrades old documents conservatively, and import orchestration commits raw provider data before optional hydration. The rejected alternative is continuing to infer trust from null or empty values.

## Goals, Non-Goals, And Design Drivers

The design must preserve Core-to-adapter dependency direction, old workspace readability, deterministic fingerprints, cancellation, secret safety, and offline tests. It does not interpret card effects or redesign provider DTOs beyond the known gaps.

## Context And Scope

Archidekt and Moxfield create Core snapshots; Scryfall enriches them. Workspace services persist and refresh those values, and analysis services inspect readiness. The public change is additive except that invalid refresh scopes become explicit errors.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Infer coverage from values | Treat null/empty as missing | No schema change | Cannot represent known-empty or partial support | Rejected |
| One snapshot-level complete flag | Mark an entire card complete | Small shape | Loses field and face precision | Rejected |
| Field-group coverage | Known/unknown by group on root and faces | Conservative and testable | Requires migration and clone updates | Chosen |

## Chosen Design

### Coverage and schema model

CardMetadataCoverage contains four independently known groups: Rules, ManaProduction, PrintedCombatStats, and Colors. CardSnapshot and CardFaceSnapshot each carry coverage. Provenance schema version 2 marks documents written with this shape.

The persisted additive shape is exact:

| Location/property | JSON type | Omitted/null policy |
| --- | --- | --- |
| snapshot.metadataCoverage | object | Omitted in v1 loads as all unknown; version 2 writes a non-null object |
| face.metadataCoverage | object | Same policy independently for every face |
| rules, manaProduction, printedCombatStats, colors | string enum known or unknown | Missing/null/unknown values load as unknown; other strings are rejected |
| provenance.schemaVersion | integer | Omitted means version 1; writer emits 2; values above 2 are rejected |

Known means the provider supplied a valid authoritative representation, including a valid empty array/string where the provider contract permits emptiness. Null, wrong JSON kinds, invalid colors, incomplete required face collections, and absent properties remain unknown. Dynamic printed stats are known metadata but receive an analysis diagnostic when a consumer requires a numeric value.

Migration states are:

| Input | Load state | Next save |
| --- | --- | --- |
| No version or version 1 | Values retained; all coverage unknown | Write version 2 without promoting groups |
| Version 2 | Preserve values and coverage | Write version 2 |
| Future version | Reject with bounded compatibility error | No write |

### Provider field ownership

| Group | Archidekt evidence | Moxfield evidence | Scryfall evidence |
| --- | --- | --- | --- |
| Rules | Valid nested/root rules fields and complete face shape | Valid board/card rules fields and complete face shape | Card or face rules fields from Scryfall card contract |
| ManaProduction | Valid produced-mana property, including empty list | Valid produced-mana property, including empty list | produced_mana property |
| PrintedCombatStats | Valid direct or nested power/toughness strings | Valid root/face power/toughness strings | power/toughness properties |
| Colors | Valid root Colors and ColorIdentity, or corresponding face fields | Valid colors and color identity | colors and color_identity |

Adapters never mark a group known merely because deserialization produced a default value.

### Readiness

Readiness selects cards with positive quantity in included primary categories. It evaluates each required root/face group and reports missing metadata separately from known-but-unsupported values. Cards moved from excluded to included become analysis-needed without requiring re-import.

### Import and hydration flow

1. Fetch and map provider deck.
2. Persist the raw workspace atomically.
3. Attempt Scryfall hydration for analysis-needed cards.
4. Merge successful items and their coverage.
5. Persist the enriched workspace and bounded redacted warnings.
6. On non-cancellation failure, retain step 2 and return degraded success.
7. On cancellation, propagate cancellation and do not convert it to a warning.

Partial batches merge only matching successful results; missing responses keep existing values and unknown coverage. No credentials, request headers, raw URLs with secrets, or provider bodies enter persisted warnings.

### Refresh contract

The accepted scopes are missing, needed, stale, all, and analysis-needed. The existing scopes retain their current meanings. analysis-needed uses readiness against included primary categories. Parsing is closed and case-insensitive; an unknown value returns an App validation error before network or repository mutation.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| CardMetadataCoverage | Field-group trust state | Snapshot lifetime | Core model | None | Serialization/value tests |
| Snapshot upgrader | Version transition | Load/save operation | Workspace persistence | Core models | Round-trip tests |
| Provider mappers | Valid payload evidence | Mapping call | Adapter-internal | Provider DTOs, Core | Offline fixtures |
| Metadata readiness evaluator | Select analysis-needed cards | Request | Core service | Inclusion helper | Unit tests |
| Import hydrator | Save and enrich ordering | Import operation | Service workflow | Repository, Scryfall abstraction | Fake integration tests |

## Runtime And Data Flow

Cloning copies coverage recursively. Fingerprinting serializes stable group flags and schema version in the same canonical order as other snapshot fields. Quality summaries count cards/faces by complete, partial, and unknown groups and never collapse known-empty into unknown.

## MCP Surface, Schemas, And Diagnostics

deck_refresh_card_metadata adds scope value analysis-needed. Existing request and response fields remain. Invalid scopes return the existing structured validation failure and perform no refresh. Workspace quality output adds coverage counts without removing old fields. Tool visibility and operation-mode annotations remain unchanged.

## Adapter And Provider Contracts

Adapters continue to own third-party DTOs, HTTP behavior, auth, retry, pacing, and cache rules. Only sanitized checked-in payloads are used by normal tests. Live tests are Category=Live, read-only, and never required for completion.

## Cross-Cutting Concepts

All collections and warning rows use deterministic ordering. CancellationToken flows through async calls with ConfigureAwait(false). Warning count limits include an omitted count. Coverage is data trust, not the general evidence vocabulary owned by [mcp-trust-evidence](../mcp-trust-evidence/README.md).

## Project Boundaries

MtgMcp.Core defines models and workflows through provider-neutral abstractions. Adapter projects reference Core and map external contracts. MtgMcp.App owns MCP validation and presentation. No new reverse reference is allowed.

## Readability And Documentation

Use specific names, immutable records or values, exhaustive switches, and XML summaries for new declarations. Reuse DeckCategoryInclusion and current redaction patterns. Remove obsolete presence-only readiness helpers when all callers migrate.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| CSI-REQ-001, CSI-REQ-002 | Explicit groups and version table | CSI-FIX-001 to CSI-FIX-004 |
| CSI-REQ-003, CSI-REQ-004 | Provider-owned evidence mapping | CSI-FIX-005 to CSI-FIX-007 |
| CSI-REQ-005, CSI-REQ-006 | Closed readiness and scope semantics | CSI-FIX-008, CSI-FIX-009 |
| CSI-REQ-007, CSI-REQ-008 | Two-commit import flow and redaction | CSI-FIX-010 to CSI-FIX-012 |
| CSI-REQ-009 | Recursive copy and canonical fingerprint | CSI-FIX-013 |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core models, persistence, helpers | CSI-REQ-001, CSI-REQ-002, CSI-REQ-009 | Round-trip and deterministic copy tests pass |
| 2 | Three provider adapters, readiness | CSI-REQ-003 to CSI-REQ-005 | Offline mapping matrix passes |
| 3 | Workspace service and App refresh tool | CSI-REQ-006 to CSI-REQ-008 | Failure and public surface tests pass |
| 4 | Docs and broad validation | All | task lint and task test pass |

## Test Architecture

Core tests cover state combinations, multi-face cards, dynamic stats, clones, and fingerprints. Adapter tests deserialize sanitized JSON fixtures. Service tests use an in-memory repository and fake hydrator for success, partial, failure, and cancellation. App tests inspect schemas and unknown-scope behavior.

## Framework And External Notes

Archidekt, Moxfield, and Scryfall payload assumptions are frozen only in adapter fixtures. Provider changes that invalidate a shape should produce unknown coverage until the adapter and fixture are deliberately updated.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Coverage is field-group, not per property | Decision | Keeps persisted shape bounded | Add a group only for a demonstrated consumer need |
| General evidence vocabulary | Deferred | No source attribution tiers here | Owned by mcp-trust-evidence |
| Provider drift | Risk | More analysis-needed cards | Conservative unknown state and fixture updates |

## Glossary

- Known-empty: an authoritative valid representation containing no values.
- Unknown: the provider did not prove the field group.
- Analysis-needed: an included card missing metadata required by supported analysis.
