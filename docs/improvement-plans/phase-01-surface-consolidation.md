# Phase 1 - Tool Surface Consolidation and Toolsets

| | |
|---|---|
| Effort | L |
| Risk | High (breaking surface change) |
| Depends on | Phase 0 (metrics + deprecation policy) |
| Unblocks | Phase 2 (normalize once on final surface), Phase 8 (toolset gating) |
| Target version | 0.9.0 (deprecations) -> 0.10.0 (removals) |

Goal: bring the advertised tool count from ~114 down to a number real MCP clients and
models handle well, without losing capability, and let deployments advertise only the
tools they need.

## 1. Problems addressed

- **P1 - Too many tools.** ~114 `[McpServerTool]` across 15 tool types
  (`McpSurfaceTests.cs:49-66`). Every tool's schema is injected into client context
  (token cost) and large lists degrade model tool-selection.
- **P2 - Mode-blind advertising.** `WithToolsFromAssembly()` registers all tools
  unconditionally (`Hosting/MtgMcpHost.cs:126`); `OperationModeGuard` only throws at call
  time (`Hosting/OperationModeGuard.cs:47-67`). A `read-only` deployment still advertises
  ~40 mutating tools that can only fail.
- **P5 (partial) - tool/resource duplication.** `server_get_info` vs `mtg://server/info`;
  `source_list` vs `mtg://sources/status`; `playgroup_get_auth_status` vs
  `mtg://providers/{provider}/auth-status`.
- **P24 - no subset selection.** There is no way to enable a subset of capabilities.

## 2. Goals / non-goals

Goals:
- A documented target ceiling (proposal: <= ~60 advertised tools in the documented core
  profile, trending lower) tracked by the Phase 0 metrics check.
- Configurable toolsets so a deployment advertises only chosen capability groups.
- Operation-mode-aware advertising: do not list tools that cannot run in the current mode.
- Net capability preserved: every workflow in `README.md` remains achievable.

Non-goals:
- Changing tool *semantics* or output shape (that is Phase 2/3). Phase 1 changes *which*
  tools exist/are advertised and merges near-duplicates behind parameters.

## 3. Current state (investigation)

- SDK capabilities available now (`ModelContextProtocol` 1.4.0):
  - `WithTools<T>()` / `WithTools(IEnumerable<Type>)` for selective registration.
  - `WithListToolsHandler` / `WithCallToolHandler` for a fully dynamic surface.
  - `McpServerPrimitiveCollection<T>` with a `Changed` event and
    `notifications/tools/list_changed` for runtime changes.
- Tools are organized by class but the file->prefix mapping is not 1:1 (e.g.
  `deck_analyze_structure`/`deck_list_cards_by_zone` live in `WorkspaceTools`;
  `archidekt_*` deck/folder tools split between `WorkspaceTools` and `CheckpointTools`).
- Clear near-duplicate families confirmed:
  - `workspace_checkpoint_{create,list,get,restore,delete}` vs
    `archidekt_checkpoint_{create,list,get,rename,delete}` differ only by local vs
    Archidekt backing (`CheckpointTools.cs:35-239`).
  - `deck_simulate_goldfish` + `deck_project_board_state` + `deck_estimate_win_turn` are
    three views of one goldfish run (`DeckSimulationService.Goldfish.cs:87-145`).
  - `deck_compare_goldfish`, `archidekt_compare_goldfish`, and
    `deck_compare_workspaces_analysis` overlap heavily.
  - Many `deck_analyze_*` are single-aspect reads (`AnalysisTools.cs`).

## 4. Workstreams

