# Contributing

Run these before sending changes:

```bash
task build
task test
```

From a fresh checkout, bootstrap local prerequisites first:

```bash
./bootstrap.sh
```

On Windows, use `.\bootstrap.ps1`.

Keep adapters isolated from core domain logic. Normal tests must use fixtures or mock HTTP handlers and must not mutate real Archidekt decks.

Public MCP surface changes must update `README.md`, `CHANGELOG.md`, and the
surface tests. Follow `docs/versioning.md` for deprecations and result-shape
changes.
