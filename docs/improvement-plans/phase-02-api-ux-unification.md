# Phase 2 - API and UX Contract Unification

| | |
|---|---|
| Effort | M-L |
| Risk | Medium (parameter/behavior changes) |
| Depends on | Phase 1 (normalize the final surface once) |
| Unblocks | Phase 3 (typed returns enable output schemas) |
| Target version | 0.8.0 (deprecation release) -> 0.9.0 (removal release) |

Goal: one consistent way to call tools and read results across the (post-consolidation)
surface - one output-control idiom, consistent identifiers, and one result/error framing.

## 1. Problems addressed

- **P3 - multiple output-control idioms.** **Seven** independent `summary/normal/full`
  constant classes plus per-presenter normalizers, a `compact/full` variant, a
  default-`full` variant, the legacy `includeWorkspace` bool, and extra `analysisMode` /
  `sourceSupportDepth` / refresh `scope` knobs.
- **P4 (naming/shape) - inconsistent parameters and result framing** across tools.
- **P5 (partial) - residual duplication** after Phase 1.

## 2. Goals / non-goals

Goals:
- A single shared `DetailLevel` concept (`summary|normal|full`) with one normalizer and
  consistent defaults, used by every tool and presenter.
- Remove the `includeWorkspace` bool and the `compact/full` variant.
- A documented, shared vocabulary for the few legitimate extra knobs.
- Consistent identifier and parameter naming and a consistent analytical result envelope.

Non-goals:
- Changing analytical *math* (Phase 7) or the protocol-level structured-output mechanics
  (Phase 3). Phase 2 normalizes the C#/parameter contract; Phase 3 exposes it over MCP.

## 3. Current state (investigation)

Confirmed duplication of the same idea in independent places - **seven** distinct
detail-level helper classes, each with its own normalizer:
1. `WorkspaceTools.WorkspaceStartDetailLevels` (`WorkspaceTools.cs:794-810`).
2. `CompactMutationPresenter.DetailLevels` + `ResolveDetailLevel(includeWorkspace, detailLevel)`
   (`CompactMutationPresenter.cs:44-55`) - the legacy-flag bridge.
3. `PlanPreviewPresenter.PreviewDetailLevels` (`PlanPreviewPresenter.cs:350-387`).
4. `GoldfishOutputPresenter.GoldfishDetailLevels` (`GoldfishOutputPresenter.cs:139-381`).
5. `PerformanceOutputPresenter.PerformanceDetailLevels` - **defaults to `full`** (the one
   default that visibly shrinks output when standardized on `summary`; call this out in the
   changelog).
6. `DeckNormalizationPresenter.DetailLevels` (`DeckNormalizationPresenter.cs:72`).
7. `CardFacetOutputPresenter.DetailLevels` (`CardFacetOutputPresenter.cs:138`).

Plus the non-class variants: `deck_evaluate_card` inline `compact|full`
(`RecommendationTools.cs:221-235`), and extra vocabularies `PreviewSourceSupportDepths`
(`none|minimal|balanced`), `PreviewAnalysisModes` (`none|summary|full`), and refresh `scope`
(`missing|stale|needed|all|included|maybeboard`).

Each presenter re-implements the same normalize-then-shape logic, so the inconsistency is
mechanical duplication, not deliberate per-tool design.

## 4. Workstreams

### 4.1 One shared DetailLevel
- Add a single `DetailLevel` enum (or shared static) in one place (e.g.
  `src/MtgMcp.App/Tools/Presentation/DetailLevel.cs`) with one `Parse`/`Normalize`.
- Replace all `*DetailLevels` classes and per-presenter normalizers with it. Presenters
  take the parsed enum, not raw strings.
- Standardize defaults: pick one default (proposal: `summary` for reads/mutations,
  `full` only where raw fidelity is the documented purpose) and document the rule.

### 4.2 Remove legacy/variant knobs
- Delete `includeWorkspace`; callers use `detailLevel=full`. Honor the Phase 0/1
  deprecation window (accept `includeWorkspace` for one minor with a deprecation note,
  then remove). Update mutation tools and `deck_plan_apply`.
