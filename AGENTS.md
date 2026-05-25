# Agent Notes

- Prefer small, direct C# changes that match the existing architecture.
- Write idiomatic C# using the latest language features already supported by this repo's target framework and toolchain.
- `MtgMcp.Core` must not reference adapter or host projects.
- `MtgMcp.Scryfall` and `MtgMcp.Archidekt` own third-party HTTP contracts.
- Normal tests must not require network access or mutate real Archidekt decks.
- Use `Taskfile.yml` for common development workflows.

## Style and Safety

### Simplicity

- Use the least amount of code and abstraction needed to solve the problem.
- Do not add abstractions unless they remove real duplication, clarify ownership, or match an existing repo pattern.
- Prefer code that is straightforward to read over clever or densely chained code.

### Comments and Documentation

- Add XML summary comments for new C# declarations, but keep them specific and useful; avoid boilerplate like "Handles X" or "Gets the Y" when a clearer purpose can be stated.
- Good XML comments should help the IDE reader understand what role the member serves and, when helpful, why it matters or how to use it.
- Use inline comments sparingly, mainly for external API quirks, safety invariants, secret-handling, concurrency, or persistence decisions that are not obvious from the code.
- Comments should not mention planning context, prompts, or why the agent changed code.
- Never use emojis in log messages, comments, documentation comments, or other code text.

### Naming

- Prefer variable, field, and property names that match their type when a shorter name would be generic, such as `DeckAnalyzer` for a `DeckAnalyzer`.
- Add a domain or ownership prefix only when it clarifies the role, boundary, or adapter involved.

### LINQ Usage

- Prefer `for` and `foreach` loops over complex multi-line LINQ chains.
- Use `if` conditionals instead of `.Where()` with multi-line lambdas.
- Use `List.Sort()` instead of `.OrderBy().ToList()` when sorting an existing mutable list.
- Simple one-line LINQ calls such as `.Any()`, `.First()`, `.Select()`, `.ToArray()`, and `.ToList()` are fine when they stay readable.

### Safety

- Keep formatting scan-friendly: separate different phases or concerns with a blank line, but keep related setup, mapping, and assertions together.
- For async library code, pass `CancellationToken` through and use `ConfigureAwait(false)` consistently.
- For MCP tools/resources/prompts, keep annotations and descriptions accurate, guard mutating tools with `OperationModeGuard`, and update surface tests when the public MCP shape changes.
- Never expose Archidekt secrets in errors, logs, config output, or tests; use redaction patterns already in the repo.
- Prefer fixture, fake HTTP, and in-memory repository tests. Mark live network tests as `Category=Live`, and keep normal `task test` safe for offline runs.
- Before wrapping up changes, run the narrow relevant test first, then `task lint` or `task test` when the change touches shared behavior.

## Design Feedback

- When a request leaves design room, consider simpler alternatives that match existing project boundaries.
- If a suggested approach adds avoidable coupling, abstraction, or test risk, name the concern and suggest a better fit.
- Mention obvious design problems in code you touch, but keep unrelated refactors out of the change unless they are necessary.

## C# 15 Features

- Prefer C# 15 features when they make domain shapes, API boundaries, or call sites clearer than older patterns.
- Prefer union types for closed alternatives, discriminated outcomes, and typed error or result shapes instead of hand-rolled base classes, marker interfaces, nullable tuples, or loosely typed status objects.
- Use exhaustive switching over union values so new cases surface as compile-time work instead of hidden runtime behavior.
- Use collection expression arguments when constructor or factory arguments such as capacity, comparer, or options make initialization clearer or more efficient.
