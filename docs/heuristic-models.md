# Heuristic And Simulation Models

`mtg-mcp` uses heuristics when card facts and exact mathematics cannot answer a
deckbuilding question alone. Those models must remain configurable, repeatable,
and visibly different from facts.

## Model Contract

Every model or profile that makes a choice or estimate should define:

- A stable model and profile identifier with an explicit version.
- Immutable input facts and the source or fingerprint of those facts.
- Validated parameters with bounded values and documented inheritance.
- An explicit policy order, priority, and deterministic tie-breaker.
- A deterministic random source and seed when sampling is required.
- Bounded work, output, and trace sizes.
- Chosen, rejected, unsupported, and indeterminate outcomes when those cases
  carry different information.
- Assumptions, warnings, unsupported mechanics, and a trace of the policies or
  evidence that affected the result.

Configuration may select known policies and tune allowlisted parameters. It
must not execute arbitrary scripts, dynamically compiled expressions, or
untrusted code.

## Rules-Engine Lessons Without A Rules Engine

Useful rules-engine practices include named/versioned workflows, schema
validation, explicit facts, priority, deterministic conflict resolution,
explainable results, and cached fact evaluation. `mtg-mcp` should apply those
ideas through small typed C# policies and existing simulation profiles.

It should not implement stack handling, priority exchange, layers, replacement
effects, comprehensive card scripting, or general forward chaining. Unsupported
Magic behavior is reported conservatively instead of guessed.

## Validation

- Profile validation covers duplicate IDs, unknown parents, cycles, unsupported
  predicates, invalid ranges, and missing explicit selections.
- Fixed inputs and seeds reproduce the same result for the same model version.
- Tests prove policy precedence, tie-breaking, warnings, trace bounds, and
  unsupported behavior.
- Calibration compares directional or bounded expectations rather than treating
  one synthetic fixture as ground truth.

See [simulation profiles](simulation-profiles.md),
[Stats Lab metrics](stats-lab-metrics.md), and the
[north star](north-star.md).
