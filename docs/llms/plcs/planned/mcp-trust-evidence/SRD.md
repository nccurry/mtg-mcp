# MCP Trust Evidence Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: mtg-mcp maintainers and implementing agents
- Last updated: 2026-06-28
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

The MCP should make trust boundaries obvious when it reports card legality,
simulation results, Commander bracket estimates, role classifications, draw
odds, recommendations, and source evidence. Users and agents should be able to
tell whether a value is a source fact, mathematically derived, parser-derived,
heuristic, model-scored, or unsupported.

These requirements belong in a durable PLC because the work crosses Core, App,
adapter fixtures, public MCP output shapes, simulation assumptions,
recommendation semantics, docs, and surface tests.

## Audience

This document is for mtg-mcp maintainers and implementation agents. Readers
should understand the repository boundaries: Core owns domain logic, App owns
MCP surfaces, and adapter projects own third-party HTTP contracts.

## References

- [README.md](README.md)
- [SADD.md](SADD.md)
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- [FIXTURES.md](FIXTURES.md)
- `src/MtgMcp.Core/Analysis/EvidenceModels.cs`
- `src/MtgMcp.Core/Analysis/DeckRoleClassifier.cs`
- `src/MtgMcp.Core/Analysis/DeckAnalysisService.RoleCounts.cs`
- `src/MtgMcp.Core/Analysis/DeckAnalysisMetrics.cs`
- `src/MtgMcp.Core/Models/Responses/DeckTuningWorkflowModels.cs`
- `src/MtgMcp.Core/Recommendations/DeckRecommendationCardFacts.cs`
- `src/MtgMcp.Core/Recommendations/DeckQueryRecommendationEngine.cs`
- `src/MtgMcp.App/Tools/Simulation/GoldfishOutputPresenter.cs`
- `src/MtgMcp.Scryfall/ScryfallTaggerCorpusSignalProvider.cs`
- `tests/MtgMcp.Calibration/CalibrationCorpusLoader.cs`
- `tests/MtgMcp.Calibration/StatsLabCalibrationRunner.cs`
- `docs/toolsets.md`
- `docs/commander-bracket-model.md`
- `docs/simulation-profiles.md`

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Missing legality is never silently treated as legal. | Query, recommendation, and replacement paths use tri-state legality semantics and shared format normalization. | Workspace legality audit behavior remains a metadata-gap warning. |
| Summary simulation output remains honest. | Default comparison summary includes the existing model label plus at least one assumption or caveat. | No per-card evidence rows are required in summary. |
| Bracket estimates match the intended 1-5 surface. | Bracket 5/cEDH can be emitted through explicit criteria and accepted by calibration tests. | This is not only a clamp change. |
| Evidence labels are consistent. | MCP JSON uses the closed evidence tier string set defined in this SRD. | Existing `SourceKind` strings remain unless a phase intentionally migrates them. |
| Role and odds provenance is explainable without bloating hot paths. | Existing role-count explanation rows evolve into structured, tiered rows at normal/full detail. | Cheap classifier calls remain available. |
| Source attribution is accurate. | Live/cached Tagger signals, local user annotations, and embedded taxonomy matches are labeled separately. | The Scryfall Tagger provider already uses source-backed `otag:` searches. |

## System Overview

The MCP provides grounded card data, deterministic analysis, recommendation
heuristics, simulation summaries, and source search outputs to external LLM
clients. The trust problem is not that every output is heuristic; it is that
some outputs currently hide the distinction between source-backed data,
mathematical derivation, parser-derived classification, and heuristic scoring.

This PLC plans compatibility-safe changes that preserve normal offline tests,
avoid full rules-engine scope, and keep public MCP shape changes additive unless
a label is actively misleading.

## Scope And Non-Scope

- In scope: tri-state legality consolidation, summary caveat preservation,
  Commander bracket 1-5 correction, canonical evidence tiers, lazy role/odds
  provenance, score transparency, hardened Tagger evidence semantics, and small
  bracket/scoring profile externalization.
