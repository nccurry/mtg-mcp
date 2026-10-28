# Design Goals

## Evidence Before Advice

- Return provider attribution, retrieval time, cache status, assumptions,
  warnings, confidence, and determinism when they affect interpretation.
- Keep incompatible source contexts separate. Official Scryfall facts,
  Scryfall community-tag evidence, Archidekt state, and Playgroup observations answer
  different questions. Future popularity/tournament sources must also retain
  their populations and denominators.
- Prefer inspectable counts, rows, exact derivations, and explicit unavailable
  states. The calling LLM explains tradeoffs and makes deckbuilding choices.
- Stable `0.9.0` contains no recommendation, intent, weak-card, replacement,
  blended-score, advisor-prompt, or strategic-automation feature.

## Small Stable Core

- Keep `MtgMcp.Core` free of runtime third-party packages and host or adapter
  references.
- Keep only dependency-light provider-neutral evidence, identifiers, failures,
  and shared contracts in Core.
- Put local deck persistence/interchange in Decks and exact probability logic in
  Statistics; do not move those concerns into Core for convenience.
- Keep HTTP payloads, auth, pacing, retry, and provider caches in their owning
  adapters. Keep MCP and configuration composition in App.
- Use C# unions for closed outcomes with case-specific payloads, enums for simple
  categories, and records for independent state.

## Testable By Construction

- Inject time, randomness, storage, and network boundaries when behavior needs
  control in tests.
- Keep normal tests deterministic, offline, and free of real provider mutation.
- Maintain at least 90 percent line coverage per production assembly with tests
  that prove observable behavior and failure handling.
- Keep analyzer, architecture, tool/API, calibration, and performance checks as
  complementary evidence; coverage alone is not correctness.

## MCP-Native Safety

- Keep tool registration stable, deterministic, described, annotated, and
  schema-backed.
- Bound routine output with detail levels and pagination.
- Use `read-only`, `local` (default), and `remote`. Enforce local and remote
  mutation authority separately in code rather than trusting annotations.
- Redact credentials and sensitive local paths from logs, errors, resources,
  test data, and configuration output.
- Report every failed tool call with the MCP `isError` flag and a structured
  body (`kind`, `reasonCode`, `message`). Input the server cannot read returns
  `invalid-input` with reason `invalid-arguments` and a top-level `path` to the
  rejected value. A union's `kind` may appear in any property position.

## Clear Tool Groups

- Assign every stable tool to exactly one toolset. Toolsets decide which tools a
  client sees; operation modes decide what it may change.
- Keep registration fixed for the MCP session. Select toolsets at startup and
  report the resulting tool list without claiming it can change dynamically.
- Make `decks`, `scryfall`, and `stats` the focused default profile. Require
  explicit enablement for `archidekt`, `playgroup`, and `spellbook`.
- Support `default`, `all`, `none`, and explicit toolset lists so clients and
  tests can request a predictable tool list.
- Add a tool only when its distinct input/output contract completes a useful LLM
  task. Prefer one catalog tool over parallel discovery tools that return the
  same facts.
- Do not replace a large explicit tool list with a generic router, intent
  inference, per-tool allowlists, or compatibility aliases.

## Deferred Experimental Estimation

- Heuristic recommendations, blended scores, and simulation are not stable
  rewrite capabilities. Do not preserve the legacy model/profile architecture
  merely because it exists.
- The approved `deck_on_curve_estimate` follows this rule: it is versioned,
  bounded, replayable, and visibly separate from provider facts and exact
  mathematics. Any future sampled or heuristic evidence must meet the same bar.
- Never add a generic rules engine or arbitrary expression language.

See [Evidence-First Rewrite Guide](rewrite-guide.md) for what the server does
now, what remains only a plan, and who may authorize implementation.
