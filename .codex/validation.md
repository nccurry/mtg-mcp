# Validation Recipes

Run commands from the repository root. `Taskfile.yml` is the command source of
truth; this file helps choose the right command.

## Common Checks

On fresh or drifted checkouts, run the platform bootstrap first:

```bash
./bootstrap.sh
```

```powershell
.\bootstrap.ps1
```

Use focused checks first while developing:

```bash
task test:unit
task test:integration
task test:e2e
task lint
task test
task ci
```

## Command Chooser

- Tiny docs-only guidance changes: inspect source-of-truth order and run
  `git diff --check`.
- Core behavior: run `task test:unit`, then broader checks as risk warrants.
- Fixture-backed adapter behavior: run `task test:integration`.
- MCP process, tool, resource, prompt, or operation-mode behavior: run
  `task test:e2e` or the affected app test project, then `task smoke:mcp` when
  service registration risk changes.
- Project-boundary or dependency changes: run the architecture tests and
  `task lint`.
- Shared behavior, public APIs, or public MCP shape: run the narrow check, then
  `task lint` and `task test` as risk warrants.
- Stats Lab, simulation, calibration, hot-path, or allocation-sensitive changes:
  run the narrow tests, `task bench:dry`, and the targeted benchmark or
  calibration task when performance evidence is part of the risk.
- Release, package, installer, or config changes: use the relevant Task command
  and record skipped checks with reasons.
- Rewrite child changes: run the active child's traceability/acceptance checks
  in addition to affected tests; surface changes reconcile the child matrix and
  cutover manifest without preserving legacy counts.

## Live Providers

`task test` must stay offline. Use `task test:live` only when live-provider
validation is explicitly requested and safe. Never mutate a real Archidekt deck
from normal tests.

## Noisy Gates

For broad gates, write output to an ignored log:

```bash
mkdir -p artifacts/agent-logs
task ci > artifacts/agent-logs/ci.log 2>&1
tail -n 200 artifacts/agent-logs/ci.log
rg -n "error|failed|FAILED|exception" artifacts/agent-logs/ci.log
```

## Handoff Format

Final handoff should include commands run, commands skipped with reasons,
failing command summaries and log paths when any fail, changed risk areas, and
whether live-provider or benchmark validation was required.
