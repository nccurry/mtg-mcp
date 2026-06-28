# 1.0 Readiness

Current status: not ready to tag `1.0.0`.

This checklist gates the `1.0.0` release. Keep it synchronized with the release
train in [`docs/improvement-plans/README.md`](improvement-plans/README.md) and
with the public compatibility policy in [`docs/versioning.md`](versioning.md).

## Checklist

| Area | Status | Evidence or blocker |
| --- | --- | --- |
| Phase 1 surface consolidation | Partial | Toolsets and mode-aware advertising exist, but the surface still reports more than 100 tools. The final 1.0 ceiling/removal set is not complete. |
| Phase 2 API UX unification | Partial | `detailLevel` is unified, but compatibility inputs such as `compact` and `includeWorkspace` remain documented during the deprecation window. |
| Phase 3 protocol conformance | Mostly complete | Structured output, output schemas, structured errors, pagination, resource discovery, and E2E smoke exist. Keep client matrix coverage green as SDKs move. |
| Phase 4 domain typing | Partial | Typed edit operations and several status enums are in place. Continue to verify legacy JSON compatibility before release. |
| Phase 5 service decomposition | Partial | Shared JSON stores and service slimming have landed, but decomposition remains an incremental track rather than a closed release gate. |
| Phase 6 adapter hardening | Partial | Redaction and local adapter tests exist. Registry/release checks still need a final pass with release artifacts. |
| Phase 7 analytical depth | Complete for planned slice | Draw/interaction evaluation, density-aware bracket, deterministic metadata, and local combo fallback are covered by tests. |
| Phase 8 new capabilities | Complete for planned slice | Batch lookup, image lookup, price source port, and local collection ownership diffs are implemented and documented. |
| Phase 9 observability | Partial | Tool-call logs, `Meter`, `ActivitySource`, MCP `logging/setLevel`, and report-only `task perf:report` are implemented. Source-fetch metrics and broader client matrix are not complete. |
| Version metadata | Blocked | `server.json` and `src/MtgMcp.App/MtgMcp.App.csproj` still declare `0.8.0`; do not tag `1.0.0` until versions and changelog are finalized. |
| Deprecation removals | Blocked | 0.x compatibility shims must be audited and removed on schedule before `1.0.0`. |
| MCP Registry validation | Pending | The release workflow validates and publishes registry metadata, but this branch has not completed final registry validation for `1.0.0`. |
| Client compatibility matrix | Partial | Current checked path is the .NET MCP SDK over stdio. Add and verify any other supported clients before `1.0.0`. |
| Release verification | Pending | `task release:verify VERSION=1.0.0` must pass on the exact tagged source state. |
| Local install smoke | Pending | Install the exact release artifact locally and run `mtg-mcp --smoke` before announcing the release. |

## Release Rule

Do not create or push a `v1.0.0` tag until every checklist row is complete or
explicitly accepted as non-blocking in this document.
