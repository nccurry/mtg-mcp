# Card Snapshot Integrity Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Core, adapter, and MCP surface maintainers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

Analyses currently infer readiness from populated values and cannot reliably distinguish unknown data from authoritative empty data. This PLC adds persisted field-group coverage, fixes narrow adapter omissions, and makes import hydration failure-safe. It is a prerequisite for analyses that must decline unsupported facts rather than guess.

## Audience

Maintainers of workspace persistence, provider adapters, metadata refresh, and analysis consumers.

## References

- src/MtgMcp.Core/Models/CardModels.cs
- src/MtgMcp.Core/Workspaces/DeckServiceHelpers.cs
- src/MtgMcp.Archidekt/ArchidektGateway.Mapping.cs
- src/MtgMcp.Moxfield/MoxfieldGateway.Mapping.cs
- src/MtgMcp.Scryfall
- [Jasmine repair roadmap](../../../plans/jasmine-analysis-repair-roadmap.md)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Reliable readiness | Known-empty values do not look missing and unknown values do not look complete | Root and faces are evaluated independently |
| Durable imports | A successful provider import remains openable when enrichment fails | Sanitized warnings explain degradation |
| Safe refresh | Unsupported scopes return validation errors | No implicit all-card refresh |

## System Overview

Provider adapters translate remote card payloads into Core snapshots. Workspaces persist snapshots and later clone, fingerprint, refresh, and analyze them. Coverage accompanies the values through that entire lifecycle and is upgraded conservatively from old JSON.

## Scope And Non-Scope

- In scope: root and face coverage, schema migration, narrow Archidekt and Moxfield gaps, Scryfall coverage, analysis-needed selection, import-before-hydration persistence, hydration outcomes, cloning, summaries, and fingerprints.
- Out of scope: goldfish behavior, card-count partitions, general evidence provenance, and broad provider mapper rewrites.
- Compatibility target: old workspaces deserialize; new coverage is additive; missing and stale refresh scopes remain accepted; missing is not silently redefined.
- Explicit non-goals: proving arbitrary rules text support or changing provider authentication and retry policies.

## Stakeholders And Affected Systems

Core persistence and analysis helpers, Archidekt/Moxfield/Scryfall adapters, workspace repository implementations, deck import and refresh tools, surface tests, workspace documentation, and offline provider fixtures.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| CSI-REQ-001 | Must | Data | Snapshots shall persist schema-versioned known/unknown coverage for rules data, mana production, printed combat stats, and colors at root and face level. | Empty and unknown are different states. | CSI-FIX-001 through CSI-FIX-003 round-trip with exact states. |
| CSI-REQ-002 | Must | Compatibility | Old snapshots that omit coverage shall load as unknown and shall upgrade to schema version 2 on the next save without inventing known facts; future unsupported versions shall fail clearly. | Migration must be conservative. | CSI-FIX-001 and CSI-FIX-004 pass migration and rejection tests. |
| CSI-REQ-003 | Must | Adapter | Archidekt mapping shall capture produced mana, root colors, and direct nested power/toughness when valid, and set coverage only from authoritative JSON shapes. | These are the demonstrated mapping gaps. | CSI-FIX-005 covers populated, empty, malformed, and multi-face payloads. |
| CSI-REQ-004 | Must | Adapter | Moxfield shall map colors, produced mana, and coverage, while Scryfall shall set coverage for every field group it owns. | All hydration paths need the same semantics. | CSI-FIX-006 and CSI-FIX-007 prove provider ownership. |
| CSI-REQ-005 | Must | Functional | Analysis readiness shall evaluate included cards and faces by required field groups, treating known-empty as complete where legitimate and dynamic combat values as known but unsupported. | Value-presence checks are insufficient. | CSI-FIX-002, CSI-FIX-003, and CSI-FIX-008 produce expected readiness rows. |
| CSI-REQ-006 | Must | MCP | deck_refresh_card_metadata shall accept analysis-needed, preserve needed/stale/missing semantics, target included primary categories, and reject unknown scopes. | Unknown scopes currently risk refreshing all cards. | CSI-FIX-009 validates selection and App errors. |
| CSI-REQ-007 | Must | Reliability | Provider imports shall persist the raw imported workspace before best-effort hydration; enrichment failure shall not erase import success, while cancellation shall propagate. | Durable provider work must survive optional enrichment. | CSI-FIX-010 and CSI-FIX-011 validate order and cancellation. |
| CSI-REQ-008 | Must | Diagnostics | Partial, missing, and failed hydration shall update coverage accurately and persist bounded, redacted warnings without secrets or raw provider errors. | Degradation must be visible and safe. | CSI-FIX-012 validates mixed batches and redaction. |
| CSI-REQ-009 | Must | Consistency | Snapshot cloning, quality summaries, and fingerprints shall include coverage and schema version deterministically. | Copies and caches must not lose trust state. | CSI-FIX-013 validates clone equality and fingerprint changes. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Every requirement states one behavior or constraint.
- [x] Requirements avoid vague phrases unless paired with measurable criteria.
- [x] Implementation details appear only when they are true constraints.
- [x] No unresolved planning placeholders remain.

