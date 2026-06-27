# Phase 3 - MCP Protocol Conformance

| | |
|---|---|
| Effort | L |
| Risk | Medium |
| Depends on | Phase 2 (typed returns) |
| Unblocks | Phase 8 (new tools ship conformant from day one) |
| Target version | 0.12.0 |

Goal: make the server a first-class citizen for capable MCP clients - structured output
with output schemas, structured errors, paginable lists, and resource-based discovery.

## 1. Problems addressed

- **P6 - `object`/anonymous returns prevent output schemas.** ~30 tools (at least 28
  confirmed; ~33 `Task<object>` signatures across the tool classes) return `object` built
  from anonymous types (mutations, category edits, simulation/plan presenters,
  `deck_evaluate_card`, `deck_batch_tuning_report`, workspace start/refresh/validate,
  re-evaluation, facets, intent). The SDK defaults `UseStructuredContent=false`, so today no
  anonymous/object tool emits `structuredContent` or an `outputSchema`. Raw enumerable
  returns also cannot be used directly for MCP `structuredContent` because the protocol
  requires an object root.
- **P7 - errors are thrown exceptions.** All validation/guard failures throw
  `ArgumentException`/`InvalidOperationException` (e.g. `RecommendationTools.cs:289`,
  `OperationModeGuard.cs:57`). The SDK maps these to an error result with a prose message;
  there is no machine-readable error code/shape and no `McpException` usage.
- **P8 - no continuation for large persisted lists.** `workspace_list` and `deck_plan_list`
  are persisted local lists and need cursor continuation. Source/search tools stay bounded
  by `limit` until their provider adapters expose stable continuation tokens.
- **P9 - no enumerable workspace resource.** All workspace resources are parameterized
  templates (`MtgResources.cs:91-166`); resource-browsing clients cannot discover saved
  decks without a tool call.

## 2. Goals / non-goals

Goals:
- Structured content + generated `outputSchema` for the high-traffic tools.
- A structured, machine-readable tool-error model surfaced as MCP tool errors.
- Cursor/continuation for list-style results.
- A discoverable workspace listing via resources.
- Human-readable tool `Title`s and verified annotations on the consolidated surface.

Non-goals:
- New analytical capabilities (Phase 7/8). This phase is protocol shape only.

## 3. Current state (investigation)

- SDK levers available now (`ModelContextProtocol` 1.4.0):
  - `[McpServerTool].UseStructuredContent`, `.OutputSchemaType`, `.Title`, `.IconSource`.
    `McpServerToolCreateOptions.UseStructuredContent` / `.OutputSchema` are available for
    programmatic control; for method-level registry tools, assign `ProtocolTool.OutputSchema`
    after creation when the SDK overload does not copy the explicit schema.
  - `CallToolResult.StructuredContent` + `IsError`; `Tool.OutputSchema`.
  - `WithCallToolHandler` and `WithRequestFilters` for centralized result/error mapping.
  - `WithListResourcesHandler` / static resources for discovery; protocol pagination
    (`cursor`) for primitive listings.
- Tool exceptions were plain BCL exceptions; messages are user-facing prose. Phase 3 adds an
  App-boundary mapper for validation, operation-mode-blocked, and conflict errors. Provider
  auth/unavailable/rate-limit taxonomy expands in Phase 6 with adapter outcomes.
- Presenters (`GoldfishOutputPresenter`, `PerformanceOutputPresenter`,
  `PlanPreviewPresenter`, `CompactMutationPresenter`, `DeckNormalizationPresenter`,
  `CardFacetOutputPresenter`) emit anonymous objects, so typed tool signatures alone are
  insufficient - presenter outputs must become typed records too.

## 4. Workstreams

### 4.1 Structured output + output schemas
- Convert anonymous/`object` returns into typed result records (coordinated with Phase 2's
  envelope). Each presenter returns a concrete `record`/`sealed class` per detail level, or
  one type with nullable sections, so the SDK can derive a schema.
- Enable `UseStructuredContent` per object-root typed tool. Skip `object`, `string`,
  `CallToolResult`, and raw enumerable returns until they have typed object-root envelopes.
  Verify `structuredContent` + `outputSchema` appear for converted tools.
- Where a tool genuinely returns heterogeneous shapes by `detailLevel`, prefer one stable
  superset type with optional members over `object`, so the schema stays meaningful.
- **Ship an in-phase client smoke with the structured-content flip.** Enabling
  `UseStructuredContent` is a big-bang payload change; do not ship it with no client test
  until Phase 9. This phase delivers a minimal in-proc SDK-client smoke (initialize ->
  `tools/list` -> one `tools/call` returning `structuredContent` + a non-empty `Content`)
  as its own test deliverable. The full multi-client matrix stays in Phase 9.

### 4.2 Structured error model
- Define an error taxonomy. Phase 3 ships `validation`, `operation-mode-blocked`, and
  `conflict`; Phase 6 adds provider-specific `provider-auth`, `provider-unavailable`, and
  `rate-limited` once adapters share typed outcomes.