- Out of scope: full Magic rules engine, broad role-rule configuration
  framework, Comprehensive Rules ingestion, EDHREC integration, MTGJSON or
  other price-provider integration, and discussion/forum evidence.
- Compatibility target: existing MCP clients, JSON output compatibility,
  normal offline tests, Core/App/adapter project boundaries, and existing docs.
- Explicit non-goals: live network dependency in normal tests, Archidekt
  mutations, new provider dependencies for Phase 1, or eager provenance on all
  classifier calls.

## Stakeholders And Affected Systems

Stakeholders include MCP users, LLM agents consuming MCP output, mtg-mcp
maintainers, and future implementation agents. Affected systems include Core
analysis and recommendation services, App presenters and surface tests,
Scryfall fixture-backed adapter behavior, Commander bracket docs and
calibration, and development workflows driven by `Taskfile.yml`.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| REQ-001 | Must | Functional | The software shall represent card legality as `legal`, `not_legal`, or `unknown` through one shared Core helper that also normalizes format aliases. | Missing legality currently has inconsistent meanings across recommendation/query paths, and duplicate helpers do not normalize formats consistently. | Duplicate legality checks are removed or routed through the shared helper; tests cover all three states and format aliases such as `commander`/`edh`. |
| REQ-002 | Must | Functional | The software shall never silently treat `unknown` legality as `legal`, and each recommendation/query path shall follow the Phase 1 policy matrix. | Missing metadata must not become hidden permission to recommend illegal cards. | Reason-returning paths show unknown legality; silent filters exclude only `not_legal` and keep unknown cards with explicit warning, refresh note, or named confidence/score penalty. |
| REQ-003 | Must | Functional | Summary comparison output shall preserve the no-opponent/no-full-rules-engine assumption or caveat alongside the existing model label. | Summary is the default detail; comparison summaries already expose model labels but can omit the key assumption note. | Default `deck_compare_goldfish` summary test verifies the model label remains and an assumption/caveat string is present. |
| REQ-004 | Must | Functional | Commander bracket estimates shall support a 1-5 output range with explicit bracket-5/cEDH criteria and updated calibration validators. | A clamp change alone cannot produce correct bracket 5 behavior because signal caps and calibration guards also enforce 1-4. | Bracket tool output, signal criteria, both calibration validators, calibration corpus data, and docs accept 1-5 and include bracket-5 fixtures. |
| REQ-005 | Must | Interface | Evidence tiers shall use the closed Core-owned wire values `source_fact`, `source_evidence`, `derived_math`, `parser_derived`, `heuristic_inference`, `model_score`, and `unsupported`. | A shared vocabulary prevents every surface from inventing trust terms. | Evidence tier serialization tests cover every canonical value; tests document that `Deterministic=true` can coexist with `heuristic_inference`; existing `SourceKind` strings are reconciled or deliberately left distinct. |
| REQ-006 | Must | Functional | Role, tag, category, and counting provenance shall be represented per assignment or evidence row, and implementation shall not add one top-level source field to `CardRoleAssignment`. | One card classification can combine user, workspace, Tagger, parser, and heuristic sources. | Existing `DeckRoleCountExplanation`/`MatchingEvidence` behavior is evolved into structured tiered rows or adjacent rows; tests show mixed evidence for one card without misleading top-level provenance. |
| REQ-007 | Must | Performance | Existing cheap role classification shall remain available for hot paths. | Eager evidence construction risks performance regression and token bloat. | Hot-path callers can still use the cheap predicate/classifier; evidence is opt-in. |
| REQ-008 | Must | Interface | Evidence detail shall be gated by MCP detail level. | Summary output must stay compact while normal/full can expose audit detail. | Surface tests verify summary labels/caveats and normal/full evidence rows where applicable. |
| REQ-009 | Should | Functional | Recommendation and score outputs should label blended scores as model-derived and separate them from source facts. | A single score can otherwise look like direct evidence. | Recommendation tests verify score kind, evidence tier, and confidence meaning fields. |
| REQ-010 | Should | Provider | Tagger evidence labels should distinguish source-backed Scryfall Tagger signals, cached source-backed signals, user/local annotations, and embedded taxonomy matches. | The Scryfall provider already uses `otag:` searches; the remaining risk is Core labeling that makes local annotations look source-backed. | Offline tests prove only source-returned or source-cached cards get Tagger-backed labels; local annotation and embedded taxonomy rows get distinct source labels. |
| REQ-011 | Should | Configuration | Bracket/scoring profiles should reuse the existing simulation profile resolver pattern before any broader role-rule profile work. | Smaller profile scope avoids a premature configuration framework and avoids duplicating `SimulationProfileCatalog` behavior. | Resolver tests cover host default, explicit deck intent, built-in fallback, missing explicit profile behavior, and whether this is an extension of or a small sibling to the simulation profile resolver. |
| REQ-012 | Must | Testability | Normal tests shall remain offline and shall not mutate real Archidekt decks. | Repo safety requires deterministic local validation. | Fixture-backed tests use fake HTTP or local data; live tests remain opt-in. |

