# mtg-mcp — Agent Instructions

`mtg-mcp` is a C# MCP server that gives LLMs grounded Magic: The Gathering
card, deck, source, and statistical evidence plus explicit deck operations. It
ships as a .NET tool and release archives built through `Taskfile.yml`.

## Source of truth

When sources disagree, trust them in this order:

1. Current code, tests, and project files.
2. `Taskfile.yml` and `.github/workflows/` for build and check commands.
3. `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
   `.editorconfig`, lock files, and other checked-in configuration.
4. `README.md`, `CONTRIBUTING.md`, and the docs under `docs/`.
5. Scoped `AGENTS.md` files, `llms.txt`, and `.codex/` playbooks.

Completed PLCs in `docs/llms/plcs/completed/` and legacy code in Git history
are design evidence, not designs to copy; current code and tests win.

## North star

- Help LLMs make informed deckbuilding decisions by returning grounded card,
  deck, source, and statistical evidence with visible provenance and limits.
- Keep source facts, source evidence, derived mathematics, sampled estimates,
  parser-derived classifications, heuristics, and blended scores distinct.
- Do not invent missing facts, equate deterministic output with factual output,
  or treat popularity as proof of card quality.
- Keep `mtg-mcp` a server that returns evidence and performs explicit
  operations, not a Magic rules engine or the final decision-maker. The client
  LLM makes deckbuilding decisions.
- `docs/north-star.md`, `docs/design-goals.md`, and `docs/heuristic-models.md`
  set the long-term product direction. Read `docs/rewrite-guide.md` before
  changing a product or architecture boundary.

## Product rules

- The stable server returns evidence, provider data, explicit operations, exact
  mathematics, and one seeded sampler, `stats_simulate`, that applies only rules
  the request states. Do not add advisor prompts, intent inference,
  recommendations, weak-card judgments, blended quality scores, or strategic
  simulation to the stable surface. Statistics stays exact and caller-supplied
  and makes no legality verdicts.
- Operation modes are `read-only`, `local`, and `remote` (default). Mode is
  the only write-permission boundary. With every toolset on, the server exposes
  86 tools in `remote` mode (59 in `read-only`, 73 in `local`).
- Tools use the `local_deck_*`, `scryfall_*`, `archidekt_*`, `playgroup_*`,
  `spellbook_*`, and `stats_*` prefixes, and each description starts with its
  data source. Assign every stable tool to exactly one toolset. Toolsets choose
  which tools an LLM sees; they never grant write access.
- Registration is fixed for an MCP session. Every toolset is on by default; the
  Archidekt and Playgroup toolsets turn on only when their credentials are
  present.
- Community tags come from official Scryfall data and live in `scryfall.db`.
  There is no separate Tagger prefix, adapter, toolset, or database.
- Do not add automatic legacy data, configuration, or schema migration, or
  compatibility aliases, unless an approved PLC changes that rule.
- Do not solve a growing tool list with a generic router, intent inference,
  per-request tool switching, or aliases. Merge catalog or discovery tools when
  one stable result can describe the same information without loss.

## Repository map

| Path | What it owns |
| --- | --- |
| `src/MtgMcp.Core/` | Dependency-light, provider-neutral evidence, identifiers, failures, and shared contracts |
| `src/MtgMcp.App/` | MCP host, registration, tools, resources, prompts, operation modes, configuration, and wiring |
| `src/MtgMcp.Decks/` | Local deck storage (`decks.db`) and manual import and export |
| `src/MtgMcp.Statistics/` | Exact deck calculations |
| `src/MtgMcp.Simulation/` | The seeded sampling engine behind `stats_simulate` |
| `src/MtgMcp.Scryfall/` | Scryfall API, card data, rulings, tags, and `scryfall.db` |
| `src/MtgMcp.Archidekt/`, `src/MtgMcp.Playgroup/`, `src/MtgMcp.Spellbook/` | Provider adapters |
| `tests/` | One test project per production project, plus architecture and E2E tests |
| `docs/` | Product docs, `architecture.md`, `adapters.md`, and agent docs under `docs/llms/` |
| `scripts/` | Helper scripts that Task calls |

Read the closest folder `AGENTS.md` before changing files in that folder.

## Architecture

- `MtgMcp.Core` must not reference adapter or host projects.
- Third-party HTTP contracts, auth, pacing, retries, and provider caches stay
  in their adapter projects. Adapters may translate provider payloads into Core
  models, but Core must not expose provider transport types.
- `MtgMcp.Simulation` takes plain counts and rule records and never reads the
  deck store or Scryfall. A choice the engine makes is either a fixed engine
  rule listed in the tool description or a required request field; add a rule
  kind as one record, one evaluator case, and fixtures. Keep its random
  sequence versioned and free of runtime-dependent hashing or ordering.
- Full Scryfall card-data downloads are explicit. Nothing downloads in the
  background, and there is no Tagger-site acquisition.

## Commands

Use Task for every repeatable operation. Run `task --list` for the menu and see
`docs/llms/repo-operations.md` for the policy.

```sh
task test:unit          # App, Core, Statistics, and Simulation unit tests
task test:integration   # architecture and local deck integration tests
task test:e2e           # process end-to-end tests
task lint               # format check plus strict analyzer build
task test               # all non-live tests
task validate           # lint plus test
task ci                 # what CI runs, including coverage gates and smoke tests
```

- Start with the narrowest affected test. Run `task lint` and `task test` for
  shared behavior, public MCP shape, project references, or adapter contracts.
- `task format` changes files. `task install` changes the local workstation.
  `task test:live` calls real providers; run it only when asked and safe.
- Run one top-level Task invocation at a time per worktree. Docs-only changes
  need `git diff --check` and a link check, not a .NET build.
- Do not hand-edit generated build, package, coverage, benchmark, or release
  output unless the task targets those baselines.

## Code and comments

- Write the least code that solves the problem. Reuse existing code and
  dependencies first. Add an abstraction only when it removes real
  duplication, clarifies ownership, or matches an established pattern.
- Comments explain the code. Never mention prompts, plans, or change history in
  code comments. No emojis in code, logs, or documentation comments.
- Use inline comments sparingly, for provider quirks, safety invariants, secret
  handling, concurrency, persistence, or non-obvious algorithms.

### C# and .NET

- `global.json` pins the preview SDK. Package versions live in
  `Directory.Packages.props`; shared build settings live in
  `Directory.Build.props`. Use C# features that toolchain supports.
- Nullable reference types are on and warnings fail the build. Fix warnings
  instead of suppressing them. Follow `.editorconfig`, including the unprefixed
  camelCase private-field convention.
- Add a useful XML summary to every named type and member, including private
  members and tests; the analyzers enforce it. Do not restate the name. Add
  parameter, return, and type-parameter tags only when they add information,
  and use `<inheritdoc/>` for inherited contracts.
- Use unions for closed alternatives with case payloads and switch
  exhaustively. Use enums for simple labels and records for independent data.
  Do not convert models only to use a newer feature.
- Prefer guard clauses, shallow control flow, and small methods. Prefer loops
  over long multi-line LINQ. Use `List.Sort()` for an existing mutable list.
- Pass `CancellationToken` through async library paths and use
  `ConfigureAwait(false)` outside host-specific code.
- More detail: `docs/llms/csharp-best-practices.md`.

## Safety and tests

- Guard write-capable MCP tools with `OperationModeGuard`. Keep annotations,
  descriptions, output schemas, and mode visibility accurate.
- Never expose credentials, tokens, cookies, or local secret paths in errors,
  logs, configuration output, tests, or docs.
- Keep normal tests deterministic, offline, and free of real provider
  mutations. Prefer fixtures, fake HTTP, temporary files, and in-memory stores.
- Mark live provider tests `[Trait("Category", "Live")]`; `task test` skips them.
- Keep at least 90 percent line coverage for every production assembly
  (`task coverage:gates`) with behavior-focused tests, not unjustified
  exclusions.
- Public MCP tool or API changes update `README.md`, `CHANGELOG.md`, and the
  related tests. Follow `docs/versioning.md`.

## Planning

- Put durable ordinary plans under `docs/llms/plans/`. Put work that spans
  several modules, changes public tools or APIs, changes provider contracts or
  storage, or needs phases in a PLC under `docs/llms/plcs/`.
- Keep PLCs in `planned/`, `in-progress/`, or `completed/` by lifecycle. A
  planned PLC is not permission to implement; follow
  `docs/llms/plcs/README.md` for review and owner authorization.
- Use the ignored `/plans/` folder for local scratch only.

## Worktrees

- Do your work in your own worktree: `../<repo-name>-wt/<campaign>/<checkout>`,
  where the campaign is the PLC, plan, or feature slug. Never reuse a folder
  for another branch.
- Never change the branch or files of the main checkout or of a worktree you
  do not own. Never reset, stash, or clean them.
- Evidence: record decisions and short validation results in the PLC or plan
  under `docs/llms/`. Raw logs and output stay untracked in the checkout's
  ignored `artifacts/` folder or in `<campaign>/_output/`.
- To keep a worktree from cleanup, lock it:
  `git worktree lock --reason "<owner>; keep until <condition>; <date>" <path>`.
  Clean, unlocked, idle worktrees may be removed; their branches are kept.
- No containers are used here; if you run one, run it as your own user.

## Commits and reviews

- Use Conventional Commits with a scope, such as `feat(decks): ...` or
  `docs(plc): ...`. The subject says what changed, not the steps taken.
- Keep changes scoped. Do not commit local settings, `mtg-mcp.local.json`,
  logs, caches, credentials, or build output.
- Fix every supported review finding in scope. Reply with evidence when you
  leave one unfixed.

## Keeping this file current

- Add a rule only for a repeated mistake or a fact an agent cannot easily find.
  Prefer a check, analyzer rule, test, or task over a written rule.
- No status, dates, version numbers, tool counts, or plan IDs. They go stale.
- Put multi-step procedures in docs or skills, and link to them.
- Keep this file under 200 lines. Move folder-specific rules into that folder's
  `AGENTS.md`.
- When you change this file, update `llms.txt`, scoped `AGENTS.md` files, and
  `.codex/` to match, and remove rules that no longer apply.
