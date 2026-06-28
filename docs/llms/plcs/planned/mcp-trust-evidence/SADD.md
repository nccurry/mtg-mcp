# MCP Trust Evidence Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: mtg-mcp maintainers and implementing agents
- Last updated: 2026-06-28
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

The chosen design fixes correctness bugs first, then adds a small Core-owned
trust vocabulary and lazy explanation paths. The highest-value decision is to
avoid eager, cross-cutting provenance on hot-path models. Instead, cheap
classification and scoring remain available, while existing user-visible
explanation rows evolve into structured tiered rows at normal/full detail.

The main constraint is MCP compatibility: existing clients should keep working
while new fields make trust boundaries clearer. The most important rejected
alternative is a broad evidence rewrite that attaches one provenance object to
every classification or score whether or not the caller displays it.

## Goals, Non-Goals, And Design Drivers

Goals:

- Make legality, caveats, bracket estimates, role matches, odds success sets,
  and recommendation scores honest about trust level.
- Keep Phase 1 correctness fixes independent from taxonomy or source-provider
  work.
- Preserve Core/App/adapter boundaries.
- Keep normal tests offline and deterministic.
- Keep summary output compact.

Non-goals:

- Full Magic rules engine.
- Broad role-rule configuration framework in early phases.
- New live provider dependency for normal tests.
- Comprehensive Rules, EDHREC, price-provider, or discussion evidence ingestion
  in the first implementation slice.

## Context And Scope

MCP clients call App tools and receive JSON summaries, normal responses, or full
responses. App presenters currently decide how much detail to include. Core owns
domain classification, odds, simulation metrics, recommendation scoring, and
source evidence models. Adapter projects own third-party HTTP contracts such as
Scryfall and Archidekt.

This design covers planned changes to Core models/services, App presenter
output, surface tests, docs, fixture-backed provider tests, and calibration
cases. It does not cover mutating tools or workspace persistence migrations.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Eager provenance on `CardRoleAssignment` | Add source/evidence lists to every classification result. | Simple to discover at call sites. | Hot-path cost, response bloat, and misleading top-level source for mixed evidence. | Rejected |
| One top-level evidence kind per role assignment | Add `EvidenceKind` and `Source` to `CardRoleAssignment`. | Small surface change. | Misrepresents cards with multiple role/tag sources. | Rejected |
| New parallel role explanation API | Build a new explanation model beside role-count explanations. | Could be tailored to odds. | Duplicates `DeckRoleCountExplanation` and risks drift. | Rejected unless the existing shape cannot be evolved. |
| Hard reject unknown legality everywhere | Treat unknown legality the same as not legal in all paths. | Prevents accidental illegal recommendations. | Silent filters would drop cards with incomplete snapshots and hide metadata gaps. | Rejected |
| Broad profile/config framework | Externalize role rules, bracket rules, meta scoring, and scorer weights together. | Flexible. | Premature and large compared with current need. | Rejected |
| Small phased evidence design | Fix correctness first, add minimal tier vocabulary, then expose lazy evidence where surfaced. | Reviewable, compatible, cheaper. | Requires discipline across phases. | Chosen |

## Chosen Design

Phase 1 uses one shared Core legality result with three states: `legal`,
`not_legal`, and `unknown`. Query and recommendation paths decide explicitly
how to handle unknown legality. The existing workspace legality audit remains a
metadata-gap reporter and should not be flattened into recommendation behavior.
The shared helper also owns format normalization so `commander`, `edh`, and
other aliases do not diverge between call sites.

Simulation presenters keep labels, assumptions, and caveats in summary output.
Large evidence rows remain gated to normal/full detail. Goldfish comparison
summary already carries model labels, so this phase focuses on preserving the
no-opponent/no-full-rules-engine assumption text.

Commander bracket output becomes a real 1-5 model by adding bracket-5/cEDH
detection criteria, updating thresholds, and changing calibration validation.
Phase 3 does not add canonical evidence-tier fields; it uses existing notes and
labels while fixing range, signal caps, duplicated calibration guards, corpus
data, and docs.

Evidence tiers are introduced as small Core-owned values adjacent to
`SourceEvidenceMetadata`. JSON uses the exact strings `source_fact`,
`source_evidence`, `derived_math`, `parser_derived`, `heuristic_inference`,
`model_score`, and `unsupported`. `Deterministic` continues to mean
repeatable/non-LLM, while evidence tier describes trust source. A deterministic
heuristic can still be `heuristic_inference`; blended scores are usually
`model_score`.

Role and odds provenance is opt-in. Existing cheap classifier and boolean
target-match paths remain. Phase 5 should first evolve
`DeckRoleCountExplanation` and `DeckAnalysisService.RoleCounts.cs`, which
already provide mixed matching evidence, before adding any new explanation API.

