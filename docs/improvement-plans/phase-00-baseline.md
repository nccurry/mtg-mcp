# Phase 0 - Baseline, Guardrails, and Quick Wins

| | |
|---|---|
| Effort | S-M |
| Risk | Low |
| Depends on | none |
| Unblocks | all phases (metrics + deprecation policy) |
| Target version | 0.8.0 |

Goal: make the surface measurable, put change-management policy in place, and fix the
cheap honesty/documentation gaps before any larger refactor. There is no breaking
*capability* change here - every existing workflow still works - but two changes alter tool
*output shape* and are changelogged accordingly: adding `RngKind` (purely additive) and
switching `deck_evaluate_card`'s non-ramp path from `Score: 0` to an explicit
not-applicable status object (a real shape change for that input, not just an addition).

Note: these output changes are contract-visible and must be reflected in the
`McpSurfaceTests` snapshot, the changelog, and (once enabled) the output schemas. Treat them
as contract changes, not invisible internals.

## 1. Problems addressed

- **P11 - Undocumented surface.** `commander_search_candidates`
  (`RecommendationTools.cs:108`), `deck_evaluate_card` (`:213`),
  `deck_batch_tuning_report` (`:241`), and resources `mtg://workspace/{id}/state`
  (`MtgResources.cs:123`) and `mtg://workspace/{id}/assistant-context` (`:155`) are
  registered but absent from `README.md`, which claims to enumerate the surface.
- **P10 (label only) - `deck_evaluate_card` over-claims.** Description says "deterministic
  card evaluation" but the implementation is ramp-only (`RampContextScorer.cs:26-31`
  returns `Score = 0` + "No ramp operational facts were detected" for non-ramp cards).
  The real fix is Phase 7; Phase 0 only stops the over-claim.
- **P13 (label only) - goldfish determinism.** At Phase 0 planning time,
  `GoldfishSimulationResult` (`Simulation/GoldfishModels.cs:204`) carried `ModelLabel`
  but no `RngKind` or determinism label, and the goldfish path used `System.Random`
  (`DeckSimulationService.Goldfish.Run.cs:20`), unlike the deterministic
  `DeckPerformanceAnalysis` which stamped
  `RngKind = DeterministicSimulationRandom.Kind` (`PerformanceModels.cs:46`).
- **No surface metrics / no change policy.** There is a strong surface snapshot test
  (`tests/MtgMcp.App.Tests/Tools/McpSurfaceTests.cs`) and boundary tests, but nothing
  measures tool count / annotation coverage / description quality, and there is no
  written deprecation policy for the pre-1.0 surface.

## 2. Goals / non-goals

Goals:
- A CI-visible report of surface size and health (tool/resource/prompt counts, per-tool
  schema-size estimate, annotation and `Title` coverage, description length sanity).
- A documented deprecation/versioning policy and a lightweight ADR process.
- Documentation that matches the registered surface exactly, enforced by a test.
- No tool/resource description that over-claims relative to its implementation.

Non-goals (deferred):
- Reducing the tool count (Phase 1), redesigning evaluation (Phase 7), or changing the
  goldfish RNG (Phase 7). Phase 0 only labels and documents.

## 3. Current state (investigation)

- Surface registration is attribute-scan: `WithToolsFromAssembly().WithResourcesFromAssembly().WithPromptsFromAssembly()`
  (`Hosting/MtgMcpHost.cs:126-128`). Tool types are enumerated explicitly in the surface
  test (`McpSurfaceTests.cs:49-66`) - a reliable list to drive a metrics check.
- Repo already uses repo-introspecting tests that locate the root via `mtg-mcp.slnx` and
  walk `src`/`tests` (`DocumentationCommentTests.cs:343-357`, `ProjectBoundaryTests.cs:221-235`).
  A documentation-reconciliation test should follow this pattern.
- `ServerInfoService` already produces version/git info for `server_get_info` and
  `mtg://server/info`; the metrics tool can reuse reflection over the tool types rather
  than runtime server start.
- `docs/architecture.md` exists and is accurate; `README.md` "MCP Surface" section is the
  canonical human list that is currently incomplete.

## 4. Workstreams

### 4.1 Surface metrics + CI check
- Add a test/tool (e.g. `tests/MtgMcp.App.Tests/Tools/McpSurfaceMetricsTests.cs`, or a
  small console mode behind `mtg-mcp --surface-report`) that reflects over the tool/
  resource/prompt types and emits: total counts, per-tool input-schema property count and
  rough token estimate, annotation completeness, `Title` presence, and description
  length bounds.
