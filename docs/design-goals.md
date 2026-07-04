# Design Goals

## Evidence Before Advice

- Return provider attribution, retrieval time, cache status, assumptions,
  warnings, confidence, and determinism when they affect interpretation.
- Keep incompatible source contexts separate. Official Scryfall facts, Tagger
  community classifications, Archidekt state, and Playgroup observations answer
  different questions. Future popularity/tournament sources must also retain
  their populations and denominators.
- Prefer inspectable counts, rows, exact derivations, and explicit unavailable
  states. The calling LLM explains tradeoffs and makes deckbuilding choices.
- Stable `0.9.0` contains no recommendation, intent, weak-card, replacement,
  blended-score, advisor-prompt, or strategic-automation surface.

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
- Keep analyzer, architecture, surface, calibration, and performance checks as
  complementary evidence; coverage alone is not correctness.

## MCP-Native Safety

- Keep tool registration stable, deterministic, described, annotated, and
  schema-backed.
- Bound routine output with detail levels and pagination.
- Use `read-only`, `local` (default), and `remote`. Enforce local and remote
  mutation authority separately in code rather than trusting annotations.
- Redact credentials and sensitive local paths from logs, errors, resources,
  test data, and configuration output.

## Surface Discipline

- Assign every stable tool to exactly one capability toolset. Toolsets express
  relevance; operation modes continue to express authority.
- Keep registration static for the MCP session. Select toolsets at startup and
  advertise the resulting exact surface without dynamic list-change claims.
- Make `decks`, `scryfall`, and `stats` the smallest coherent default. Require
  explicit enablement for `archidekt`, `playgroup`, and `tagger`.
- Support `default`, `all`, `none`, and explicit toolset lists so clients and
  tests can request a predictable surface.
- Add a tool only when its distinct input/output contract advances a complete
  LLM workflow. Prefer one catalog tool over parallel discovery tools that
  return the same domain facts.
- Do not replace a large explicit surface with a generic router, intent
  inference, per-tool allowlists, or compatibility aliases.

## Deferred Experimental Estimation

- Heuristic recommendations, blended scores, and simulation are not stable
  rewrite capabilities. Do not preserve the legacy model/profile architecture
  merely because it exists.
- If a post-cutover PLC approves sampled or heuristic evidence, version it,
  bound it, preserve inputs/seeds/assumptions/warnings, and label it separately
  from provider facts and exact mathematics.
- Never add a generic rules engine or arbitrary expression language.

See [Evidence-First Rewrite Guide](rewrite-guide.md) for current-versus-target
routing and implementation authority.