Tagger evidence does not need a new basic `otag:` route. The existing
Scryfall Tagger corpus provider already uses source-backed `otag:` searches.
The design hardens labeling so source-backed live/cached Tagger signals, local
user annotations, and embedded taxonomy matches remain distinct.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| Core legality helper | Normalize format and return tri-state legality. | Stateless. | Core method/type. | Card legalities only. | Unit tests for legal, not legal, unknown, and format normalization. |
| Evidence tier vocabulary | Define canonical trust strings. | Static values. | Core type serialized by MCP models. | Existing evidence metadata, role-count evidence rows, corpus evidence tables, combo source labels. | Serialization and surface tests. |
| Presenter detail policy | Keep compact caveats in summary and rows in normal/full. | Per response. | MCP JSON output. | App output presenters. | App presenter tests. |
| Bracket model correction | Support 1-5 estimates and bracket-5 criteria. | Static thresholds/calibration data. | Bracket tool output and docs. | Core metrics, signal criteria, duplicated calibration guards, calibration corpus. | Unit and calibration tests. |
| Role-count evidence rows | Evolve existing role explanation rows into structured/tiered evidence. | Per request. | Existing role explanation surface. | `DeckRoleCountExplanation`, `DeckAnalysisService.RoleCounts.cs`, classifier logic. | Mixed-source evidence tests and perf inspection. |
| Odds success-set output | Explain counted cards for target odds. | Per request/detail level. | MCP odds output. | Existing role-count explanation machinery where possible. | Detail-level tests. |
| Recommendation score metadata | Label model scores and confidence meanings. | Per response. | MCP recommendation output. | Recommendation scoring services. | Unit and surface tests. |
| Tagger evidence labels | Distinguish provider, cache, local annotation, and embedded taxonomy tag evidence. | Per evidence row. | Source evidence rows and role-count rows. | Existing Scryfall Tagger provider, facet annotations, embedded taxonomy. | Fake HTTP and Core labeling tests. |
| Profile resolver | Resolve bracket/scoring profiles only. | Host/deck selection. | Config/deck intent where applicable. | Existing `SimulationProfileCatalog` behavior and tests. | Resolver tests. |

## Runtime And Data Flow

Legality flow:

1. Caller requests legality decision for a card and format.
2. Shared helper normalizes the format key.
3. Helper returns `legal`, `not_legal`, or `unknown`.
4. Caller applies its path-specific policy:
   - reason-returning paths surface unknown as a warning or rejection reason
   - silent filters exclude `not_legal` and keep/penalize `unknown`
   - scoring paths reduce confidence for `unknown`

Evidence flow:

1. Cheap analysis paths continue using existing classifiers and scores.
2. User-visible normal/full detail requests use existing role-count
   explanation machinery where possible.
3. Explanation output emits rows with card, role/tag/category, counted-because,
   evidence tier, source label, and score meaning.
4. Presenters include compact labels in summary and detailed rows only at
   normal/full detail.

Tagger labeling flow:

1. Scryfall Tagger provider query results keep source-backed or source-cached
   labels.
2. Local `tagger.oracle_tags` annotations are labeled as local/user-provided
   facts, not provider facts.
3. Embedded deterministic taxonomy matches are labeled as parser-derived or
   heuristic evidence.
4. Only cards returned by source-backed or source-cached Tagger evidence receive
   Tagger-backed source labels.

## MCP Surface, Schemas, And Diagnostics

Expected detail-level rule:

| Detail level | Required trust content | Excluded content |
| --- | --- | --- |
| Summary | labels, assumptions, caveats, score meanings | per-card evidence rows, full success sets, component-score tables |
| Normal | summary content plus focused evidence rows and success sets | exhaustive debug traces |
| Full | normal content plus component scores, source rows, and diagnostic detail | unrelated provider internals |

Expected additive fields include evidence tier strings, source labels, score
kind, confidence meaning, assumptions, warnings, and counted-because reasons.
Tool descriptions and annotations must say when values are heuristic, derived,
or source-backed.

Canonical evidence tier wire values are exactly:

- `source_fact`
- `source_evidence`
- `derived_math`
- `parser_derived`
- `heuristic_inference`
- `model_score`
- `unsupported`

No new operation mode is planned.

## Adapter And Provider Contracts

Phase 7 should treat the existing Scryfall Tagger corpus provider as the
starting point. New adapter work is only needed if fixture coverage or labeling
requires it. Any adapter edits must preserve Scryfall ownership of HTTP
contracts, use fake HTTP or fixtures in normal tests, sanitize provider errors,
and avoid live network requirements.

No Archidekt mutation or real deck write is required.

## Cross-Cutting Concepts

- `Deterministic`: repeatable/non-LLM behavior, not proof of source backing.
- Evidence tier: trust category of a value or evidence row.
- Composite tier: blended values take the weakest useful label, usually
  `model_score`.
- Confidence meaning: name of what a numeric value represents, such as
  `classifierBranchScore`, `sourceCoverage`, `evidenceCoverage`, or
  `scoreConfidence`.