- Convert `deck_evaluate_card` from `compact/full` to `summary/normal/full`.
- Centralize `sourceSupportDepth` and `analysisMode` into the shared vocabulary namespace,
  documented in one place; keep refresh `scope` (it is a domain concept, not a detail
  level) but document it alongside.

### 4.3 Identifier and parameter conventions
- Audit parameter names/order across the surviving surface: `workspaceId`, `planId`,
  `cardNameOrId`, `format`, `limit`, `seed`, `simulations`, `maxTurn`, `bypassCache`,
  `detailLevel`. Make order and naming consistent (e.g. `detailLevel` always last before
  `CancellationToken`).
- Add a convention test (extend `McpSurfaceTests`) asserting parameter-name vocabulary and
  that `detailLevel` defaults are from the shared enum.

### 4.4 Consistent analytical result envelope (Phase 2 is the owner)
- **Phase 2 is the definitional owner of the result-envelope contract.** This concept is
  touched by three phases (Phase 2 agrees the shape, Phase 4 types its status/enums, Phase 3
  exposes its schema), which is the roadmap's riskiest seam; it needs one owner so three
  docs don't produce three subtly different envelopes. Write the envelope as an ADR
  (`docs/adr/`) here; Phases 3 and 4 reference that ADR rather than redefining the shape.
- Define a shared envelope for analytical tools: `status`, `warnings`, `assumptions`, and
  determinism/source metadata, applied consistently (today `Status`/`Severity`/`Outcome`/
  `Notes` are ad hoc). In Phase 2, agree the shape (ADR) and apply it to the C# return
  types; Phase 4 types the closed-set fields; Phase 3 exposes the output schema.

## 5. Files to create / change

- Create: `src/MtgMcp.App/Tools/Presentation/DetailLevel.cs`,
  `.../OutputControlVocabulary.cs`, `docs/output-control.md`.
- Change: all tool classes that take `detailLevel`/`includeWorkspace`; all five presenters
  (`CompactMutationPresenter`, `PlanPreviewPresenter`, `GoldfishOutputPresenter`,
  `PerformanceOutputPresenter`, `DeckNormalizationPresenter`, `CardFacetOutputPresenter`);
  `RecommendationTools.cs` (evaluate-card levels); `README.md` and usage resources.
- Tests: `McpSurfaceTests` snapshot + new convention test.

## 6. Testing

- Snapshot updated; convention test enforces parameter naming and shared `detailLevel`.
- Per-presenter unit tests assert `summary`/`normal`/`full` produce the documented bounded
  shapes via the single normalizer.
- Deprecation test: `includeWorkspace` still works for the deprecation window.

## 7. Definition of done

Split across two releases so implementers know exactly what each version ships:

Deprecation release (0.8.0):
- Exactly one `detailLevel` normalizer/enum used everywhere; no `*DetailLevels` duplicates.
- The new unified `detailLevel` is in place on every tool; `includeWorkspace` and the
  `compact/full` variant still work but are marked deprecated in their descriptions and the
  changelog, and emit no behavior surprises.
- Parameter naming/order consistent and test-enforced; one documented analytical result
  envelope applied to return types.
- Extra knobs (`analysisMode`, `sourceSupportDepth`) documented in one place.
- A deprecation test asserts the legacy `includeWorkspace`/`compact` inputs still resolve.

Removal release (0.9.0):
- `includeWorkspace` and `compact/full` removed; the deprecation test is replaced with a
  test asserting they are rejected.
- `README.md`/usage resources reference only the unified idiom.

## 8. Risks & mitigations

- Risk: default changes alter output size for existing callers. Mitigation: document the
  default rule, keep `full` reachable, and call it out in the changelog.
- Risk: envelope unification touches many models. Mitigation: do the envelope as a thin
  shared shape first; defer deeper typing to Phase 4.

## 9. Open questions

- Default `detailLevel` per tool category - confirm the `summary`-default rule and the
  list of tools that legitimately default to `full` (performance/raw-fidelity tools).
- Should `analysisMode`/`sourceSupportDepth` collapse into `detailLevel`, or remain
  distinct axes? (Recommend distinct but shared-vocabulary, since they gate cost, not
  verbosity.)
