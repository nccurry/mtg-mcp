# Contributing

For executable changes, run the risk-appropriate build and test tasks before
sending changes:

```bash
task build
task test
```

From a fresh checkout, bootstrap local prerequisites first:

```bash
./bootstrap.sh
```

On Windows, use `.\bootstrap.ps1`.

The server requires MCP `2026-07-28`. Do not add a fallback for older
initialize-based clients. `task restore` verifies the committed package graph;
use `task restore:lock` only after an approved package-version change.

Docs-only changes follow the validation guidance in `AGENTS.md` and do not need
a .NET build.

Keep future adapters isolated from Core domain logic. Normal tests must remain
offline and must not mutate provider state.

Public MCP tool or API changes must update `README.md`, `CHANGELOG.md`, and the
related tests. The clean-break rewrite is complete; use its completed records as
design evidence and follow `docs/versioning.md` for current-server changes.

## Evidence-First Rewrite

Read [`docs/rewrite-guide.md`](docs/rewrite-guide.md) before work that touches
the released `0.9.0` boundaries. The legacy implementation remains reference
evidence in Git history, but its workspace, plan, recommendation, intent,
scoring, prompt, simulation, and provider abstractions are not the foundation.

Do not treat a completed rewrite child as authorization for new work. Create a
new PLC when a change spans several modules or public APIs, changes a provider
contract or storage, or needs phases. Docs-only guidance changes require `git diff
--check` and link/Markdown inspection, not a .NET build.
