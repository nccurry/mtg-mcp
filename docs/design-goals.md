# Design Goals

## Evidence Before Advice

- Return provider attribution, retrieval time, cache status, assumptions,
  warnings, confidence, and determinism when they affect interpretation.
- Keep incompatible populations separate. EDHREC inclusion, tournament results,
  and Playgroup observations answer different questions.
- Prefer inspectable counts, rows, deltas, and confidence intervals over opaque
  recommendations. Let the calling LLM explain tradeoffs.

## Small Stable Core

- Keep `MtgMcp.Core` free of runtime third-party packages and host or adapter
  references.
- Put deterministic deck, analysis, planning, evidence, and simulation behavior
  in Core behind provider-neutral models.
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
- Default to plan mode. Require explicit apply configuration for deck or remote
  mutations, and enforce permission in code rather than trusting annotations.
- Redact credentials and sensitive local paths from logs, errors, resources,
  test data, and configuration output.

## Evolvable Estimation

- Version heuristic and simulation behavior and make profiles configurable,
  validated, bounded, and replayable.
- Preserve inputs, seeds, assumptions, warnings, and decision traces when a
  result may be compared later.
- Extend the current typed profile architecture before adding a generic rules
  engine or expression language.
