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

Docs-only changes follow the validation guidance in `AGENTS.md` and do not need
a .NET build.

Keep future adapters isolated from Core domain logic. Normal tests must remain
offline and must not mutate provider state.

Public MCP surface changes must update `README.md`, `CHANGELOG.md`, and surface
tests. During the clean-break rewrite, the approved active PLC defines the
surface and compatibility boundary; otherwise follow `docs/versioning.md`.

## Evidence-First Rewrite

Read [`docs/rewrite-guide.md`](docs/rewrite-guide.md) before rewrite work. The
legacy implementation remains reference evidence in Git history, but its
workspace, plan, recommendation, intent, scoring, prompt, simulation, and
provider abstractions are not the rewrite foundation.

Do not implement a rewrite child until its independent review is recorded, its
README says `Implementation authorized: Yes`, and it has moved to
`in-progress/`. The approved umbrella and active child packet then replace the
ordinary pre-1.0 compatibility policy for that clean-break scope. Docs-only
guidance changes require `git diff --check` and link/Markdown inspection, not a
.NET build.
