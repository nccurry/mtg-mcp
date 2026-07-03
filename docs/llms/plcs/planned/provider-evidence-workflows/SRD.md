# Provider Evidence Workflows Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related design: [SADD.md](SADD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-07-03 | mtg-mcp | Initial foundation follow-up draft. |

## Context And Outcomes

Provider observations are valuable only when their origin and limits remain
visible. LLMs should receive attributable facts and evidence with enough
metadata to judge freshness and population, while adapters isolate unstable
wire contracts and Core remains deterministic and testable.

## References

- [North star](../../../../north-star.md)
- [Design goals](../../../../design-goals.md)
- [Adapter architecture](../../../../adapters.md)
- [MCP trust evidence PLC](../mcp-trust-evidence/README.md)
- [Output control](../../../../output-control.md)

## Scope And Non-Scope

In scope are source facts, attributed source evidence, retrieval and cache
metadata, freshness, population/sample metadata, permission sensitivity,
provider-specific failure states, normalized Core models, and safe Archidekt
workflows.

Out of scope are HTML scraping, browser automation, bulk crawling, undocumented
field claims, blending unlike source populations into one fact, live network
requirements for normal tests, and heuristic scores presented as observations.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | An LLM asks why a card is popular. | Rows identify provider, population, retrieval time, cache state, and available sample counts. |
| CASE-002 | A source is stale or unavailable. | The response exposes stale/unavailable state without invented replacement values. |
| CASE-003 | A player analyzes a Playgroup. | Raw games and rankings remain separate from any local-meta heuristic score. |
| CASE-004 | A player applies an Archidekt edit. | Apply mode, sanitization, checkpoints, and fixture-tested adapter behavior guard the change. |

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| PEW-001 | Must | Scryfall and workspace fields shall be identified as source facts when directly observed. | Fixtures preserve source field attribution without heuristic labels. |
| PEW-002 | Must | Tagger, EDHREC, tournament, and Playgroup observations shall be source evidence, not universal truth. | Public descriptions and output metadata use evidence vocabulary. |
| PEW-003 | Must | Evidence shall carry source, retrieval time, cache state, freshness state, and permission sensitivity. | Contract tests verify required metadata for every configured provider. |
| PEW-004 | Must | Population and sample metadata shall be carried when the provider exposes it; absence shall remain unknown. | Missing counts serialize as unsupported/unknown, never zero by guess. |
| PEW-005 | Must | Unlike source populations shall remain distinct through Core and MCP presentation. | EDHREC and tournament rows are not summed or averaged without an explicit model output. |
| PEW-006 | Must | Provider wire contracts and HTTP behavior shall remain adapter-owned. | Architecture tests prevent provider DTOs and clients from entering Core. |
| PEW-007 | Must | Core shall receive normalized evidence models only. | Core tests construct evidence without adapter assemblies. |
| PEW-008 | Must | Playgroup observations shall remain distinct from heuristic local-meta scoring. | Schemas and tests expose separate fields/types and provenance. |
| PEW-009 | Must | Archidekt mutations shall remain apply-only, guarded, sanitized, checkpoint-aware, and fixture-tested. | Surface, guard, redaction, and fake-HTTP tests cover every write path. |
| PEW-010 | Must | Implementations shall not scrape HTML, automate browsers, crawl in bulk, or claim undocumented semantics. | Design review and adapter tests show supported endpoints/fixtures only. |
| PEW-011 | Must | Source failures shall be partial, typed, bounded, and secret-safe. | One provider failure does not fabricate data or leak credentials. |
| PEW-012 | Should | Source-backed output shall be deterministic, cache-aware, and bounded. | Stable fixtures produce stable ordering and response limits. |

## Interfaces, Data, States, And Modes

Normalized evidence should distinguish available, stale, unavailable,
permission-restricted, unsupported, and unknown states. Read-only evidence is
available in all operation modes. Planning artifacts follow plan-mode rules.
Archidekt writes require apply mode; aliases must not bypass guards.

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Attribution | Every evidence row has a stable source key and retrieval context. |
| Honesty | Missing provider fields remain unknown; no generated fallback is labeled factual. |
| Isolation | Core has no adapter references or provider DTOs. |
| Offline safety | Normal tests use fake HTTP, fixtures, temporary paths, and in-memory stores. |
| Secret safety | Errors, config, logs, fixtures, and MCP output pass redaction tests. |

## Traceability And Validation

| Requirement group | Design section | Evidence |
| --- | --- | --- |
| PEW-001–005 | Normalized evidence | Core model and presenter fixtures |
| PEW-006–007 | Ownership boundaries | Architecture and project-reference tests |
| PEW-008 | Playgroup separation | Adapter, Core, and App contract tests |
| PEW-009 | Archidekt safety | Guard, fake-HTTP, checkpoint, and redaction tests |
| PEW-010–012 | Provider behavior | Design inspection, pacing/cache tests, surface tests |

## Definition Of Done

- [ ] Provider-specific Must requirements have fixture evidence.
- [ ] Core remains adapter-independent and dependency-light.
- [ ] No normal test uses network access or real Archidekt mutation.
- [ ] Source limitations and permission sensitivity are documented.
- [ ] Lint, test, coverage, surface, and MCP smoke gates pass.
