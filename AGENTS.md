# Agent Notes

## Source Of Truth

- This file is the authoritative durable instruction file for coding agents in
  this repository.
- Current code, tests, project files, `Taskfile.yml`, `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, `.editorconfig`, and human-facing
  docs win over stale planning notes.
- `llms.txt` is a compact orientation map, not a second rulebook.
- `docs/llms/` contains supplemental workflow guidance, durable plans, and PLC
  packets. `.codex/` contains optional review and validation playbooks.
- Read the closest scoped `AGENTS.md` under `src/`, `src/MtgMcp.Core/`,
  `src/MtgMcp.App/`, `tests/`, or `docs/` before changing that tree.

## North Star

- Help LLMs make informed deckbuilding decisions by returning grounded card,
  deck, source, and statistical evidence with visible provenance and limits.
- Keep source facts, source evidence, derived mathematics, sampled estimates,
  parser-derived classifications, heuristics, and blended scores distinct.
- Do not invent missing facts, equate deterministic output with factual output,
  or treat popularity as proof of card quality.
- Keep `mtg-mcp` an evidence and workflow server, not a Magic rules engine or
  the final decision-maker.
- Treat `docs/north-star.md`, `docs/design-goals.md`, and
  `docs/heuristic-models.md` as the durable product direction.

## Architecture

- Keep dependency-light domain logic in `MtgMcp.Core`; it must not reference
  adapter or host projects.
- Keep MCP registration, tools, resources, prompts, operation modes, server
  metadata, and host concerns in `MtgMcp.App`.
- Keep third-party HTTP contracts, auth, pacing, retries, and provider caches in
  their adapter projects.
- Adapters may translate third-party payloads into Core models, but Core must
  not expose provider transport types.
- Prefer the least code and abstraction needed. Add an abstraction only when it
  removes real duplication, clarifies ownership, or matches an established
  pattern.

## C# Style

- Use the checked-in .NET 11 preview toolchain, nullable reference types, and
  clear C# 15 features supported by that toolchain.
- Use unions for closed alternatives with meaningful case payloads and switch
  exhaustively. Keep enums for simple categories and records for orthogonal
  state; do not convert models only to use a newer feature.
- Prefer guard clauses, shallow control flow, small cohesive methods, and loops
  over complex multi-line LINQ. Use `List.Sort()` for an existing mutable list.
- Keep the established unprefixed camelCase private-field convention.
- Add a useful XML summary to every named C# type and member, including private
  members and tests. Comment local functions and lambdas only when their intent
  is not obvious.
- Avoid boilerplate summaries that restate a member name or type. Add parameter,
  return, and type-parameter tags only when they add information; use
  `<inheritdoc/>` for inherited contracts.
- Use inline comments sparingly for provider quirks, safety invariants,
  secret-handling, concurrency, persistence, or non-obvious algorithms.
- Never mention prompts or change history in code comments, and never use
  emojis in code text, logs, or documentation comments.

## Safety And Tests

- Pass `CancellationToken` through async library paths and use
  `ConfigureAwait(false)` consistently outside host-specific code.
- Guard write-capable MCP tools with `OperationModeGuard`; keep annotations,
  descriptions, output schemas, and operation-mode visibility accurate.
- Never expose credentials, tokens, cookies, or local secret paths in errors,
  logs, configuration output, tests, or docs.
- Keep normal tests deterministic, offline, and free of real Archidekt
  mutations. Prefer fixtures, fake HTTP, temporary files, and in-memory stores.
- Mark live provider tests with `Category=Live` so `task test` stays offline.
- Maintain at least 90 percent line coverage for every production assembly with
  behavior-focused tests; do not meet the gate through unjustified exclusions.

## Planning And Validation

- Use `task --list` as the menu of supported repository operations.
- For a small change, run the narrow affected test first. For shared behavior,
  public MCP shape, project references, or adapter contracts, run `task lint`
  and `task test` as risk warrants.
- Put durable ordinary plans under `docs/llms/plans/` and cross-cutting,
  public-surface, adapter-contract, persistence, or phased work under
  `docs/llms/plcs/`.
- Keep PLCs in `planned/`, `in-progress/`, or `completed/` according to their
  lifecycle. Keep ignored `/plans/` for local scratch only.
- For docs-only guidance changes, run `git diff --check` and inspect links and
  rendered Markdown. Do not run a .NET build unless executable inputs changed.
- Do not hand-edit generated build, package, coverage, benchmark, or release
  artifacts unless the task explicitly targets those baselines.