## Requirement Quality Checklist

- [x] Every Must requirement has acceptance criteria.
- [x] Every requirement states one behavior or constraint.
- [x] Requirements avoid vague phrases unless paired with measurable criteria.
- [x] Implementation details appear only when they are true constraints.
- [x] TBD/TBR items include owner, reason, and resolution plan.

## Interfaces, Data, States, And Modes

Expected MCP-facing changes are additive output fields, updated annotations or
descriptions, and detail-level behavior. Summary detail carries labels,
assumptions, and caveats. Normal/full detail may carry per-card evidence rows,
success sets, and score components.

Evidence tier wire values are closed for this PLC:

| Wire value | Meaning | Example |
| --- | --- | --- |
| `source_fact` | Direct fact from a declared source, including local workspace/user facts when source label identifies them. | Scryfall legality value or workspace category. |
| `source_evidence` | Provider-backed evidence that supports a claim but is not itself the final claim. | A card returned by a Scryfall Tagger `otag:` query. |
| `derived_math` | Deterministic calculation from known inputs. | Hypergeometric draw odds. |
| `parser_derived` | Deterministic extraction from card text or metadata. | Oracle text snippet used as role evidence. |
| `heuristic_inference` | Repeatable rule-based inference that is not source fact. | Fallback role classifier branch. |
| `model_score` | Blended or weighted result from source and heuristic components. | Recommendation score or bracket density score. |
| `unsupported` | Value unavailable because the source, theme, format, or provider path is unsupported. | Unsupported EDHREC theme or disabled provider. |

Phase 1 legality policy:

| Path | Unknown legality policy | Notes |
| --- | --- | --- |
| Shared helper | Return `unknown`; never coerce to legal. | Normalize format aliases before lookup. |
| `DeckQueryRecommendationEngine` / `deck_query_cards` | Reject from accepted recommendations with a visible `unknown legality` reason. | This path already reports rejection reasons. |
| `DeckReplacementService` candidate scoring | Keep with a named legality penalty and note; exclude `not_legal`. | Replaces the current unlabeled soft penalty. |
| Corpus recommendation/search paths | Keep with warning or confidence penalty; exclude `not_legal`. | Avoid silently deleting cards from incomplete snapshots. |
| Playgroup/meta scoring paths | Keep with reduced score/confidence and a note; exclude or fail only `not_legal`. | Unknown is metadata uncertainty, not legality proof. |
| Wincon payoff, commander-meta, and new-card swap paths | Keep unknown candidates only when otherwise relevant, with warning or score penalty; exclude `not_legal`. | Prefer aggregate notes when row-level warnings are not available. |
| Workspace legality audit | No behavior change. | Missing legality already reports a metadata gap. |