### 4.1 Categorize the full surface (keep / merge / demote / remove)
Produce a reviewed inventory; see [Appendix A](#appendix-a-consolidation-map) for the
starting proposal. Each decision records rationale and the deprecation path. Do this
first and get sign-off before code changes.

**Required gate: a workflow coverage matrix.** Before any merge/demote/remove decision is
approved, produce a matrix mapping each README workflow and built-in prompt to the
tools/parameters that satisfy it, both before and after consolidation. A decision is only
approved if every existing workflow remains achievable post-change. This matrix is a
checked-in artifact (`docs/toolsets.md` or an appendix) and is the acceptance evidence for
this phase, not just a checklist.

### 4.2 Merge near-duplicate tools behind parameters
- Unify checkpoints into one `checkpoint_*` family that infers local vs Archidekt from
  the workspace binding (or takes an explicit `target`), replacing the 10 tools with ~5.
- Fold board-state/win-turn into `deck_simulate_goldfish` views (e.g. a `view`/`projection`
  parameter or returning all three in one bounded result), or keep one and demote the
  others. Confirm callers/prompts updated.
- Collapse the three "compare" tools into one comparison entry point with a `source`
  (local ids / Archidekt ids / baseline) and `mode` (goldfish / analysis) parameter.
- Consider one `deck_analyze` entry point with a typed `aspect` selector
  (`structure|mana|consistency|cost|bracket|draw-odds|land-drops|best-practices`) for the
  lighter analyses, keeping heavyweight ones (`performance`, `combos`) separate.
- **Weigh the Phase 3 output-schema cost when merging.** A `selector`/`aspect`/`view`
  parameter creates a "one tool, heterogeneous output by parameter" shape that Phase 3 must
  then express as an output schema. Prefer selectors whose results share a **stable
  superset shape** (optional sections that are null when not selected) over selectors that
  return structurally different payloads per value; this keeps the Phase 3 schema meaningful
  and avoids an `object`-typed return. If a merge would force genuinely disjoint shapes,
  prefer keeping the tools separate.

### 4.3 Demote status/info tools to resources
- Where a canonical resource already exists, drop the duplicate tool (or vice versa):
  `server_get_info` -> `mtg://server/info`; `source_list` -> `mtg://sources/status`;
  provider auth-status tool -> `mtg://providers/{provider}/auth-status`. Keep exactly one
  canonical home and document why if both must stay for client compatibility.

### 4.4 Toolsets (method-level registry from the start)
- Build a **method-level** registry (one entry per tool method), not a tool-type->toolset
  map. This is deliberate: several tool *classes* contain mixed capabilities (e.g.
  `WorkspaceTools` holds read-only `workspace_list` alongside mutating `workspace_start` and
  `archidekt_*` writes; `AnalysisTools` mixes the read-only analyses with the planning-state
  `deck_refresh_card_metadata`). A type-level map would mis-group these.
- Each registry entry carries: tool name, owning class+method, toolset group, and a
  capability tag (`read` / `plan` / `mutate`). Source the entries by reflecting over the
  `[McpServerTool]` methods (the existing annotations already encode read/destructive), and
  add the toolset + capability tag via a small attribute or a central table keyed by tool
  name. The Phase 0 metrics check and the surface snapshot validate the registry is
  complete and consistent with the annotations.
- Define toolset groups (proposal): `cards`, `workspace`, `editing`, `analysis`,
  `simulation`, `plans`, `sources`, `archidekt`, `playgroup`, `intent`, `facets`,
  `combos`, `collection` (Phase 8), `server`.
- Add config `MtgMcp.Toolsets` (env `MTGMCP__TOOLSETS=...`) selecting enabled groups.
  In the 0.9.0 compatibility/deprecation release, blank means "all tools allowed by the
  current operation mode" so existing MCP client configs do not silently lose tools. The
  reduced default/core profile becomes the default only with the planned 0.10.0 removal
  release. Implement registration by filtering the method-level registry and registering
  the selected methods (via `WithTools(...)` over the resolved methods, or a
  `WithListToolsHandler` that filters by the registry) instead of `WithToolsFromAssembly()`.

### 4.5 Mode-aware advertising
- Drive mode-aware advertising off the same method-level registry capability tag: in
  `read-only` advertise only `read` tools; in `plan` advertise `read` + `plan`; in `apply`
  advertise all. Because the tag is per method, mixed-capability classes are handled
  correctly without re-deriving from method bodies.
- **Single source of truth for capability.** The registry capability tag is authoritative
  for *both* advertising and call-time gating: `OperationModeGuard`'s check should derive
  from the same registry entry rather than each tool independently passing a string to
  `EnsureCanMutate`/`EnsureCanWritePlanningState`. If the tag and the guard derive
  independently they will drift.
- Keep `OperationModeGuard` at call time as defense-in-depth; advertising is the UX layer.
- Tests: (a) the registry capability tag matches each tool's MCP annotations (a `mutate`
  tool must not be `ReadOnly=true`); (b) **every `mutate`/`plan`-tagged tool actually
  invokes the matching guard, and every guarded tool is correspondingly tagged** - so the
  advertised mode and the enforced mode cannot diverge.
- **Toolset x mode is an intersection.** The advertised set is `toolsets ENABLED` AND
  `capability allowed by current mode`. Document this (e.g. enabling the `editing` toolset
  in `read-only` mode advertises none of its mutating tools).

### 4.6 Migration + deprecation
- Under the Phase 0 deprecation policy: ship 0.9.0 advertising the new/merged tools while
  the old names remain (descriptions prefixed "Deprecated: use X"), then remove old names
  in 0.10.0. Update `McpSurfaceTests.cs` and its `removedToolNames` guard each step.
- Update `README.md`, prompts (`MtgPrompts.cs`), and usage resources
  (`mtg://usage/*`) to reference the new surface.

## 5. Files to create / change

- Create: `src/MtgMcp.App/Hosting/ToolRegistry.cs` (method-level entries: name, method,
  toolset, capability tag, + selection/filtering), `docs/toolsets.md` (incl. the required
  workflow coverage matrix).
- Change: `Hosting/MtgMcpHost.cs` (selective + mode-aware registration), the merged tool
  classes (`CheckpointTools`, `SimulationTools`, `AnalysisTools`, `DeckReEvaluationTools`),
  `MtgResources.cs`/`ServerTools.cs`/`CorpusTools.cs` (demotions),
  `Options.cs` (`Toolsets` option), `MtgPrompts.cs`, `README.md`,
  `McpSurfaceTests.cs` (+ metrics ceiling from Phase 0).
- Add tests: toolset selection, mode-aware advertising count per mode.

## 6. Testing

