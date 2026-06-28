# Phase 5 - Core Service Decomposition

| | |
|---|---|
| Effort | XL |
| Risk | Medium-High (large internal refactor) |
| Depends on | Phase 4 (typed contracts) recommended first |
| Unblocks | sustainable change velocity; easier Phase 7 |
| Target version | 0.12.0 |

Goal: tame the largest services and the shared base so Core stays changeable and testable,
without altering external behavior.

## 1. Problems addressed

- **P16 - god services, fat base, duplicated repositories.**
  - `DeckRecommendationService` is ~6,079 LOC across 14 partials and depends on concrete
    `DeckAnalysisService`, `DeckSimulationService`, and `PlaygroupService`.
  - `DeckServiceBase` is ~2,125 LOC across 6 partials in 3 folders, including
    `Analysis/DeckServiceBase.AnalysisMetrics.cs` (831) and
    `Recommendations/DeckServiceBase.RecommendationHelpers.cs` (584) - helpers inherited by
    every service whether or not they need them.
  - `DeckSimulationService.Goldfish.cs` is a single ~2,912-line file.
  - `JsonDeckWorkspaceRepository` and `JsonDeckPlanRepository` duplicate the atomic-write /
    id-sanitize / enumerate-deserialize pattern.

## 2. Goals / non-goals

Goals:
- Break `DeckRecommendationService` along its existing concern seams into focused
  collaborators.
- Retire the fat `DeckServiceBase`: move analysis-metric and recommendation helpers into
  standalone injectable units used only where needed.
- Extract a shared `JsonFileStore<T>` for repositories.
- Reduce concrete service-to-service coupling where it blocks testing/substitution.
- Optionally split the giant goldfish file and introduce per-subdomain namespaces.

Non-goals:
- No behavior change; this is structure. The MCP surface and analytical results stay
  identical (guarded by existing snapshot/analysis tests).

## 3. Current state (investigation)

- `DeckRecommendationService` partials already name the seams: `BatchTuning`,
  `Brainstorming`, `CardEvaluation`, `Categories`, `CommanderCandidates`, `Corpus`,
  `Evidence`, `Goals`, `Meta`, `PlaygroupMeta`, `Queries`, `Replacements`, `Trends`
  (+ root). These are natural extraction units.
- `DeckServiceBase` partials: `Workspaces/DeckServiceBase{.cs,.Context,.Persistence,.WorkspaceHelpers}`
  plus the two heavy cross-folder helper files. The base holds `IDeckWorkspaceRepository`,
  `IDeckPlanRepository?`, `ICardCatalog`, `DateOnly?`.
- Existing guardrails to extend: `ProjectBoundaryTests.DeckWorkspaceService_DoesNotRecreateFeatureFacade`
  and `DeckFeatureFiles_StayInFeatureFolders` already prevent facade regressions and pin
  feature folders.
- `RequireArchidektGateway` is duplicated (`DeckMutationServiceBase.cs:37-41` and
  `DeckSimulationService.cs:42-46`) because the two-level base split doesn't cleanly cover
  who needs the gateway.

## 4. Workstreams

This phase is the most likely to sprawl, so it is structured as **independent, ordered
sub-PRs**, each shippable on its own. The workstreams below are numbered by topic, but the
required execution order is:

1. Sub-PR A: extract `JsonFileStore<T>` and reimplement both repositories on it (4.3).
   Lowest-risk, isolated, unblocks nothing else - do it first.
2. Sub-PR B: slim `DeckServiceBase` - move analysis-metric and recommendation helpers off
   the base into injectable collaborators (4.2).
3. Sub-PRs C..N: recommendation extraction (4.1) - **one focused service per PR**, and
   **leaf/standalone concerns first** (Queries, Replacements, BatchTuning) before the
   entangled ones (Corpus, PlaygroupMeta, then Evidence/Trends/Meta/CommanderCandidates).
   Ordering reduces the drift surface of turning 14 partials into ~7 services; not one
   mega-PR.
4. Sub-PR Z (optional, last): split the goldfish file and/or introduce per-subdomain
   namespaces (4.4).

Hard rule for every sub-PR: "no behavior change" explicitly includes **no analytical
snapshot drift** - the calibration suite and analysis/simulation snapshot tests must be
byte-identical before and after. A sub-PR that changes any analytical output is rejected as
out of scope for this phase.

### 4.1 Decompose DeckRecommendationService (Sub-PRs C..N, one service each)
- Promote partial-file concerns into separate services/collaborators, e.g.
  `CardQueryService` (Queries), `CardEvaluationService` (CardEvaluation - see Phase 7),
  `CommanderEvidenceService` (Evidence/Trends/Meta/CommanderCandidates),
  `CorpusRecommendationService` (Corpus), `PlaygroupMetaScoringService` (PlaygroupMeta),
  `BatchTuningReportService` (BatchTuning), `ReplacementService` (Replacements).
