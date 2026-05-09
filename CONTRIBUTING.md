# Contributing

Run these before sending changes:

```powershell
task build
task test
```

Keep adapters isolated from core domain logic. Normal tests must use fixtures or mock HTTP handlers and must not mutate real Archidekt decks.
