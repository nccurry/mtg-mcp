# Configurable Decision Models Architecture And Design

## Constraints And Strategy

The design extends the current simulation-profile boundary. Core receives
validated policy definitions and immutable facts; App owns file/configuration
binding and bounded MCP presentation. Adapters remain unaware of policy
execution.

Use ideas from open-source rules engines selectively: workflow/schema
validation, explicit conflict resolution, and readable declarative JSON. Do
not import their packages into Core and do not reproduce a general-purpose
expression language.

## Building Blocks

- `DecisionInputSnapshot`: immutable facts, modeled state, and fingerprint.
- `DecisionPolicySet`: stable ID, model version, priority-ordered policies, and budgets.
- `AllowedDecisionPredicate`: a closed set of supported comparisons over named facts.
- `DecisionEvaluator`: pure bounded evaluation with no I/O.
- `DecisionOutcome`: payload union for chosen, rejected, unsupported, and indeterminate.
- `DecisionTrace`: ordered choices, policy matches, rejections, assumptions, warnings, and unsupported behaviors.
- App configuration loader and presenter: validation paths, detail levels, redaction, and size limits.

## Runtime Flow

1. App resolves a built-in or configured profile and validates its schema.
2. Core creates an immutable snapshot and stable input fingerprint.
3. The evaluator sorts policies by priority, then stable policy ID.
4. Each predicate is evaluated against an allowlisted fact accessor.
5. Budgets stop excessive work with an indeterminate/bounded outcome.
6. The evaluator returns one typed outcome and a complete ordered trace.
7. App presents bounded summary evidence or fuller normal/full trace data.

## Data And Conflict Design

Configuration is data, not code. Predicates use named operands and a closed
operator vocabulary. Parameters have explicit types and numeric/string bounds.
Unknown policy kinds, predicates, parameters, or versions fail validation.

Higher priority wins. Equal priorities use stable policy ID and choice ID
ordering. Configuration order is never the sole conflict resolver.

Use a union only where cases carry different payloads. Simple categories such
as severity or supported operator remain enums; orthogonal trace data remains
records.

## Failure Modes

| Failure | Response |
| --- | --- |
| Unknown predicate or parameter | Reject configuration with path and reason code. |
| Unsupported card behavior | Return unsupported outcome and trace the missing capability. |
| Choice tie after configured policies | Apply stable tie-breaker and record it. |
| Budget exhausted | Return indeterminate bounded outcome with consumed budget. |
| Version mismatch | Reject or use an explicit documented migration; never guess. |

## Alternatives

- General-purpose external rules engine: rejected because it expands Core
  dependencies and execution semantics beyond the product need.
- Arbitrary scripting: rejected because it is difficult to bound, secure,
  reproduce, and explain.
- Hard-coded decisions only: rejected because assumptions cannot be tuned or
  calibrated without code changes.
- Configuration order as priority: rejected because harmless reformatting can
  change outcomes.

## Test Architecture

Unit tests cover predicate semantics, exhaustive outcomes, ordering, budgets,
immutability, and fingerprints. Fixture tests cover JSON validation and replay.
Calibration tests compare profile versions. App tests cover detail levels,
schema descriptions, configuration errors, and surface stability. No normal
test uses network access.

## Deferred Work

Public profile mutation, remote profile registries, user-defined scripts, and
comprehensive Magic rules behavior are deferred and require separate PLCs.