- Ship the check **report-only first**: it prints the metrics and never fails the build,
  so the team can review and accept a baseline. After the first accepted baseline, ratchet
  it to fail CI on unapproved regressions (e.g. tool count increasing without bumping an
  agreed ceiling constant). Seed the ceiling constant at the **current count (114)** as a
  non-regression ratchet; Phase 1 owns lowering it. Wire it into `task lint`/CI so later
  phases can show progress.

### 4.2 Deprecation + ADR policy
- Write `docs/versioning.md`: how surface changes are announced, how a tool/parameter is
  marked deprecated (description prefix + changelog), the minimum deprecation window, and
  that breaking surface changes target pre-1.0 minors.
- Add `docs/adr/0001-record-architecture-decisions.md` and a short template; record the
  "evidence-first, LLM-does-judgment" and "API-only sources" decisions retroactively so
  later phases have a place to log trade-offs.

### 4.3 Documentation reconciliation + enforcement
- Update `README.md` to list `commander_search_candidates`, `deck_evaluate_card`,
  `deck_batch_tuning_report`, and the two missing resources - or remove them if Phase 1
  will cut them (decide per [Phase 1](phase-01-surface-consolidation.md)).
- Add a test that cross-checks every registered tool/resource/prompt name against the
  README surface section so docs can never silently drift again.

### 4.4 Honesty quick wins (no redesign)
- `deck_evaluate_card`: Phase 0 made the then-ramp-only implementation honest by
  tightening the `[Description]` and returning an explicit non-applicable status instead
  of a bare `Score: 0`. Phase 7 supersedes this with the current supported-role evaluator
  for ramp, draw, and interaction under the same tool name.
- Goldfish determinism labeling: add `RngKind` (and a short determinism note) to
  `GoldfishSimulationResult`, `ProjectedTurnState`, and `WinTurnEstimate`, populated with
  the actual generator kind. Phase 0 labeled the then-current generator honestly; Phase 7
  moves the family to the shared stable deterministic RNG. Update the surface snapshot
  test accordingly. (Adding these fields is an additive, contract-visible output change -
  see the note at the top of this phase.)

## 5. Files to create / change

- Create: `docs/versioning.md`, `docs/adr/0001-*.md` (+ template),
  `tests/MtgMcp.App.Tests/Tools/McpSurfaceMetricsTests.cs`,
  `tests/MtgMcp.App.Tests/DocumentationSurfaceTests.cs`.
- Change: `README.md` (surface section), `RecommendationTools.cs` (Phase 0 evaluate
  description + non-ramp status; Phase 7 later broadens it), `OperationalFacts/RampContextScorer.cs`
  (explicit Phase 0 not-applicable result; Phase 7 later replaces this with supported-role
  output), `Simulation/GoldfishModels.cs` (+`RngKind`),
  `Simulation/DeckSimulationService.Goldfish*.cs` (stamp `RngKind`),
  `McpSurfaceTests.cs` (snapshot update), `Taskfile.yml`/`ci.yml` (run metrics check).
- Update: `CHANGELOG.md`.

## 6. Testing

- Metrics and doc-reconciliation tests run offline in `task test`.
- Snapshot test updated for the new `RngKind` fields and the evaluate-card status field.
- No live tests required.

## 7. Definition of done

- CI prints a surface report (report-only initially); after a baseline is accepted, it
  ratchets to fail on unapproved count growth.
- `docs/versioning.md` + ADR process exist and are linked from `CONTRIBUTING.md`.
- A test enforces README-vs-registered-surface parity; it passes.
- No tool/resource description over-claims; `deck_evaluate_card` returns an explicit
  status for roles outside its current scope; goldfish results carry `RngKind`.

## 8. Risks & mitigations

- Risk: metrics budgets become noisy. Mitigation: start as report-only, then ratchet a
  single ceiling constant.
- Risk: doc-parity test is brittle to formatting. Mitigation: parse by tool-name tokens,
  not exact prose; keep a small allowlist for intentionally hidden internals.

## 9. Open questions

- Should the surface report be a test, a `--surface-report` CLI mode, or both? (CLI mode
  doubles as a release artifact and is reusable by Phase 9 observability.)
- For undocumented tools, document-then-cut in Phase 1, or cut now? Recommend documenting
  now and letting Phase 1 decide removals under the deprecation policy.
