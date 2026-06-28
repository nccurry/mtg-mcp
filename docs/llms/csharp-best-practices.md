# C# Best Practices

## Language And Runtime

- Use the checked-in .NET SDK and language features already supported by this
  repo's target framework and toolchain.
- Keep nullable reference types meaningful and treat nullability warnings as
  design feedback.
- Use modern C# features when they make code clearer, not just newer.
- Prefer dependency-light Core logic. MCP host, persistence, network, provider,
  and file-system details belong at the edge.

## Design

- Prefer plain records, enums, small services, and existing helpers before
  adding abstractions.
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

## Errors And Secrets

- Throw for programmer errors and invalid state.
- Return typed outcomes for expected user or domain failures.
- Preserve useful exception context when wrapping errors.
- Never expose Archidekt credentials, tokens, cookies, or local secret paths in
  errors, logs, config output, tests, or docs.

## Tests

- Test behavior rather than implementation details.
- Keep normal tests deterministic and local.
- Avoid network, machine-global state, real Archidekt mutation, and wall-clock
  timing in normal tests.
- Use focused unit tests for Core logic and fixture-backed adapter tests when
  provider translation behavior matters.
