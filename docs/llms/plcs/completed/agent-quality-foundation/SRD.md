# Agent Quality Foundation Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Summary |
| --- | --- |
| 2026-07-03 | Initial accepted requirements. |

## Purpose And Audience

This document guides maintainers and coding agents implementing repository-wide
documentation, linting, coverage, and MCP safety defaults.

## Scope And Non-Scope

- In scope: north-star docs, scoped agent guidance, LLM orientation, planning
  templates, analyzer enforcement, Task workflows, 90 percent coverage gates,
  plan-mode default, tests, and follow-up PLC packets.
- Out of scope: implementing trust-evidence model changes, a Magic rules
  engine, new provider contracts, or the configurable decision-model runtime.
- Compatibility target: existing MCP names and schemas, explicit operation-mode
  values, normal offline tests, and Core/App/adapter boundaries.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| REQ-001 | Must | The repository shall define a grounded-evidence north star and design goals. | README and architecture docs link concise north-star, design-goal, and heuristic-model documents. |
| REQ-002 | Must | Agent guidance shall use root, source, Core, App, tests, and docs scopes without duplicate adapter files. | Instruction discovery shows the intended chain from each scoped directory. |
| REQ-003 | Must | `llms.txt` shall be a compact linked orientation index. | It has an H1, blockquote summary, linked H2 lists, and an Optional section. |
| REQ-004 | Must | Reusable ordinary-plan and five-file PLC templates shall live under `docs/llms/templates`. | Lifecycle docs point to the new templates; existing packets remain in place. |
| REQ-005 | Must | Every named C# member shall have a useful XML summary. | SA1600 and CS1591 fail the build for missing comments; low-signal comment tests pass. |
| REQ-006 | Must | Lint shall verify formatting and run the tuned analyzer stack with warnings as errors. | `task format:check` and `task lint` pass without blanket suppressions. |
| REQ-007 | Must | Each production assembly shall have at least 90 percent line coverage. | The canonical coverage report includes and gates all eight source assemblies at 90 percent. |
| REQ-008 | Must | An unspecified operation mode shall resolve to `plan`. | Default options, registry, server info, config resources, and process behavior are tested. |
| REQ-009 | Must | Normal validation shall stay deterministic and offline. | `task test` uses fixtures/fakes and never mutates a real Archidekt deck. |
| REQ-010 | Should | Product follow-up work shall be captured in separate PLCs. | Trust evidence is aligned and configurable-decision/provider-evidence packets exist. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Maintainability | Shallow control flow is analyzer-enforced; comments are useful rather than generated filler. |
| Testability | Every production assembly meets the line-coverage gate with behavior-focused tests. |
| Safety | Apply-only tools are absent when no operation-mode configuration is supplied. |
| Compatibility | Explicit `read-only`, `plan`, and `apply` behavior remains stable. |
| Traceability | Every Must requirement maps to a phase and validation check. |

## Traceability

| Requirement | Design | Validation |
| --- | --- | --- |
| REQ-001–REQ-004 | Documentation foundation | Link review and `git diff --check` |
| REQ-005–REQ-006 | Analyzer and Task design | `task format:check`, `task lint` |
| REQ-007 | Coverage design | `task coverage` |
| REQ-008 | Operation-mode design | App unit and E2E tests |
| REQ-009 | Test architecture | `task test` |
| REQ-010 | Follow-up packets | Packet inspection |

## Definition Of Done

- [ ] Every Must requirement has objective evidence.
- [ ] All production assemblies meet the 90 percent line gate.
- [ ] The default MCP process runs in plan mode.
- [ ] The strict lint and full CI workflows pass.
- [ ] Follow-up product work remains outside this implementation.
