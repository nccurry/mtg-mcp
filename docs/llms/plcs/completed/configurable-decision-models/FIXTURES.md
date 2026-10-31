# Configurable Decision Models Fixtures

## Fixture Families

| ID | Purpose | Expected evidence |
| --- | --- | --- |
| CDM-ORDER-001 | Same policies in different file order | Same outcome and trace order |
| CDM-TIE-001 | Equal-priority choices | Stable policy-ID and choice-ID tie-break trace |
| CDM-UNSUPPORTED-001 | Unsupported card behavior | Unsupported union case with capability reason |
| CDM-INDETERMINATE-001 | Insufficient facts | Indeterminate case listing missing facts |
| CDM-BUDGET-001 | Step/choice budget exceeded | Bounded outcome and consumed-budget metadata |
| CDM-REPLAY-001 | Same seed, model, and fingerprint | Byte-equivalent decision payload |
| CDM-SCHEMA-001 | Unknown predicate/operator | Validation failure with JSON path |
| CDM-NOEVAL-001 | Script-like configuration | Rejected as unsupported data, never executed |

## Acceptance Matrix

Each supported decision needs cases for chosen, rejected, unsupported, and
indeterminate outcomes when those payloads are meaningful. Each case records
policy ID, model version, seed when sampled, input fingerprint, assumptions,
warnings, considered choices, and applied tie-breaker.

## Calibration Cases

Use representative aggro, combo, control, value, big-mana, and stax workspaces.
Record baseline and candidate profile results; do not label calibration scores
as universal deck quality.

## Update Rules

- Never update replay output without a model-version decision.
- Keep seeds stable unless the fixture specifically tests seed variance.
- Review trace ordering changes as public-behavior changes.
- Keep provider data out of evaluator fixtures; normalize it before snapshot creation.
