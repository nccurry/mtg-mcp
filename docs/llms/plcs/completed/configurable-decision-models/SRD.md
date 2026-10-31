# Configurable Decision Models Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related design: [SADD.md](SADD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-07-03 | mtg-mcp | Initial foundation follow-up draft. |

## Context And Outcomes

Simulation necessarily makes assumptions. Users and LLMs need those
assumptions to be explicit, configurable within safe bounds, reproducible, and
separate from source facts. Maintainers need new policies to remain testable
without embedding a full Magic rules engine.

## References

- [North star](../../../../north-star.md)
- [Heuristic models](../../../../heuristic-models.md)
- [Simulation profiles](../../../../simulation-profiles.md)
- [Stats Lab metrics](../../../../stats-lab-metrics.md)
- [Microsoft RulesEngine](https://github.com/microsoft/RulesEngine)
- [NRules](https://nrules.net/)
- [json-rules-engine](https://github.com/CacheControl/json-rules-engine)

External projects are design references only; they are not approved Core
dependencies.

## Scope And Non-Scope

In scope are immutable input snapshots, versioned policies, allowlisted JSON
configuration, deterministic conflict resolution, typed outcomes, bounded
execution, full decision traces, replay metadata, and calibration fixtures.

Out of scope are arbitrary scripts, dynamic code, `eval`, unbounded recursion,
provider HTTP contracts in Core, and comprehensive stack, priority, layers,
replacement effects, or card-rules execution.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | A user selects a simulation profile. | The evaluator applies a named versioned policy set and reports it. |
| CASE-002 | Two allowed policies prefer different choices. | Priority and stable tie-breaking produce one replayable outcome. |
| CASE-003 | A card behavior is unsupported. | The result says unsupported and records the missing behavior without inventing it. |
| CASE-004 | A maintainer changes policy configuration. | Offline fixtures expose decision and calibration changes before merge. |

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| CDM-001 | Must | Evaluation inputs shall be immutable facts and state snapshots. | Tests prove evaluation does not mutate caller-owned state. |
| CDM-002 | Must | Every policy set shall have a stable policy ID and model version. | Results and traces expose both values. |
| CDM-003 | Must | Policy ordering shall use explicit priority and deterministic tie-breaking. | Reordered equivalent configuration produces the same choice. |
| CDM-004 | Must | Configuration shall permit only allowlisted parameters, operators, and predicates. | Unknown fields/operators fail validation; scripts and `eval` are impossible. |
| CDM-005 | Must | Evaluation shall enforce bounded choice, step, depth, and time budgets. | Budget fixtures return a typed bounded outcome without hanging. |
| CDM-006 | Must | Distinct payload outcomes shall use a closed union for chosen, rejected, unsupported, and indeterminate cases. | Switches are exhaustive and each case carries relevant evidence. |
| CDM-007 | Must | Every decision shall produce a trace of considered choices, rejections, policies, assumptions, warnings, and unsupported behavior. | Normal/full fixtures verify trace completeness and stable ordering. |
| CDM-008 | Must | Sampled decisions shall expose seed, model version, assumptions, and input fingerprint. | The same fixture and seed replay byte-equivalent decision data. |
| CDM-009 | Must | Configuration and tests shall work offline. | Normal tests use files, fixtures, and in-memory state only. |
| CDM-010 | Must | The capability shall not claim rules-engine completeness. | Public descriptions and notes state the supported model boundary. |
| CDM-011 | Should | Existing simulation profile IDs shall remain compatible or have explicit migration notes. | Compatibility tests cover built-in profiles and documented aliases. |
| CDM-012 | Should | Summary output shall remain bounded while normal/full can expose traces. | Surface tests verify size limits and detail-level behavior. |

## Interfaces, States, And Modes

The first implementation should extend existing simulation profile loading and
simulation tools. Read-only analysis remains visible in all modes. Any future
persisted profile editing must be classified separately and guarded by the
operation-mode contract. Configuration failures must identify paths and policy
IDs without exposing secrets.

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Determinism | Same inputs, version, configuration, and seed produce the same outcome and trace. |
| Safety | Unknown predicates fail closed; every execution budget has a tested limit. |
| Testability | Policy evaluation has no network, clock, filesystem, or adapter dependency. |
| Explainability | Every rejection and unsupported choice has a stable reason code. |
| Maintainability | Core remains dependency-light and policy-specific branches remain shallow. |

## Traceability And Validation

| Requirement group | Design section | Evidence |
| --- | --- | --- |
| CDM-001–003 | Core model and evaluator | Unit determinism and immutability tests |
| CDM-004–005 | Configuration and budgets | Schema, rejection, and budget fixtures |
| CDM-006–008 | Outcomes and traces | Exhaustive switch and replay tests |
| CDM-009–012 | Integration and presentation | Offline App surface, docs, and compatibility tests |

## Definition Of Done

- [ ] Must requirements are implemented and traced to tests.
- [ ] Calibration changes are reviewed with before/after evidence.
- [ ] Public descriptions state assumptions and non-goals.
- [ ] No general-purpose rules-engine dependency is added to Core.
- [ ] `task lint`, `task test`, coverage, surface, and smoke gates pass.
