# C# Best Practices

## Language And Runtime

- Use the checked-in .NET SDK and language features already supported by this
  repo's target framework and toolchain.
- Keep nullable reference types meaningful and treat nullability warnings as
  design feedback.
- Use modern C# features when they make code clearer, not just newer.
- Prefer dependency-light Core logic. MCP host, persistence, network, provider,
  and file-system details belong at the edge.
- For the evidence-first rewrite, use the active child's project boundary:
  local deck persistence belongs in Decks and exact mathematics in Statistics;
  do not move legacy planning, recommendation, intent, or simulation models
  into Core.
- Use unions for closed alternatives with case-specific payloads and handle them
  exhaustively. Keep enums for simple categories and records for independent
  state.

## Design

- Prefer plain records, enums, small services, and existing helpers before
  adding abstractions.
- Prefer guard clauses and shallow control flow. Extract a helper when it names
  a real rule or responsibility, not merely to reduce line count.
- Keep public contracts small, intention-revealing, and evidence-oriented.
- Do not leak adapter-specific HTTP payloads, auth details, or host types into
  `MtgMcp.Core`.
- Use typed outcomes when failures are expected domain outcomes.
- Keep source-backed results explicit about source, availability, confidence,
  cache status, assumptions, warnings, and determinism.

## Async

- Accept and pass through `CancellationToken` for async library work.
- Use `ConfigureAwait(false)` in library code unless host behavior needs the
  original context.
- Avoid fire-and-forget work unless ownership, logging, and cancellation are
  explicit.

## Comments

- Add a useful XML summary to every named type and member, including private
  members and tests.
- Comment local functions and lambdas only when their intent or invariant is not
  obvious.
- Avoid summaries that merely restate a member name or type. Add parameter and
  return tags only when they provide information the signature does not.
- Use `<inheritdoc/>` for inherited contracts instead of copying text.

## Errors And Secrets

- Throw for programmer errors and invalid state.
- Return typed outcomes for expected user or domain failures.
- Preserve useful exception context when wrapping errors.
- Never expose provider credentials, tokens, cookies, CSRF values, or local
  secret paths in errors, logs, config output, tests, or docs.

## Tests

- Test behavior rather than implementation details.
- Keep normal tests deterministic and local.
- Avoid network, machine-global state, real Archidekt mutation, and wall-clock
  timing in normal tests.
- Use focused unit tests for Core logic and fixture-backed adapter tests when
  provider translation behavior matters.
- Maintain at least 90 percent line coverage for every production assembly, but
  treat coverage as evidence rather than a substitute for meaningful assertions.
