# MCP Trust Evidence Implementation Plan

Use this document to define the order of work: which requirements are solved
first, second, third, and what evidence proves each phase is complete.

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

Deliver the work in reviewable slices. The first three phases are independent
correctness fixes: legality, simulation caveats, and Commander bracket 1-5
behavior. Phase 3 uses existing notes/labels only; canonical evidence-tier wire
fields arrive in Phase 4 so bracket range/calibration work is not blocked on
taxonomy design.

Each phase should have a narrow test first, then `task test` when the phase
touches shared behavior. Run `task lint` whenever public MCP surface shape,
tool descriptions, annotations, or docs change.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Phase 1 | Consolidate tri-state legality. | REQ-001, REQ-002, REQ-012 | Core recommendation/query legality paths | Unit tests, call-site inspection, `task test` | Missing legality no longer silently means legal. | Planned |
| Phase 2 | Reserved after goldfish supersession. | REQ-003 | No trust-PLC code area | Reciprocal packet inspection | conservative-goldfish-v2 phase 4 owns summary, detail, and replacement schema. | Superseded |
| Phase 3 | Correct Commander bracket 1-5 behavior. | REQ-004 | Core bracket metrics, calibration validators/corpus, docs | Unit/calibration tests | Bracket 5 can be emitted by explicit criteria without Phase 4 evidence fields. | Planned |
| Phase 4 | Add minimal evidence tier vocabulary. | REQ-005 | Core evidence models, existing evidence carriers, App surface tests | Serialization tests, surface tests | Canonical tier strings exist and serialize. | Planned |
| Phase 5 | Add lazy role/odds provenance. | REQ-006, REQ-007, REQ-008 | Existing role-count explanation rows, classifier/odds, App odds output | Unit tests, detail-level tests | Evidence rows are opt-in, structured, and detail-gated. | Planned |
| Phase 6 | Label recommendation scores. | REQ-009 | Core recommendation services, App output | Unit and surface tests | Model scores are visibly distinct from source facts. | Planned |
| Phase 7 | Harden Tagger evidence semantics. | REQ-010, REQ-012 | Existing Scryfall Tagger provider, Core/facet labels, embedded taxonomy labels | Fake HTTP and Core labeling tests | Provider/cached Tagger, local annotation, and taxonomy evidence stay distinct. | Planned |
| Phase 8 | Externalize small bracket/scoring profiles. | REQ-011 | Core/App profile resolver, docs | Resolver tests, docs inspection | Bracket/scoring profiles reuse the simulation profile pattern. | Planned |

## Phase Details

### Phase 1: Tri-State Legality Consolidation

- Problems solved: missing legality currently behaves inconsistently and can
  pass as legal in recommendation/query paths.
- Included requirements: REQ-001, REQ-002, REQ-012.
- Out of scope for this phase: evidence tiers, bracket changes, role
  provenance, and workspace legality audit changes.
- Expected edits: add one Core legality result/helper; route
  `DeckRecommendationCardFacts`, `DeckQueryRecommendationEngine`, replacement,
  corpus, playgroup, wincon payoff, commander-meta, and new-card paths through
  it as applicable.
- Required policy: use the SRD Phase 1 legality matrix. Reason-returning paths
  reject unknown legality with a visible reason; silent filters exclude
  `not_legal` and keep unknown cards only with an explicit warning, refresh
  note, or named confidence/score penalty.
- Validation: legal/not legal/unknown unit tests; call-site tests for visible
  reason, warning, keep-with-penalty, or exclusion behavior; format alias tests
  for shared normalization; `task test`.
- Exit criteria: no recommendation/query path treats missing legality as legal
  by default, and the already-correct workspace legality audit still reports
  metadata gaps.
- Rollback or fallback: revert call-site routing and helper together.
- Cleanup: remove duplicate legality helpers or keep only thin wrappers around
  the shared helper.

### Phase 2: Summary Caveat Preservation