No new mutating tools or operation-mode changes are required. If public tool,
resource, or prompt descriptions change, App surface tests must be updated.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Compatibility | Existing MCP clients read outputs after new fields are added. | Existing required fields remain unless explicitly relabeled for correctness. |
| Determinism | Simulation, odds, role explanation, and calibration tests run locally. | Normal tests pass offline with stable fixtures. |
| Performance | Role classification remains a hot-path operation. | Evidence construction is opt-in and detail-gated. |
| Diagnostics | Unknown legality or heuristic scoring affects output. | Users see warnings, caveats, score meanings, or evidence tiers. |
| Maintainability | Future agents implement phases separately. | Each phase has focused validation and exit criteria. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| Phase 1 | Consolidate tri-state legality. | REQ-001, REQ-002, REQ-012 | Shared helper used by recommendation/query paths; tests cover legal, not legal, and unknown. |
| Phase 2 | Preserve summary caveats. | REQ-003, REQ-008 | Default summary keeps the existing model label and adds assumption caveat text without large evidence rows. |
| Phase 3 | Correct Commander bracket 1-5 behavior. | REQ-004 | Bracket 5 criteria, duplicated calibration guards, corpus data, and docs pass without adding Phase 4 evidence fields. |
| Phase 4 | Add minimal evidence tier vocabulary. | REQ-005 | Canonical string serialization is tested. |
| Phase 5 | Add lazy role/odds provenance. | REQ-006, REQ-007, REQ-008 | Existing role-count explanation rows are structured/tiered, and detail-gated odds success sets pass tests. |
| Phase 6 | Label recommendation scores. | REQ-009 | Score outputs distinguish model score from source evidence. |
| Phase 7 | Harden Tagger evidence semantics. | REQ-010, REQ-012 | Offline fixtures prove provider, cache, local annotation, and embedded taxonomy attribution stay distinct. |
| Phase 8 | Externalize small bracket/scoring profiles. | REQ-011 | Resolver behavior, reuse of the simulation profile pattern, and missing-profile handling are tested. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| REQ-001 | Shared legality result | Unit tests | Core legality helper tests |
| REQ-002 | Legality call-site policy | Unit/integration tests | Query/recommendation/replacement tests |
| REQ-003 | Presenter detail policy | App tests | Default summary output test |
| REQ-004 | Bracket model correction | Unit/calibration tests | Bracket 1-5 fixture cases |
| REQ-005 | Evidence tier vocabulary | Unit/surface tests | Serialization and MCP surface tests |
| REQ-006 | Per-assignment provenance | Unit tests | Mixed-source role explanation test |
| REQ-007 | Lazy classifier design | Perf inspection/tests | Existing hot-path behavior remains cheap |
| REQ-008 | Detail-level gating | Surface tests | Summary versus normal/full matrix |
| REQ-009 | Score transparency | Unit/surface tests | Recommendation score metadata tests |
| REQ-010 | Tagger attribution | Fixture-backed adapter tests | Fake Scryfall `otag:`/`is:` responses |
| REQ-011 | Profile resolver | Unit tests | Explicit, host default, fallback, missing id cases |
| REQ-012 | Offline safety | Task workflow | `task test` remains network-free |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Unknown legality behavior differs by path. | Risk | A hard global reject can silently drop cards from candidate pools. | mtg-mcp implementer | Inventory call sites and apply visible reason, warning, refresh, or confidence penalty intentionally. |
| Bracket 5 criteria are heuristic. | Risk | cEDH detection may be under- or over-sensitive. | mtg-mcp implementer | Add calibration fixtures and document caveats. |
| Evidence rows can bloat responses. | Risk | Agent loops may consume more tokens than intended. | mtg-mcp implementer | Keep rows normal/full only; summary carries compact labels. |
| Existing `Deterministic` overlaps with evidence tiers. | Assumption | Ambiguous semantics could persist. | mtg-mcp implementer | Define `Deterministic` as repeatability, not source trust. |

## Validation

Implementation branches must run narrow tests for touched areas first, then
`task test`. Branches that change public MCP shape must also run `task lint` and
update surface tests. Docs-only changes should run `git diff --check`.

## Definition Of Done

- [ ] Must requirements are implemented or explicitly deferred by the owner.
- [ ] Acceptance criteria are satisfied with objective evidence.
- [ ] Traceability and validation notes are current.
- [ ] SADD reflects the implemented design.
- [ ] Remaining risks and follow-up work are recorded.
