# Contributing

Run these before sending changes:

```bash
task build
task test
```

On Linux, a fresh checkout can bootstrap local prerequisites first:

```bash
./scripts/setup-linux.sh
source scripts/env-linux.sh
```

Keep adapters isolated from core domain logic. Normal tests must use fixtures or mock HTTP handlers and must not mutate real Archidekt decks.