- Surface snapshot updated; metrics check confirms the documented core/default profile is
  under the agreed ceiling once that profile becomes the default.
- New tests: enabling a toolset advertises exactly its tools; `read-only`/`plan`/`apply`
  advertise the expected counts; deprecated aliases still resolve during the window.
- E2E (`tests/MtgMcp.E2E.Tests`) updated to drive merged tools.

## 7. Definition of done

- 0.9.0 compatibility/deprecation release: advertised tools are mode-aware, configurable by
  toolset, and still preserve the blank-toolset compatibility profile.
- 0.10.0 removal/default-profile release: default advertised tool count is at/under the
  agreed ceiling and mode-aware.
- Toolsets are configurable and documented; default profile covers the README workflows.
- Duplicated status tools resolved to one canonical home.
- Deprecation window honored; removals land in the planned minor.

## 8. Risks & mitigations

- Risk: breaking existing client configs. Mitigation: deprecation window + changelog +
  keep aliases for one minor.
- Risk: merged tools grow too many parameters. Mitigation: prefer a small typed selector
  enum over many booleans; cap parameter count per tool in the metrics check.
- Risk: capability regressions. Mitigation: the required workflow coverage matrix (4.1) is
  a sign-off gate before any merge/demote, and is verified in E2E.

## 9. Open questions

- Final ceiling number and the exact toolset taxonomy (needs maintainer sign-off).
- Should toolsets also gate resources/prompts, or tools only? (Recommend tools first.)
- Merge vs demote for the three "compare" tools - one tool with modes, or keep
  `deck_compare_goldfish` and remove the others?

## Appendix A: consolidation map (starting proposal)

Counts are current per tool class. "->" is the proposed action.

- Cards (4): keep. Add `card_get_batch` is Phase 8, not here.
- Workspace lifecycle (`WorkspaceTools`, 19): keep core
  (`workspace_start/list/open/export/parse/validate/validate_legality/refresh/diff/diff_last_import`);
  move `deck_analyze_structure` into the analysis group; fold `workspace_diff` +
  `workspace_diff_last_import` behind a `baseline` parameter; reconsider
  `workspace_reopen_with_writeback` as a `workspace_start` option.
- Archidekt deck/folder (in `WorkspaceTools`, 6): keep but group under the `archidekt`
  toolset; consider merging `archidekt_list_decks`/`archidekt_list_folders` framing.
- Checkpoints (10 -> ~5): merge local + Archidekt families into one `checkpoint_*` set
  inferring backing from the workspace.
- Mutations (`DeckMutationTools`, 7): keep; single-card `deck_add_card`/`deck_remove_card`
  could become thin convenience wrappers over the bulk tools, or be dropped in favor of
  bulk-only (decide with maintainer).
- Categories (`CategoryTools`, 8): keep create/rename/delete; consider merging
  add/remove/set-primary into one `deck_update_card_categories` with an `op` selector and
  keeping the bulk variant.
- Analysis (`AnalysisTools`, 15): merge the light single-aspect reads
  (`structure/mana/consistency/cost/bracket/draw_odds/land_drop_odds/best_practices`) into
  one `deck_analyze` with an `aspect` selector; keep `deck_analyze_combos`,
  `deck_refresh_card_metadata`, `deck_summarize`, `deck_explain_role_counts`,
  `deck_review_weak_spots`, and the `combo_*`/`card_classify_win_routes` tools.
- Re-evaluation (`DeckReEvaluationTools`, 2): keep `deck_re_evaluate`; fold
  `deck_compare_workspaces_analysis` into the unified comparison tool.
- Simulation (7 -> ~4): keep `deck_simulate_goldfish` (absorb board-state + win-turn as
  views), keep `deck_analyze_performance` and `deck_plan_compare_performance`; merge the
  goldfish/Archidekt comparisons into one comparison tool.
- Plans (8): keep; they are coherent and distinct.
- Recommendations (10): keep `deck_query_cards`, `deck_review_new_card_swaps`,
  `commander_get_*`, `wincon_find_payoffs`, `deck_score_cards_for_playgroup_meta`; review
  `commander_search_candidates` and `deck_batch_tuning_report` (heavy; consider gating
  behind a toolset).
  - **`deck_evaluate_card` rename decision (resolved, spans Phases 1/7):** this phase
    renames it to an honest ramp-scoped name (e.g. `deck_evaluate_ramp_card`) under the
    deprecation/removal flow, because the tool is ramp-only until Phase 7. Phase 7 then
    introduces the general evaluator (under `deck_evaluate_card` or a clear general name),
    accepting one deprecation of the interim ramp-scoped name. This keeps every shipped
    version honest (no generically-named-but-ramp-only tool for several minors) and avoids a
    silent multi-minor mismatch. Do not leave this as an open question.
- Sources (`CorpusTools`, 7): keep; demote `source_list` to the existing resource.
- Intent (4), Facets (5), Playgroup (7): keep; gate behind their toolsets.
- Server (1): demote `server_get_info` to `mtg://server/info`.

Indicative result: roughly 114 -> ~55-65 advertised tools before mode/toolset filtering,
and far fewer in a scoped deployment.
