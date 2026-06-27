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
  tool emits `structuredContent` or an `outputSchema`.
- **P7 - errors are thrown exceptions.** All validation/guard failures throw
  `ArgumentException`/`InvalidOperationException` (e.g. `RecommendationTools.cs:289`,
  `OperationModeGuard.cs:57`). The SDK maps these to an error result with a prose message;
  there is no machine-readable error code/shape and no `McpException` usage.
- **P8 - no continuation for large lists.** `workspace_list`, `deck_plan_list`, and
  source/search tools bound with `limit` only.
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
  - `[McpServerTool].UseStructuredContent`, `.OutputSchemaType`, `.Title`, `.IconSource`
    (all currently unset). `McpServerToolCreateOptions.UseStructuredContent` /
    `.OutputSchema` for programmatic control.
  - `CallToolResult.StructuredContent` + `IsError`; `Tool.OutputSchema`.
  - `WithCallToolHandler` and `WithRequestFilters` for centralized result/error mapping.
  - `WithListResourcesHandler` / static resources for discovery; protocol pagination
    (`cursor`) for primitive listings.
- All tool exceptions are plain BCL exceptions; messages are user-facing prose. Some are
  already careful (redaction in adapters), but there is no error taxonomy.
- Presenters (`GoldfishOutputPresenter`, `PerformanceOutputPresenter`,
  `PlanPreviewPresenter`, `CompactMutationPresenter`, `DeckNormalizationPresenter`,
  `CardFacetOutputPresenter`) emit anonymous objects, so typed tool signatures alone are
  insufficient - presenter outputs must become typed records too.

## 4. Workstreams

### 4.1 Structured output + output schemas
- Convert anonymous/`object` returns into typed result records (coordinated with Phase 2's
  envelope). Each presenter returns a concrete `record`/`sealed class` per detail level, or
  one type with nullable sections, so the SDK can derive a schema.
- Enable `UseStructuredContent` (globally via the server's tool create options, or
  per-tool) and verify `structuredContent` + `outputSchema` appear for converted tools.
- Where a tool genuinely returns heterogeneous shapes by `detailLevel`, prefer one stable
  superset type with optional members over `object`, so the schema stays meaningful.
- **Ship an in-phase client smoke with the structured-content flip.** Enabling
  `UseStructuredContent` is a big-bang payload change; do not ship it with no client test
  until Phase 9. This phase delivers a minimal in-proc SDK-client smoke (initialize ->
  `tools/list` -> one `tools/call` returning `structuredContent` + a non-empty `Content`)
  as its own test deliverable. The full multi-client matrix stays in Phase 9.

### 4.2 Structured error model
- Define an error taxonomy (e.g. `validation`, `not-found`, `operation-mode-blocked`,
  `provider-auth`, `provider-unavailable`, `rate-limited`, `conflict`) with a small typed
  payload (code, message, retriable, hint).
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
- Add an opt-in continuation pattern to list-style tool results: accept `cursor`
  (or `offset`) and return `nextCursor` alongside bounded rows for `workspace_list`,
  `deck_plan_list`, and source/search results. Keep `limit` as the page size.
- For Archidekt listing, align the existing `page`/`pageSize` with the shared pattern.

### 4.4 Resource discovery
- Add an enumerable workspace resource (e.g. static `mtg://workspaces` returning id/name/
  format/updatedAt rows, or a `WithListResourcesHandler` that lists current workspaces) so
  clients can browse saved decks. Reuse `DeckWorkspaceService` listing.
- Review resource templates for completeness and ensure `resources/templates/list`
  advertises the parameterized ones.

### 4.5 Titles, icons, annotation re-verification
- Set human-readable `[McpServerTool].Title` for each tool (cheap UX win for clients that
  render titles). Optionally set `IconSource`.
- Re-verify `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld` on the consolidated surface
  (Phase 1 may have merged tools with mixed semantics - a merged tool that can mutate must
  not be `ReadOnly`).

## 5. Files to create / change

- Create: `src/MtgMcp.App/Hosting/McpErrorMapping.cs` (filter/handler + taxonomy),
  `src/MtgMcp.Core/.../McpToolError.cs` (or App-level error DTO),
  result record types per presenter, `mtg://workspaces` resource.
- Change: `Hosting/MtgMcpHost.cs` (enable structured content, register error filter,
  set tool create options), all `object`-returning tools + presenters, list tools
  (`workspace_list`, `deck_plan_list`, source tools), `OperationModeGuard` (typed error),
  `McpSurfaceTests.cs` (schemas/titles now part of the surface).

## 6. Testing

- E2E (`tests/MtgMcp.E2E.Tests`): assert representative tools return `structuredContent`
  with an `outputSchema`; assert error cases return `IsError` with the taxonomy payload
  (matching the example in 4.2); assert pagination round-trips with `nextCursor`.
- **Backward-compat test for structured content:** after enabling `UseStructuredContent`,
  assert that each converted tool still returns usable human-readable text content in
  `CallToolResult.Content` (structured content is additive, not a replacement). This guards
  clients that only read text content.
- Snapshot test extended to cover `outputSchema` presence and `Title`s.
- Keep everything offline (mocked transport).

## 7. Definition of done

- High-traffic tools expose `structuredContent` + `outputSchema`.
- Tool errors are structured, coded, secret-free, and tested.
- List tools support cursor continuation; Archidekt listing aligned.
- Saved workspaces are discoverable via a resource.
- Titles set; annotations re-verified on the consolidated surface.

## 8. Risks & mitigations

- Risk: enabling structured content changes payload shape for existing clients.
  Mitigation: structured content is additive (text content remains); validate with the
  in-phase minimal client smoke (4.1) when the flip ships, the full client matrix later
  (Phase 9), and changelog the change.
- Risk: typed supersets for multi-detail-level tools get awkward. Mitigation: prefer a
  small number of detail-specific record types over one bloated type when nullability
  would dominate.
- Risk: centralized error mapping hides stack info needed for debugging. Mitigation: log
  full detail server-side (Phase 9), return only safe taxonomy to clients.

## 9. Open questions

- Enable `UseStructuredContent` globally or per-tool? (Recommend global default with
  per-tool opt-out for any tool that must stay text-only.)
- Continuation as opaque `cursor` vs numeric `offset`? (Recommend opaque cursor to allow
  future backing changes.)
- Error location is decided (4.2): App-boundary MCP error DTO + Core domain exceptions
  mapped in one filter. Residual detail: exact set of domain exception types to introduce
  in Core vs reuse of existing exceptions.
