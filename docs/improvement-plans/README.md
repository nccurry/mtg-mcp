# Improvement Plans

Per-phase implementation plans for the roadmap in
[`../../IMPROVEMENT_PLAN.md`](../../IMPROVEMENT_PLAN.md).

Each file is a detailed, code-grounded plan for one phase. They are meant to be
handed to implementing agents/contributors. The master plan owns sequencing,
the problem inventory, and the guiding principles; these files own the "how."

| Phase | Plan | Theme |
|---|---|---|
| 0 | [phase-00-baseline.md](phase-00-baseline.md) | Baseline metrics, guardrails, doc reconciliation, honesty quick wins |
| 1 | [phase-01-surface-consolidation.md](phase-01-surface-consolidation.md) | Tool surface reduction, toolsets, mode-aware advertising |
| 2 | [phase-02-api-ux-unification.md](phase-02-api-ux-unification.md) | Unify `detailLevel`, identifiers, result/error framing |
| 3 | [phase-03-mcp-protocol-conformance.md](phase-03-mcp-protocol-conformance.md) | Structured output/errors, pagination, resource discovery |
| 4 | [phase-04-domain-typing.md](phase-04-domain-typing.md) | Union types, enums, domain vs DTO separation |
| 5 | [phase-05-service-decomposition.md](phase-05-service-decomposition.md) | Break god services, retire fat base, dedupe repositories |
| 6 | [phase-06-adapter-hardening.md](phase-06-adapter-hardening.md) | Shared resiliency, error model, secret redaction, auth |
| 7 | [phase-07-analytical-depth.md](phase-07-analytical-depth.md) | Card evaluation, bracket, combos, determinism |
| 8 | [phase-08-new-capabilities.md](phase-08-new-capabilities.md) | Collection/ownership, batch lookup, images, pricing |
| 9 | [phase-09-observability-release.md](phase-09-observability-release.md) | Logging/metrics, client matrix, perf, 1.0 release |

## Implementation order and PR granularity

Phases are sequenced in the master plan, but several are explicitly expected to land as
*multiple* PRs rather than one:

- Phase 4 - first PR is `DeckEditOperation` union + JSON converters + fixtures only;
  domain/response model separation is a later, separate PR.
- Phase 5 - split into ordered sub-PRs: `JsonFileStore<T>` -> `DeckServiceBase` slimming ->
  recommendation extraction (one focused service per PR) -> optional goldfish/namespace
  work. Each sub-PR must show no analytical snapshot drift.
- Phase 6 - secret-redaction hardening and Archidekt JWT refresh ship as their own early
  PRs before the broad resiliency work.
- Phase 7 - land evaluator roles incrementally (ramp -> draw -> interaction -> ...), and
  treat the bracket-model change as its own PR gated on agreed benchmark expectations.
- Phase 8 - batch lookup and the image affordance shipped first (cheap, expose existing
  data); the collection subsystem followed as an ADR-gated local persistence slice.

Phases 1-3 are sequential and contain breaking surface changes; honor the deprecation
windows from Phase 0's `docs/versioning.md` and the per-phase "deprecation release" vs
"removal release" targets.

## Release train (single source of truth for versions)

This table is authoritative for version targets; per-phase `Target version` metadata must
match it. Where two phases share a minor, they touch different surfaces and land as ordered
PRs within that minor (surface-phase PRs before others). Phase 6 (adapters) is an
independent track that rides alongside the surface train.

| Version | Phase(s) | Headline change |
|---|---|---|
| 0.8.0 | 0 | Surface metrics (report-only), deprecation policy + ADRs, doc reconciliation, honesty fixes (`RngKind`, evaluate-card status) |
| 0.9.0 | 1 (deprecation) | Consolidated + toolset surface advertised; mode-aware advertising; old tool names deprecated |
| 0.10.0 | 1 (removal) + 2 (deprecation) | Remove deprecated tool names; unified `detailLevel` in place; `includeWorkspace`/`compact` deprecated |
| 0.11.0 | 2 (removal) + 6a | Remove `includeWorkspace`/`compact`; (parallel) secret-redaction hardening + Archidekt JWT refresh |
| 0.12.0 | 3 | Structured output + structured errors + pagination + resource discovery; minimal in-proc client smoke |
| 0.13.0 | 4 + 6b | Domain unions/enums (model separation as the last PR); (parallel) shared adapter resiliency + error model |
| 0.14.0 | 5 | Core service decomposition (ordered sub-PRs) |
| 0.15.0 | 7 | Analytical depth: evaluator roles (draw/interaction), density bracket, determinism unification |
| 0.16.0 | 8 | Batch lookup/image/pricing (Track 1); collection subsystem (Track 2) |
| 1.0.0 | 9 | Observability, client matrix, perf ratchet, deprecation completion, release |

Guiding principles and the problem inventory live in the master
[`IMPROVEMENT_PLAN.md`](../../IMPROVEMENT_PLAN.md) - this directory does not restate them.

## Shared facts for implementers

- Target framework: `net11.0`, `LangVersion=preview`, `Nullable=enable`,
  `TreatWarningsAsErrors=true`, central package management
  (`Directory.Build.props`, `Directory.Packages.props`).
- MCP SDK: `ModelContextProtocol` (currently `1.4.0`). The server uses method-level
  MCP tool registration with titles/output schemas/structured content, selective
  `WithTools(types)` registration, request filters, and the MCP logging-level
  handler. Still-relevant SDK capabilities for future phases include
  `WithListToolsHandler` / `WithCallToolHandler` for fully dynamic surfaces,
  `WithMessageFilters`, `IconSource`, and `McpServerPrimitiveCollection<T>.Changed`
  for list-changed notifications.
- Build/test workflows live in `Taskfile.yml`; CI is `.github/workflows/ci.yml`
  (runs `task lint`, coverage gates at 85%, `task smoke:mcp`, pack + archive smoke).
- The public surface is pinned by a snapshot test:
  `tests/MtgMcp.App.Tests/Tools/McpSurfaceTests.cs` (update it whenever the surface
  changes). Boundaries are pinned by `tests/MtgMcp.Architecture.Tests/`.