## Interfaces, Data, States, And Modes

Coverage state is known or unknown per field group. A known group may contain an authoritative empty value; malformed or wrong-kind JSON is unknown. Rules covers type line, layout, mana cost/value, oracle text, keywords, and face completeness. Colors requires valid Colors and ColorIdentity evidence independently. Printed combat stats may be known while a dynamic value such as star remains simulation-unsupported. The read-only refresh surface adds analysis-needed and returns a validation error for any unrecognized scope.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Compatibility | Load v1 JSON and save it | Values preserved; v2 coverage remains unknown unless provider evidence exists |
| Safety | Hydration error includes credentials or remote details | Persisted and returned warning is redacted |
| Determinism | Clone/fingerprint identical snapshots repeatedly | Byte-equivalent serialized state and stable fingerprint |
| Offline testability | Normal test suite exercises providers | All cases use checked-in fixtures or fake HTTP |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Coverage model and persistence | CSI-REQ-001, CSI-REQ-002, CSI-REQ-009 | Old/new round trips, clone, and fingerprint tests pass |
| 2 | Provider mapping | CSI-REQ-003, CSI-REQ-004, CSI-REQ-005 | Offline provider and readiness fixtures pass |
| 3 | Import and refresh flow | CSI-REQ-006, CSI-REQ-007, CSI-REQ-008 | Failure, cancellation, selection, and redaction tests pass |
| 4 | Public validation | All | App surface inventory, docs, lint, and offline tests pass |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| CSI-REQ-001 | Coverage and schema model | Core serialization tests | CSI-FIX-001 to CSI-FIX-003 |
| CSI-REQ-002 | Migration states | Compatibility tests | CSI-FIX-001, CSI-FIX-004 |
| CSI-REQ-003 | Provider contracts | Archidekt fixture tests | CSI-FIX-005 |
| CSI-REQ-004 | Provider contracts | Moxfield/Scryfall fixture tests | CSI-FIX-006, CSI-FIX-007 |
| CSI-REQ-005 | Readiness semantics | Core analysis tests | CSI-FIX-002, CSI-FIX-003, CSI-FIX-008 |
| CSI-REQ-006 | Refresh contract | App surface and selection tests | CSI-FIX-009 |
| CSI-REQ-007 | Import and hydration flow | Repository/fake-provider tests | CSI-FIX-010, CSI-FIX-011 |
| CSI-REQ-008 | Import and hydration flow | Redaction and batch tests | CSI-FIX-012 |
| CSI-REQ-009 | Cross-cutting consistency | Clone/fingerprint tests | CSI-FIX-013 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Provider payload shapes drift | Risk | Coverage could become unknown | Adapter owners | Keep sanitized fixtures and reject malformed shapes conservatively |
| Open questions | Question | None | mtg-mcp | None |

## Validation

Run focused Core and adapter tests, App surface tests, task lint, task test, git diff --check, and documentation link inspection. Live provider checks remain opt-in and read-only.

## Definition Of Done

- [ ] Must requirements are implemented or explicitly deferred by the owner.
- [ ] Acceptance criteria are satisfied with objective evidence.
- [ ] Traceability and validation notes are current.
- [ ] SADD reflects the implemented design.
- [ ] Remaining risks and follow-up work are recorded.