- Unknown legality: missing metadata; never silently equivalent to legal.

## Project Boundaries

Core owns domain types, legality semantics, evidence vocabulary, classifier
explanation, odds models, recommendation metadata, and bracket calculations.
App owns MCP tools, presenters, annotations, detail-level behavior, and surface
tests. Scryfall owns third-party search contracts and fixture-backed provider
tests. Core must not reference App or adapter projects.

## Readability And Documentation

Implementation should prefer small direct C# changes, reuse existing models and
presenter patterns, and avoid a new abstraction unless it removes real
duplication. New public C# declarations need useful XML summary comments. Docs
should be updated with behavior-level descriptions instead of duplicating
volatile implementation details.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| REQ-001 | One Core tri-state helper. | Core unit tests and call-site inspection. |
| REQ-002 | Path-specific unknown legality policy. | Query/recommendation/replacement tests. |
| REQ-003 | Summary presenter keeps labels and caveats. | Default summary App test. |
| REQ-004 | Bracket-5 criteria, signal caps, duplicated calibration guards, corpus data, and docs update together. | Bracket fixtures and calibration runner tests. |
| REQ-005 | Core evidence tier vocabulary. | Serialization and surface tests. |
| REQ-006 | Existing role-count explanation rows become structured/tiered; no top-level assignment source is added. | Mixed-source role explanation tests. |
| REQ-007 | Cheap classifier remains; explanation is opt-in. | Hot-path tests/inspection. |
| REQ-008 | Detail-level matrix enforced by presenters. | Summary versus normal/full tests. |
| REQ-009 | Score metadata names score kind and confidence meaning. | Recommendation output tests. |
| REQ-010 | Provider/cached Tagger evidence stays distinct from local annotations and embedded taxonomy. | Fake Scryfall fixture tests and Core labeling tests. |
| REQ-011 | Bracket/scoring resolver reuses the simulation profile pattern. | Resolver unit tests. |
| REQ-012 | Fixtures and fake HTTP keep tests offline. | `task test`. |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| Phase 1 | Core recommendations/query services | REQ-001, REQ-002, REQ-012 | Shared legality helper and call-site tests pass. |
| Phase 2 | App simulation presenters | REQ-003, REQ-008 | Default summary preserves model label/caveat. |
| Phase 3 | Core bracket metrics, signal criteria, calibration guards/corpus, App bracket output, docs | REQ-004 | Bracket 1-5 tests and docs pass without canonical evidence-tier fields. |
| Phase 4 | Core evidence models, App surface tests | REQ-005 | Evidence tier serialization and descriptions pass. |
| Phase 5 | Existing Core role explanation rows, classifier/odds, App odds output | REQ-006, REQ-007, REQ-008 | Tiered role-count explanation and success-set tests pass. |
| Phase 6 | Core recommendation scoring, App outputs | REQ-009 | Score metadata tests pass. |
| Phase 7 | Scryfall Tagger provider fixtures, Core/facet labels, embedded taxonomy labels | REQ-010, REQ-012 | Offline Tagger attribution fixtures pass. |
| Phase 8 | Core/App profile resolver using simulation profile pattern | REQ-011 | Resolver tests and docs pass. |

## Test Architecture

Use unit tests for Core helpers and scoring semantics, App tests for presenter
and MCP surface behavior, fixture-backed adapter tests for Scryfall evidence,
and calibration tests for Commander bracket range changes. Run focused tests for
each phase before `task test`. Run `task lint` when public MCP surface shape or
descriptions change.

## Framework And External Notes

Scryfall-backed Tagger work should begin with
`ScryfallTaggerCorpusSignalProvider` and its existing `otag:` search behavior
rather than a new direct Tagger API dependency. Bracket/scoring profile work
should begin from `SimulationProfileCatalog` behavior and tests rather than a
new resolver design. Any later provider work must document attribution,
permission sensitivity, pacing, retries, cache behavior, and sanitized errors
before implementation.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Lazy provenance | Decision | Protects performance and response size. | Use explanation APIs where provenance is surfaced. |
| Summary labels always travel | Decision | Keeps default outputs honest. | Include compact caveats without evidence rows. |
| Unknown legality policy by path | Decision | Avoids silent legal default and avoids silent candidate loss. | Apply path-specific behavior with tests. |
| Broad source integrations | Deferred | Would expand scope substantially. | Revisit after evidence shape is stable. |
| Role-rule externalization | Deferred | Could become a config framework. | Start with bracket/scoring profiles only. |

## Glossary

- Evidence tier: Canonical trust category serialized in MCP output.
- Source fact: A direct fact from an authoritative or declared provider.
- Source evidence: Provider-backed evidence that supports a claim but is not
  itself the final claim.
- Parser-derived: Deterministic extraction from card text or metadata.
- Heuristic inference: Repeatable rule-based inference that is not source fact.
- Model score: Blended or weighted output from heuristic components.
- Unknown legality: Missing legality metadata for a requested format.
