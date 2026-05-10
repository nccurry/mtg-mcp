# Agent Notes

- Prefer small, direct C# changes that match the existing architecture.
- `MtgMcp.Core` must not reference adapter or host projects.
- `MtgMcp.Scryfall` and `MtgMcp.Archidekt` own third-party HTTP contracts.
- Normal tests must not require network access or mutate real Archidekt decks.
- Use `Taskfile.yml` for common development workflows.

## Style and Safety

- Add XML summary comments for new C# declarations, but keep them specific and useful; avoid boilerplate like "Handles X" or "Gets the Y" when a clearer purpose can be stated.
- Use inline comments sparingly, mainly for external API quirks, safety invariants, secret-handling, concurrency, or persistence decisions that are not obvious from the code.
- Keep formatting scan-friendly: separate different phases or concerns with a blank line, but keep related setup, mapping, and assertions together.
- For async library code, pass `CancellationToken` through and use `ConfigureAwait(false)` consistently.
- For MCP tools/resources/prompts, keep annotations and descriptions accurate, guard mutating tools with `OperationModeGuard`, and update surface tests when the public MCP shape changes.
- Never expose Archidekt secrets in errors, logs, config output, or tests; use redaction patterns already in the repo.
- Prefer fixture, fake HTTP, and in-memory repository tests. Mark live network tests as `Category=Live`, and keep normal `task test` safe for offline runs.
- Before wrapping up changes, run the narrow relevant test first, then `task lint` or `task test` when the change touches shared behavior.
