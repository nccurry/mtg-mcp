# Heuristic And Simulation Models

Stable `0.9.0` does not expose heuristic recommendations, intent inference,
blended quality scores, or strategic simulation. This document constrains
possible post-cutover experiments and explains how to review remaining legacy
models; it is not authorization to retain or rebuild them.

When card facts and exact mathematics cannot answer a deckbuilding question,
the stable MCP returns the available evidence and explicit unknowns. The client
LLM makes the judgment. Any future heuristic model requires its own approved
PLC and must remain configurable, repeatable, and visibly different from facts.

## What Every Model Must State

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

If experimental work is approved, useful rules-engine practices include named,
versioned operations; schema validation; explicit facts; priority; repeatable
conflict resolution; explainable results; and cached fact evaluation. Use the
smallest typed policies the future PLC needs. Do not adopt the old
simulation-profile architecture by default.

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

The current [simulation profiles](simulation-profiles.md) and
[Stats Lab metrics](stats-lab-metrics.md) are legacy reference evidence. See the
[north star](north-star.md), [rewrite guide](rewrite-guide.md), and
[potential-features registry](potential-features.md).