- Problems solved: ownership conflict between the old additive caveat proposal
  and the atomic conservative goldfish replacement.
- Included requirements: REQ-003, marked superseded.
- Out of scope for this phase: all goldfish presenter and schema implementation.
- Expected edits: none in this PLC; use
  [conservative-goldfish-v2 phase 4](../conservative-goldfish-v2/IMPLEMENTATION_PLAN.md#phase-4-atomic-mcpdownstream-cutover-and-old-code-removal).
- Validation: inspect reciprocal links and CGF-FIX-027 through CGF-FIX-033.
- Exit criteria: no trust-PLC branch independently edits a goldfish presenter.
- Rollback or fallback: if the goldfish packet is abandoned, reopen REQ-003
  through an explicit PLC revision rather than silently restoring this phase.
- Cleanup: none.

### Phase 3: Commander Bracket 1-5 Correction

- Problems solved: bracket output range and calibration behavior do not fully
  represent bracket 5/cEDH.
- Included requirements: REQ-004.
- Out of scope for this phase: broader source ingestion and profile
  externalization.
- Expected edits: Core bracket thresholds/criteria, calibration runner or
  fixtures, duplicated calibration range validators, calibration corpus data,
  App output labels, `docs/commander-bracket-model.md`.
- Validation: bracket-5 fixture cases, range validation tests, calibration
  runner accepts 1-5, docs inspection.
- Exit criteria: bracket 5 can be emitted only through explicit criteria and
  Phase 3 uses existing notes/labels rather than canonical evidence-tier
  fields.
- Rollback or fallback: restore prior bracket range and calibration fixtures.
- Cleanup: remove obsolete 1-4 assumptions from tests/docs.

### Phase 4: Minimal Evidence Tier Vocabulary

- Problems solved: MCP outputs lack one shared trust vocabulary.
- Included requirements: REQ-005.
- Out of scope for this phase: sweeping every model and adding role evidence
  rows everywhere.
- Required inventory before edits: inspect `SourceEvidenceMetadata`,
  `DeckRoleCountExplanation`, `CorpusEvidenceTableBuilder`, combo
  `Source`/`SourceKind` models, bracket/simulation labels, and current
  provider `SourceKind` strings so the tier enum does not duplicate existing
  semantics accidentally.
- Expected edits: Core evidence tier type adjacent to
  `SourceEvidenceMetadata`; serialization support; docs/surface text for
  touched outputs.
- Validation: serialization tests for all tier strings; surface tests where
  tiers appear.
- Exit criteria: tier vocabulary exists and is used by existing
  evidence-bearing models or outputs touched by earlier phases. The Core type
  is then available to conservative-goldfish-v2 phase 2; if goldfish must land
  it first, both packets are updated in the same change.
- Rollback or fallback: remove new tier fields from touched models.
- Cleanup: replace local string constants with the shared vocabulary.

### Phase 5: Lazy Role And Odds Provenance

- Problems solved: draw odds and role matching can hide why a card counted as a
  target.
- Included requirements: REQ-006, REQ-007, REQ-008.
- Out of scope for this phase: broad score transparency and external profile
  work.
- Expected edits: evolve existing `DeckRoleCountExplanation` and
  `DeckAnalysisService.RoleCounts.cs` matching evidence into structured/tiered
  rows or adjacent rows; add a new explanation API only if the existing surface
  cannot support odds success sets without duplication.
- Validation: mixed-source role tests; odds summary versus normal/full tests;
  performance-sensitive inspection to ensure cheap paths remain cheap.
- Exit criteria: summary shows compact evidence labels and assumptions, while
  normal/full can show success sets with counted-because rows.
- Rollback or fallback: keep cheap classifier path and remove explanatory
  output fields.
- Cleanup: avoid duplicate role explanation code across odds and role count
  surfaces.

### Phase 6: Recommendation Score Transparency

- Problems solved: blended recommendation scores can look like source facts.
- Included requirements: REQ-009.
- Out of scope for this phase: changing recommendation algorithms beyond labels
  and confidence semantics unless required by legality fixes.
- Expected edits: recommendation score metadata, confidence meaning labels,
  App output descriptions.
- Validation: recommendation output tests show score kind, evidence tier, and
  confidence meaning.
- Exit criteria: source facts, source evidence, and model scores are visibly
  separate in relevant outputs.
- Rollback or fallback: remove additive score metadata fields.
- Cleanup: remove misleading wording from tool descriptions and docs.

### Phase 7: Tagger Evidence Semantics

- Problems solved: Tagger-like labels can be confused with source-backed card
  evidence even though the Scryfall Tagger provider already uses concrete
  `otag:` searches.
- Included requirements: REQ-010, REQ-012.
- Out of scope for this phase: direct Tagger GraphQL dependency, EDHREC,
  Comprehensive Rules, price providers, and discussion evidence.
- Expected edits: Scryfall Tagger fixture coverage only if needed; Core/facet
  evidence labeling for provider query results, cached provider results, local
  `tagger.oracle_tags` annotations, and embedded taxonomy matches.
- Validation: fake HTTP and Core labeling tests prove only source-returned or
  source-cached cards receive Tagger-backed source labels; normal tests stay
  offline.
- Exit criteria: embedded deterministic matches and user annotations are not
  labeled as live/cached provider Tagger evidence.
- Rollback or fallback: disable Tagger-backed evidence labeling and keep
  existing deterministic catalog behavior.
- Cleanup: remove temporary fixture builders or duplicate tag mapping code.

### Phase 8: Small Profile Externalization

- Problems solved: bracket/scoring heuristics need limited tuning without a
  large role-rule framework or duplicate profile resolver.
- Included requirements: REQ-011.
- Out of scope for this phase: role definition externalization and classifier
  rule profiles.
- Expected edits: bracket/scoring profile models and resolver that reuse
  `SimulationProfileCatalog` behavior or create the smallest sibling only when
  semantics differ; docs update.
- Validation: resolver tests for explicit deck intent, host default, built-in
  fallback, missing explicit profile id, and warning behavior aligned with
  existing simulation profile tests.
- Exit criteria: bracket/scoring profiles are selectable and failures are clear.
- Rollback or fallback: keep built-in defaults and remove external selection.
- Cleanup: avoid scaffolding unused profile categories.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Evidence work becomes a rewrite. | Phases 4-6 | Attach tiers only where surfaced; keep explanation opt-in. | mtg-mcp implementer |
| Unknown legality handling silently removes useful candidates. | Phase 1 | Decide path behavior before editing each caller. | mtg-mcp implementer |
| Summary output becomes too verbose. | Phases 2, 5, 6 | Keep labels/caveats in summary and rows in normal/full. | mtg-mcp implementer |
| Bracket-5 detection overfits fixtures. | Phase 3 | Use multiple fixture archetypes and retain caveat labels. | mtg-mcp implementer |
| Provider tests accidentally require network. | Phase 7 | Use fake HTTP and fixture files only in normal tests. | mtg-mcp implementer |

## Completion Criteria

- [x] Every Must requirement from the SRD appears in at least one phase.
- [x] Dependencies between phases are explicit.
- [x] Phase 1 is useful without requiring all later phases.
- [x] Every phase has validation and exit criteria.
- [x] MCP surface, operation-mode, docs, and public contract changes are tested or explicitly deferred for each affected area.
- [x] Provider and adapter changes use fixture-backed tests and keep live tests opt-in.
- [x] Validation uses Task commands rather than one-off shell commands where Task has an equivalent.
- [x] Documentation and readability cleanup are included in the relevant phase.
- [x] Core/App/adapter boundaries stay aligned with repo architecture.
- [x] Deferred work is captured in the SRD, SADD, or follow-up plans.
