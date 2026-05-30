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
