# Repository Operations

`mtg-mcp` uses Task as its command entry point. Every common operation should
be available through `Taskfile.yml`.

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
task benchmark:simulation
task coverage
task smoke:process
task smoke:mcp
task ci
task pack
task install
task clean
```

`task test` runs non-live, non-benchmark tests. Use `task benchmark:simulation`
for the separate fixed Release measurement. Use `task test:live` only when live
provider validation is explicitly requested and safe.

`task restore` verifies the committed NuGet graph. After an approved package
change, use `task restore:lock` to refresh that graph and `task tools:lock` to
refresh the committed Mise download records. Review both lock-file changes.

`task install` changes the local workstation: it packs the current branch,
installs the command, and verifies a self-contained Scryfall-enabled server
can start. Use it for a real local install, not as a routine test command.

## Adding A Task

When adding a task:

1. Put shared paths and flags in `vars`.
2. Give the task a clear `desc`.
3. Keep platform-specific commands under `platforms`.
4. Prefer artifact output under `artifacts/`.
5. Update docs when it becomes a supported command.
