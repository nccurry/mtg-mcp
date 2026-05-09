# Agent Notes

- Prefer small, direct C# changes that match the existing architecture.
- `MtgMcp.Core` must not reference adapter or host projects.
- `MtgMcp.Scryfall` and `MtgMcp.Archidekt` own third-party HTTP contracts.
- Normal tests must not require network access or mutate real Archidekt decks.
- Use `Taskfile.yml` for common development workflows.
