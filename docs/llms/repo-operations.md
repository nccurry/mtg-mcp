# Repository Operations

`mtg-mcp` follows a Task-based workflow. Every common operation should be
exposed through `Taskfile.yml`.

## Rules

- Use `task --list` to discover supported commands.
- Document Task commands, not raw shell command sequences, when Task already
  has an equivalent.
- Add or reuse a Task command before documenting a common new repo operation.
- Keep generated output under `artifacts/`, `bin/`, `obj/`, `coverage/`, or
  package output paths.
- Run one top-level Task invocation at a time per worktree.

## Common Tasks

```bash
task setup
task restore
task deps:check
task build
task lint
task test
task test:unit
task test:integration
task test:e2e
task benchmark:oncurve
task coverage
task smoke:process
task smoke:mcp
task ci
task pack
task clean
```

`task test` runs non-live, non-benchmark tests. Use `task benchmark:oncurve`
for the separate fixed Release measurement. Use `task test:live` only when live
provider validation is explicitly requested and safe.

`task restore` verifies the committed NuGet graph. After an approved package
change, use `task restore:lock` to refresh that graph and `task tools:lock` to
refresh the committed Mise download records. Review both lock-file changes.

## Adding A Task

When adding a task:

1. Put shared paths and flags in `vars`.
2. Give the task a clear `desc`.
3. Keep platform-specific commands under `platforms`.
4. Prefer artifact output under `artifacts/`.
5. Update docs when it becomes a supported workflow.
