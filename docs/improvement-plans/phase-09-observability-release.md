# Phase 9 - Observability, Testing, Performance, and Release Hardening

| | |
|---|---|
| Effort | M-L |
| Risk | Low-Medium |
| Depends on | runs throughout; finalized after Phases 1-3 land |
| Unblocks | a confident 1.0 |
| Target version | 1.0.0 |

Goal: operational maturity - observability, a client-compatibility safety net, performance
budgets, and a clean 1.0 with a documented support policy.

## 1. Problems addressed

- **P28 - no structured logging/metrics; client compatibility untested.** Logging is a
  single console-to-stderr provider (`Hosting/MtgMcpHost.cs:79-80`) with no per-tool
  timing/metrics; there is no test that the server advertises/lists/calls correctly under
  representative MCP clients.
- Cross-cutting: complete the deprecation windows opened in Phases 1-3 and ship 1.0.

## 2. Goals / non-goals

Goals:
- Structured, stdio-safe logging with per-tool timing and source-fetch metrics.
- An MCP client-compatibility matrix exercised in CI.
- Expanded E2E + adapter contract + calibration coverage as gates.
- Performance budgets for hot paths.
- Versioning/deprecation execution and a documented 1.0 support policy.

Non-goals:
- No new product features (that is Phase 8). This phase hardens and ships.

## 3. Current state (investigation)

- Logging: `builder.Logging.ClearProviders(); AddConsole(LogToStandardErrorThreshold=Trace)`
  (`MtgMcpHost.cs`) remains correct for stdio because logs go to stderr.
- Done in the first Phase 9 slice: `Hosting/McpObservability.cs` adds a
  call-tool request filter with redacted per-tool completion logs, an
  `ActivitySource`, a `Meter`, tool-call count/duration instruments, and an MCP
  `logging/setLevel` handler. `server_get_info` / `mtg://server/info` now expose
  the current `mcpLoggingLevel`.
- Remaining: source-fetch metrics, broader client compatibility matrix, perf
  ratchet reporting, and final release/version execution.
- CI (`.github/workflows/ci.yml`) already runs `task lint`, CodeQL, gitleaks, coverage
  report + 85% gates, `task smoke:mcp`, pack, release archives, and tool-smoke. Release is
  `.github/workflows/release.yml`. Benchmarks exist (`tests/MtgMcp.Benchmarks`,
  BenchmarkDotNet; `task bench*`). Calibration exists (`task calibrate:stats-lab`).
- `server_get_info` / `mtg://server/info` / `ServerInfoService` already expose version/git;
  reuse for diagnostics.

## 4. Workstreams

### 4.1 Structured logging + metrics
- Done: add a request filter that logs tool name, duration, success/error taxonomy,
  and detail level per call. It logs no arguments and keeps everything on stderr to
  preserve stdio framing.
- Done: add `System.Diagnostics` `Meter`/`ActivitySource` tool-call telemetry so
  external collectors can attach without a hard dependency.
- Remaining: add source-fetch latency and cache hit/miss metrics once the adapter
  boundary has a shared instrumentation point.
- Done: wire the MCP `logging` capability (`WithSetLoggingLevelHandler`) so clients can
  set the host diagnostic level.
- Extend the Phase 0 `--surface-report`/metrics into a runtime diagnostics view if useful.

### 4.2 Client-compatibility matrix
- Builds on the **minimal in-proc client smoke delivered in Phase 3** (which shipped with
  the structured-content flip so the risky change had a safety net at the time). Phase 9 is
  not the first time a client exercises the structured surface; it expands that smoke into
  the full multi-client / multi-version matrix.
- Add E2E tests (or a harness) that connect with the MCP client from the SDK and assert:
  initialize/capabilities, `tools/list` (counts per mode/toolset), a representative
  `tools/call` for each category returning structured content, `resources/list` +
  read, and `prompts/list` + get. Run across the supported SDK/protocol versions the
  project targets.
- Done for the current coverage: document the tested .NET SDK stdio path in
  `docs/compatibility.md`.
- Remaining: expand that table and CI coverage into the official multi-client /
  multi-version matrix before 1.0.

### 4.3 Expand test + calibration gates
- Promote the calibration suite (Phase 7) and adapter contract fixtures (Phase 6) to CI
  gates so analytical and provider regressions fail the build.
