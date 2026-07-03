# Agent Quality Foundation Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Design Drivers

- Keep Core free of runtime third-party dependencies.
- Make instructions concise and mechanically enforceable.
- Preserve C# 15 preview support, including union types.
- Keep MCP mutation explicit and normal tests offline.
- Raise coverage through useful tests rather than exclusions.

## Chosen Design

Repository-wide rules remain in root `AGENTS.md`; common C# and adapter rules
remain in `src/AGENTS.md`; Core and App gain narrow project-local files. Human
product principles live in `docs/`, while agent workflow and templates live in
`docs/llms/`.

Analyzers are central build-only package references. StyleCop is restricted to
documentation because its older formatting rules do not understand all modern
C# shapes reliably. Built-in rules, Roslynator, Meziantou, and Sonar provide
the remaining style, correctness, and complexity signals.

Coverage uses one canonical report and an explicit list of eight production
assemblies. Each assembly is evaluated independently. Branch and method values
are reported but are not threshold gates in this packet.

The default operation-mode value and empty-value normalization both become
`plan`; explicit aliases keep their current behavior. Tool registration remains
deterministically sorted and filtered by the resolved mode.

## Alternatives Considered

| Option | Decision | Reason |
| --- | --- | --- |
| One AGENTS file per adapter | Rejected | Repeats shared provider guidance and increases drift. |
| Built-in analyzers only | Rejected | Cannot enforce all-member documentation or nesting as requested. |
| Aggregate 90 percent coverage | Rejected | Lets strong projects conceal weak assemblies. |
| Default read-only mode | Rejected | Blocks useful local planning-state workflows. |
| General-purpose rules engine in Core | Rejected | Violates the dependency-light and non-rules-engine north star. |

## Building Blocks

| Building block | Responsibility |
| --- | --- |
| Agent and LLM docs | Route agents to authoritative rules and current architecture. |
| North-star docs | Distinguish facts, evidence, mathematics, sampled estimates, and heuristics. |
| Planning templates | Produce concise, requirements-backed, phased PLC packets. |
| Analyzer configuration | Enforce documentation, formatting, unused code, and shallow control flow. |
| Coverage workflow | Produce per-assembly metrics and fail below 90 percent. |
| Operation-mode default | Require explicit apply configuration for mutations. |

## Data And Public Contracts

No wire schema changes are planned. `MtgMcpOptions.OperationMode` changes its
default from `apply` to `plan`, and normalization of null/empty values follows
the same rule. The migration path is explicit `MTGMCP__OPERATION_MODE=apply`.

## Failure Modes

- Analyzer incompatibility with preview syntax: disable only the incompatible
  rule family or ID and retain documentation enforcement.
- Coverage instrumentation omission: fail when any expected assembly is absent.
- Comment noise: reject known generated phrases and avoid mandatory tags that
  add no information.
- Hidden mutation regression: verify default advertised tools and process-level
  behavior without an operation-mode variable.

## Test Architecture

Use architecture tests for dependency and comment-quality invariants, App tests
for operation-mode and tool registration behavior, fixture-backed adapter tests,
Core unit tests, and mocked MCP E2E tests. The canonical coverage report consumes
all non-live tests and gates every source assembly.

## Decisions And Deferred Work

Trust evidence, configurable decision models, and provider evidence workflows
are separate PLCs. They may use C# unions for payload-bearing closed outcomes,
but this packet does not perform a blanket model migration.
