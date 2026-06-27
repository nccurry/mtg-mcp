# Phase 4 - Domain Typing and Model Hygiene (Core)

| | |
|---|---|
| Effort | L |
| Risk | Medium (serialization compatibility) |
| Depends on | none hard; coordinate with Phase 2/3 result shapes |
| Unblocks | Phase 5 (clearer contracts to refactor against) |
| Target version | 0.13.0 |

Goal: close the gap between the repo's stated type-safety philosophy and the code. The
project deliberately targets `net11.0` + `LangVersion=preview` to use C# union types, but
uses them once.

## 1. Problems addressed

- **P15 - union types underused; outcomes are stringly-typed.** `union` appears once
  (`Plans/DeckPlanService.Apply.cs:831`) and is flattened into a `bool Success` +
  `string Status` DTO. A string-discriminated `DeckEditOperation` (`Plans/DeckPlanModels.cs:82`)
  carries ~12 mostly-nullable fields dispatched by a stringly-typed switch keyed off
  `DeckEditOperations` constants (`DeckPlanModels.cs:292`).
- **P17 - domain vs response DTO mixing.** `Models/WorkspaceModels.cs` (~1,809 LOC) and
  `Models/DeckTuningWorkflowModels.cs` (~1,449 LOC) mix true entities (`DeckWorkspace`,
  `DeckCard`) with tool-response shapes (`DeckOpenResult`, `WorkspaceDiffResult`, etc.).
- Pervasive `string Status`/`Kind`/`Severity`/`Outcome`/`Mode` for closed sets, e.g.
  `DeckEditPlanStatus`, `CorpusSourceStatuses`, `WorkspaceDiffLastImportStatuses`,
  `WorkspaceRefreshFromSourceStatuses`, `CorpusCacheModes`, `Severity = "warning"`
  (`WorkspaceModels.cs:1388`), `Status = "ok"` defaults (`CardFacetModels.cs:165`,
  `DeckBestPracticeModels.cs:36`).

## 2. Goals / non-goals

Goals:
- Model closed alternatives and discriminated outcomes as unions with exhaustive switches.
- Replace the `DeckEditOperation` god-DTO with a union of operation cases.
- Replace closed-set status/kind strings with enums (or unions) while preserving JSON wire
  values and on-disk compatibility.
- Separate domain entities from tool-response DTOs.

Non-goals:
- Service decomposition (Phase 5). This phase changes types, not service structure.
- Changing the MCP wire surface semantics (Phase 2/3 own that). Wire strings must stay
  stable here.

## 3. Current state (investigation)

- Only one `union` in Core; it is `private`, consumed by an exhaustive switch
  (`DeckPlanService.Apply.cs:94-113`), then flattened to `DeckEditPlanApplyResult`
  (`DeckPlanModels.cs:204`).
- **In-repo precedent (the pattern is proven, just not in Core):** the App/Cli auth commands
  already use unions - `RedditAuthParseResult`, `ArchidektAuthParseResult`,
  `PlaygroupAuthParseResult`. Cite these as the reference style; this phase brings the
  established pattern into Core rather than introducing it from scratch.
- **Lower-risk than it looks for serialization:** there is no source-generated
  `JsonSerializerContext` in the repo, so custom `System.Text.Json` converters for the new
  unions/enums will not conflict with a source-gen context.
- Status/kind/mode strings are centralized in static constant classes in several files
  (good: single source of truth per concept; bad: not type-checked). Examples confirmed:
  `DeckEditPlanStatus` (`DeckPlanModels.cs:261`), `DeckEditOperations`
  (`DeckPlanModels.cs:292`), `CorpusSourceStatuses` (`CorpusModels.cs:53`),
  `WorkspaceDiffLastImportStatuses` / `WorkspaceRefreshFromSourceStatuses`
  (`DeckTuningWorkflowModels.cs:916,993`), `CorpusCacheModes` (`Options.cs:176`).
- Critical constraint: workspaces and plans persist as JSON (`JsonDeckWorkspaceRepository`,
  `JsonDeckPlanRepository`) and many of these strings are written to disk and to the MCP
  surface. Any enum/union migration MUST round-trip the existing string values (existing
  saved workspaces/plans on user machines, and the `McpSurfaceTests` snapshot).

## 4. Workstreams

PR slicing (important): do **not** combine union introduction, status-enum conversion, and
model-file moves in one PR - file moves on top of converters and unions make regressions
hard to review. Sequence as separate PRs:

1. PR 1 (first slice): `DeckEditOperation` union + JSON converters + on-disk fixtures only
   (4.1). This is the riskiest serialization change; land and stabilize it alone.
2. PR 2: outcome unions (4.2).
3. PR 3: status/kind/mode enums with wire-preserving converters (4.3).
4. PR 4 (separate, last): domain vs response DTO file separation (4.4) - pure moves, no
   logic/serialization changes, easy to review in isolation.