- Grow `tests/MtgMcp.E2E.Tests` to cover the consolidated surface and the structured
  output/error contracts from Phase 3.

### 4.4 Performance budgets
- Define budgets for hot paths (large-deck analysis, 50k-sim performance, source fan-out,
  combo analysis).
- **Start with ratcheted reporting, not hard gates.** Emit timings and compare against a
  recorded baseline as report-only output; only promote to a build-failing gate if/when CI
  hardware proves stable enough for low-variance timing. On shared/containerized CI runners,
  keep it report-only with a generous ratchet to avoid flaky failures. Use BenchmarkDotNet
  in-process (as today) for the measurements.

### 4.5 Versioning, deprecation execution, 1.0
- Complete deprecation windows opened in Phases 1-3 (remove deprecated tool names/params on
  schedule), per the Phase 0 `docs/versioning.md`.
- Finalize `CHANGELOG.md` (currently a stub) with the cumulative surface changes, write a
  1.0 support/compatibility policy, validate the MCP Registry entry (`server.json`), and
  cut 1.0 via the existing release workflow + `task release:verify`.
- **1.0 readiness checklist** (all must be true to tag 1.0; checked in as
  `docs/release-1.0-readiness.md`; audit "what shipped in which minor" against the release
  train table in `docs/improvement-plans/README.md`, the single source of truth, so
  "deprecations complete" is verifiable):
  - [ ] Phase 1 complete: tool count at/under ceiling, toolsets + mode-aware advertising
        shipped, consolidation removals done.
  - [ ] Phase 2 complete: unified `detailLevel`; `includeWorkspace`/`compact` removed
        (removal release done).
  - [ ] Phase 3 complete: structured output + structured errors + pagination + resource
        discovery shipped.
  - [ ] All Phase 1-3 deprecations removed on schedule (no lingering deprecated
        names/params).
  - [ ] MCP Registry entry (`server.json`) validated against the registry schema.
  - [ ] Docs current: `README.md` surface section, usage resources, `docs/versioning.md`,
        `docs/compatibility.md` all match the shipped surface (doc-parity test green).
  - [ ] Client-compatibility matrix green across the supported clients/SDK versions.
  - [ ] `CHANGELOG.md` finalized; `task release:verify` passes end to end.

## 5. Files to create / change

- Create: `src/MtgMcp.App/Hosting/McpObservability.cs` (+ metrics),
  `docs/observability.md`, `docs/compatibility.md`,
  `docs/release-1.0-readiness.md`, client-matrix E2E tests, perf-budget check.
- Change: `Hosting/MtgMcpHost.cs` (filters, logging-level handler, structured logging),
  `ServerInfoService.cs`/`ServerInfo.cs` (diagnostic level), `Taskfile.yml`/`ci.yml`
  (calibration + perf gates, client matrix), `CHANGELOG.md`, `README.md`,
  `docs/versioning.md`, `server.json` (1.0).

## 6. Testing

- Client-matrix E2E run in CI (offline, in-process transport).
- Calibration + adapter contract gates green.
- Perf budgets enforced (or reported with a ratchet).
- `task release:verify` passes end to end.

## 7. Definition of done

- Tool calls are observable (structured logs + timing/metrics); MCP logging capability
  works; no secrets in logs.
- A client-compatibility matrix runs in CI and is documented.
- Calibration, adapter contract, and E2E suites are CI gates; perf budgets reported with a
  ratchet (promoted to a hard gate only if CI hardware variance allows).
- The 1.0 readiness checklist (4.5) is fully green; deprecations completed; `CHANGELOG.md`
  accurate; 1.0 cut with a documented support policy and a valid registry entry.

## 8. Risks & mitigations

- Risk: logging breaks stdio framing. Mitigation: stderr only; test that stdout carries
  only protocol JSON (the smoke test can assert this).
- Risk: perf budgets flaky in CI containers. Mitigation: generous thresholds + ratchet;
  run benchmarks in-process as today.
- Risk: declaring 1.0 prematurely. Mitigation: gate 1.0 on Phases 1-3 done + client matrix
  green + deprecations complete.

## 9. Open questions

- Metrics export: logs-only vs `Meter`/OpenTelemetry-ready? (Recommend `Meter`/
  `ActivitySource` so collectors can attach without a hard dependency.)
- Which MCP clients/SDK versions form the official support matrix?