- Keep a thin `DeckRecommendationService` only if it still earns its place as a facade;
  otherwise have tools depend on the focused services directly (coordinate with Phase 1's
  consolidated tools and their DI).
- Depend on interfaces for cross-service needs (analysis/simulation/playgroup) where it
  improves testability; introduce narrow ports rather than taking whole concrete services.

### 4.2 Retire the fat base class
- Move `DeckServiceBase.AnalysisMetrics.cs` into an injectable `DeckAnalysisMetrics`
  collaborator and `DeckServiceBase.RecommendationHelpers.cs` into recommendation-side
  helpers; inject where used instead of inheriting everywhere.
- Reduce the base to the genuinely shared minimum (repo/catalog access, workspace load
  helpers). Resolve the duplicated `RequireArchidektGateway` by giving gateway-needing
  services a shared small helper or a dedicated base that actually matches need.
- Extend the facade-regression boundary test to also forbid the analysis/recommendation
  helpers from reappearing on the base.

### 4.3 Shared JSON file store
- Extract `JsonFileStore<T>` encapsulating: shared `JsonSerializerOptions`, atomic
  temp-file write + `File.Move(overwrite:true)`, id sanitization (fix the collision risk
  where distinct ids can map to the same path), and enumerate/deserialize listing.
- Reimplement both repositories on top of it. Consider an in-memory index to avoid
  O(n) disk reads in `ListAsync`.
- Forward dependency: Phase 8's collection store should reuse `JsonFileStore<T>`. Keep it
  generic enough for a third entity type (it must not assume workspace/plan specifics).

### 4.4 (Optional) split the goldfish file + namespaces
- Split `DeckSimulationService.Goldfish.cs` along clear phases (draw/sequence, board
  projection, win detection) into partials/collaborators.
- Consider per-subdomain namespaces (`MtgMcp.Core.Workspaces`, `.Simulation`, etc.) to
  regain encapsulation lost by the single flat `namespace MtgMcp.Core`. Gate this on churn
  cost; it can be a follow-up.

## 5. Files to create / change

- Create: focused service classes under `Recommendations/`, `Analysis/`,
  `Playgroups/`; neutral storage helpers under `Storage/` such as `JsonFileStore.cs`.
- Change: `DeckRecommendationService*` (shrunk/removed), `DeckServiceBase*` (slimmed),
  `JsonDeckWorkspaceRepository`/`JsonDeckPlanRepository` (delegate to store),
  `Hosting/MtgMcpHost.cs` DI registrations, `DeckSimulationService.Goldfish.cs` (optional
  split). Extend `tests/MtgMcp.Architecture.Tests/ProjectBoundaryTests.cs`.

## 6. Testing

- Lean on existing behavior tests as the safety net: `McpSurfaceTests`, the analysis/
  simulation/plan test suites, and the offline calibration suite. They should pass
  unchanged - that is the proof of a behavior-preserving refactor.
- Add focused unit tests for each extracted service and for `JsonFileStore<T>`
  (atomic write, id sanitization/collision, listing order).

## 7. Definition of done

- No single Core service file dominates; `DeckRecommendationService` is decomposed or a
  thin facade over focused services.
- `DeckServiceBase` no longer carries analysis/recommendation helper bulk; facade-
  regression test extended and green.
- Both repositories share `JsonFileStore<T>`; id-collision risk fixed.
- All existing behavior tests pass with no surface or analytical-output changes, and each
  sub-PR independently demonstrates zero analytical snapshot drift.

## 8. Risks & mitigations

- Risk: large refactor introduces subtle behavior drift. Mitigation: do it strictly after
  Phase 4 typing, in small PRs per extracted service, each green against the full suite;
  no logic edits during moves.
- Risk: DI wiring churn. Mitigation: register focused services centrally; keep transient
  lifetimes as today.
- Risk: namespace split is high-churn. Mitigation: treat as optional/last and isolate.
- Risk: the 85% coverage gate blocks CI. Moving ~6k LOC out of one service shifts coverage
  if tests don't move with the code. Mitigation: move/duplicate the relevant tests
  alongside each extraction in the same PR; expect (and review) per-PR coverage deltas
  rather than a single end-of-phase swing.

## 9. Open questions

- Keep `DeckRecommendationService` as a facade or remove it and wire tools to focused
  services? (Depends on Phase 1's consolidated tool DI.)
- How aggressively to introduce ports for cross-service calls vs depending on concrete
  collaborators within Core (Core has no DI boundary issue, but tests benefit from ports).