- **Decision: where errors live.** The error *taxonomy DTO is an App/MCP-boundary type*,
  not a Core type - this preserves Core's independence from MCP (the repo rule that Core
  must not reference adapter/host concerns). Core (and adapters) raise typed *domain*
  exceptions/outcomes (e.g. `WorkspaceNotFoundException`, `OperationModeBlockedException`,
  provider failure outcomes from Phase 6); a single App-level mapping filter translates
  them into the MCP error DTO. Do not leak MCP error shapes into Core.
- Centralize mapping: a `WithCallToolHandler`/request filter that catches the known
  domain exception types and returns a structured tool error (`IsError=true`) with the
  taxonomy payload, instead of letting raw exceptions stringify. Keep messages secret-free
  (route through `SecretRedactor`).
- Make `OperationModeGuard` failures a first-class `operation-mode-blocked` error with the
  required mode in the payload (it already has good prose at `OperationModeGuard.cs:57-66`).
- Example structured-content payload for a blocked mutate call (the `structuredContent`
  carried alongside `IsError=true`):

  ```json
  {
    "error": {
      "code": "operation-mode-blocked",
      "message": "Tool 'deck_add_card' would modify deck state but the server is in 'plan' mode.",
      "retriable": false,
      "details": { "tool": "deck_add_card", "currentMode": "plan", "requiredMode": "apply" },
      "hint": "Restart the server with MTGMCP__OPERATION_MODE=apply to allow this tool."
    }
  }
  ```

### 4.3 Pagination / continuation
- Add an opt-in continuation pattern to persisted list-style tool results: accept `cursor`
  and return `items`, `nextCursor`, `limit`, and `totalCount` for `workspace_list` and
  `deck_plan_list`.
- Keep Archidekt's existing `page`/`pageSize` contract until Phase 6 normalizes adapter
  paging; do not invent opaque cursors without provider-backed continuation semantics.

### 4.4 Resource discovery
- Add an enumerable workspace resource (e.g. static `mtg://workspaces` returning id/name/
  format/updatedAt rows, or a `WithListResourcesHandler` that lists current workspaces) so
  clients can browse saved decks. Reuse `DeckWorkspaceService` listing.
- Review resource templates for completeness and ensure `resources/templates/list`
  advertises the parameterized ones.

### 4.5 Titles, icons, annotation re-verification
- Set human-readable tool titles through the method-level registry (cheap UX win for
  clients that render titles). Optionally set `IconSource` later.
- Re-verify `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld` on the consolidated surface
  (Phase 1 may have merged tools with mixed semantics - a merged tool that can mutate must
  not be `ReadOnly`).

## 5. Files to create / change

- Create: `src/MtgMcp.App/Hosting/McpErrorMapping.cs` (filter + App-boundary taxonomy),
  `src/MtgMcp.App/Tools/PagedToolResult.cs`, `mtg://workspaces` resource.
- Change: `Hosting/MtgMcpHost.cs` (register error filter), `ToolRegistry.cs` (titles,
  structured-content selection, output schemas), list tools (`workspace_list`,
  `deck_plan_list`), `OperationModeGuard` (typed blocked-mode exception),
  `McpSurfaceTests.cs` and E2E tests (schemas/titles/errors/paging/resource discovery).
- Follow-up with presenter result records for the remaining `object` tools when those models
  are touched by Phase 4/7 work.

## 6. Testing

- E2E (`tests/MtgMcp.E2E.Tests`): assert a representative object-root typed tool returns
  `structuredContent` with an `outputSchema`; assert error cases return `IsError` with the
  taxonomy payload; assert paged list envelopes and `mtg://workspaces` discovery.
- **Backward-compat test for structured content:** after enabling `UseStructuredContent`,
  assert that each converted tool still returns usable human-readable text content in
  `CallToolResult.Content` (structured content is additive, not a replacement). This guards
  clients that only read text content.
- Snapshot test extended to cover `outputSchema` presence and `Title`s.
- Keep everything offline (mocked transport).

## 7. Definition of done

- Object-root typed tools expose `structuredContent` + `outputSchema`; raw enumerable and
  anonymous/object presenter tools are explicitly skipped until they gain typed envelopes.
- Validation, operation-mode, and conflict tool errors are structured, coded, secret-free,
  and tested.
- Persisted local list tools support cursor continuation.
- Saved workspaces are discoverable via a resource.
- Titles set; annotations re-verified on the consolidated surface.

## 8. Risks & mitigations

- Risk: enabling structured content changes payload shape for existing clients.
  Mitigation: enable it only for object-root typed tools, keep text content, validate with
  the in-phase E2E smoke (4.1), run the full client matrix later (Phase 9), and changelog
  the change.
- Risk: typed supersets for multi-detail-level tools get awkward. Mitigation: prefer a
  small number of detail-specific record types over one bloated type when nullability
  would dominate.
- Risk: centralized error mapping hides stack info needed for debugging. Mitigation: log
  full detail server-side (Phase 9), return only safe taxonomy to clients.

## 9. Open questions

- Structured content is per object-root typed tool; raw enumerable and `object` tools stay
  text-only until their return contracts are shaped.
- Continuation uses opaque cursor strings in the public contract.
- Error location is decided (4.2): App-boundary MCP error DTO + App/Core domain exceptions
  mapped in one filter. Residual Phase 6 detail: exact provider outcome types.