### 4.1 Edit-operation union (highest value, PR 1)
- Replace `DeckEditOperation` (string `Operation` + 12 nullable fields) with a union of
  cases: e.g. `AddCard`, `RemoveCard`, `SetQuantity`, `MoveCard`, `ReplaceCard`,
  `SetCategories` (confirm the full set from `DeckEditOperations` constants and the apply
  switch in `DeckPlanService.Apply.cs`).
- Make plan apply/preview dispatch an exhaustive `switch` over the union so new cases are
  compile-time work. Remove the runtime `_ => throw` default.
- Provide JSON converters so persisted plans keep the same shape and existing plan files
  still deserialize. **Decision: use the flattened `type`-tag representation** (a `type`
  discriminator field alongside the existing fields) rather than a nested
  discriminator+payload object, because it matches the current flat plan JSON and minimizes
  migration. Add round-trip tests with fixtures of the current on-disk format.

### 4.2 Outcome unions
- Promote the existing private `DeckEditPlanApplyAttemptResult` union (or an equivalent)
  to the public result boundary where it improves callers, or keep it internal but ensure
  the public `DeckEditPlanApplyResult` is consumed via typed status (4.3) rather than
  `bool Success` + string.
- Identify other discriminated outcomes that warrant unions: refresh-from-source result
  (status cases carry different fields), diff-last-import result, source status.

### 4.3 Status/kind/mode enums
- Convert closed-set strings to enums with `[JsonStringEnumConverter]` (or explicit
  converters) that serialize to the existing exact strings (e.g.
  `draft|applied|failed|partially-applied|apply-state-unknown`,
  `available|missing-config|disabled|failed|needs-oauth|access-blocked`,
  `persisted|memory|off`, severity `warning|error|info`).
- Keep one converter policy in the shared `JsonSerializerOptions` so wire/disk values are
  unchanged. Add a test asserting each enum serializes to its legacy string.
- Make `MtgMcpOptions.OperationMode` an enum-backed value (still accepting the documented
  aliases at the config boundary, mapping in `OperationModeGuard.Normalize`).

### 4.4 Domain vs response DTO separation (separate, last PR - moves only)
- Do this only after 4.1-4.3 have landed and stabilized. It is pure file/namespace
  reorganization with no serialization or logic changes, so it reviews cleanly on its own.
- Split `Models/WorkspaceModels.cs` and `Models/DeckTuningWorkflowModels.cs` into
  `Models/Domain/*` (entities: `DeckWorkspace`, `DeckCard`, `DeckCategory`,
  `WorkspaceCheckpoint`, source/import history) and `Models/Responses/*` (tool-output
  shapes). Entities remain the single source of truth; response DTOs are projections.
- Consider value objects for `ColorIdentity` and mana value if it reduces stringly-typed
  handling, but keep scope contained (do not rewrite all call sites in this phase).

## 5. Files to create / change

- Change: `Plans/DeckPlanModels.cs` (+ union cases, converters), `Plans/DeckPlanService.Apply.cs`
  / `Preview.cs` (exhaustive switch), `Recommendations/CorpusModels.cs`,
  `Models/DeckTuningWorkflowModels.cs`, `Models/WorkspaceModels.cs`, `Options.cs`,
  `Hosting/OperationModeGuard.cs`, the shared serializer options.
- Create: `Models/Domain/*`, `Models/Responses/*`, JSON converter types, fixture-backed
  round-trip tests under `tests/MtgMcp.Core.Tests/`.

## 6. Testing

- Round-trip tests: deserialize fixtures of current on-disk workspace/plan JSON; serialize
  new enums/unions back to the exact legacy strings.
- Exhaustiveness: a compile-time guarantee (no default arm) plus a test that every
  `DeckEditOperations` case is handled.
- `McpSurfaceTests` snapshot unchanged for wire strings (proves no surface drift).

## 7. Definition of done

- `DeckEditOperation` god-DTO replaced by a union; apply/preview switch is exhaustive with
  no runtime default.
- Closed-set strings are enums/unions serializing to identical wire/disk values.
- Domain entities and response DTOs live in separate files/namespaces.
- Existing saved workspaces/plans still load; surface snapshot stable.

## 8. Risks & mitigations

- Risk (high): breaking on-disk or wire compatibility. Mitigation: converter tests against
  real fixtures; never change a serialized string; treat this as the gating acceptance
  criterion.
- Risk: union ergonomics with System.Text.Json. Mitigation: custom converters with a
  discriminator; prototype on the edit-operation union before broad rollout.
- Risk: scope creep into service refactors. Mitigation: types only; defer structure to
  Phase 5.

## 9. Open questions

- Union representation for persistence is decided (4.1): flattened `type` tag matching the
  current flat plan JSON. Residual: the exact `type` token strings per case (must equal the
  existing `DeckEditOperations` constants).
- How far to take value objects (color identity, mana value) without spilling into Phase 5.
