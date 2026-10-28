# Agent Notes

## Source Of Truth

- This file is the main instruction file for coding agents in this repository.
- Current code, tests, project files, `Taskfile.yml`, `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`, `.editorconfig`, and human-facing
  docs describe the current checkout and win over stale planning notes.
- For work shaped by the completed rewrite, the approved overall rules and
  completed child PLCs define the design limits. Treat legacy code as evidence,
  not as a default design to copy.
- `llms.txt` is a compact orientation map, not a second rulebook.
- `docs/llms/` contains extra agent guidance, long-lived plans, and PLC packets.
  `.codex/` contains optional review and validation playbooks.
- Read the closest scoped `AGENTS.md` under `src/`, `src/MtgMcp.Core/`,
  `src/MtgMcp.App/`, `src/MtgMcp.Scryfall/`, `src/MtgMcp.Playgroup/`,
  `tests/`, or `docs/` before
  changing that tree.

## North Star

- Help LLMs make informed deckbuilding decisions by returning grounded card,
  deck, source, and statistical evidence with visible provenance and limits.
- Keep source facts, source evidence, derived mathematics, sampled estimates,
  parser-derived classifications, heuristics, and blended scores distinct.
- Do not invent missing facts, equate deterministic output with factual output,
  or treat popularity as proof of card quality.
- Keep `mtg-mcp` a server that returns evidence and performs explicit
  operations, not a Magic rules engine or the final decision-maker.
- Treat `docs/north-star.md`, `docs/design-goals.md`, and
  `docs/heuristic-models.md` as the long-term product direction.

## Evidence-First Rewrite

- Read `docs/rewrite-guide.md` before planning, reviewing, or implementing the
  clean-break `0.9.0` work.
- The completed overall PLC owns the rules shared by its child PLCs. New work
  that spans several modules or public APIs needs its own reviewed and approved
  PLC.
- Accepted AMEND-005 plus the completed hardening, statistics, and
  categorization children define the original 93-tool rewrite baseline. The
  completed Commander Spellbook evidence child added three tools and the
  on-curve estimate one decks tool (97 in total). The focused agent tool
  surface then removed, merged, and renamed tools, so with every toolset on
  the server exposes 80 tools in `remote` mode (53 in `read-only`, 67 in
  `local`). Statistics remains exact, caller-supplied,
  provider-independent, and legality-free. The stabilization child and `0.9.0`
  release are complete.
- Stable `0.9.0` returns evidence, provider data, explicit operations, exact
  mathematics, and one limited on-curve estimate. The client LLM makes
  deckbuilding decisions.
- Do not carry advisor prompts, intent inference, recommendations, weak-card
  judgments, blended quality scores, or strategic simulation into the stable
  rewrite.
- Modes are `read-only`, `local`, and `remote` (default). Tools use the
  `local_deck_*`, `scryfall_*`, `archidekt_*`, `playgroup_*`,
  `spellbook_*`, and `stats_*` prefixes, and each description starts with its
  data source. Accepted AMEND-004 removes the separate Tagger prefix,
  adapter, toolset, and database; do not implement the superseded design.
- Assign every stable tool to exactly one toolset. Toolsets choose which tools an
  LLM sees; operation mode is the only write-permission boundary. Registration
  is fixed for an MCP session, every toolset is on by default, and the Archidekt
  and Playgroup toolsets turn on only when their credentials are present.
- The rewrite starts fresh: do not add automatic legacy data, configuration, or
  schema migration or compatibility aliases unless an approved overall PLC
  amendment explicitly changes that rule.

## Architecture

- Keep dependency-light domain logic in `MtgMcp.Core`; it must not reference
  adapter or host projects.
- Keep MCP registration, tools, resources, prompts, operation modes, server
  metadata, and host code in `MtgMcp.App`.
- Keep third-party HTTP contracts, auth, pacing, retries, and provider caches in
  their adapter projects.
- Adapters may translate third-party payloads into Core models, but Core must
  not expose provider transport types.
- Prefer the least code and abstraction needed. Add an abstraction only when it
  removes real duplication, clarifies ownership, or matches an established
  pattern.
- Follow the approved project boundaries: Decks owns local storage and import/
  export, Statistics owns exact calculations, provider projects own HTTP and
  caches, and App hosts and wires the MCP server together.
- Under accepted AMEND-004, Scryfall owns official card, ruling, and community
  tag acquisition in one `scryfall.db`, while their schemas and evidence classes
  remain distinct. Full Scryfall card-data downloads are explicit; no background
  download or unsupported Tagger-site acquisition is allowed.
- Do not solve a growing tool list with a generic router, intent inference,
  per-request tool switching, or compatibility aliases. Keep each tool clearly
  owned by one toolset. Merge catalog or discovery tools when one stable result
  can describe the same information without loss.

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
- Put long-lived ordinary plans under `docs/llms/plans/`. Put work that spans
  several modules, changes public tools/APIs, changes provider contracts or
  storage, or needs phases under `docs/llms/plcs/`.
- Keep PLCs in `planned/`, `in-progress/`, or `completed/` according to their
  lifecycle. Keep ignored `/plans/` for local scratch only.
- For docs-only guidance changes, run `git diff --check` and inspect links and
  rendered Markdown. Do not run a .NET build unless executable inputs changed.
- Do not hand-edit generated build, package, coverage, benchmark, or release
  artifacts unless the task explicitly targets those baselines.
