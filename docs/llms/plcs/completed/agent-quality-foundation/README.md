# Agent Quality Foundation PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/agent-quality-foundation/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: completed

## Summary

This packet establishes the repository's durable north star, agent guidance,
planning templates, C# quality gates, 90 percent per-assembly line coverage,
and least-privilege MCP default. It changes development infrastructure and the
default operation mode without changing tool names or result schemas.

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance criteria.
- [SADD.md](SADD.md): architecture and quality-gate design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): independently green phases.
- [FIXTURES.md](FIXTURES.md): documentation, configuration, surface, and coverage checks.

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Default an unspecified operation mode to `plan`. | Accepted | Local planning stays useful while deck and remote mutations require explicit consent. |
| Require 90 percent line coverage per production assembly. | Accepted | Aggregate coverage must not hide a weak project. |
| Use the full tuned Fabrial analyzer stack. | Accepted | Analyzer-backed enforcement replaces fragile source-text checks. |
| Document every named C# member. | Accepted | IDE readers should understand private and test code without requiring comments on obvious lambdas. |
| Add only Core and App project-scoped agent files. | Accepted | These boundaries need specialized rules without duplicating adapter guidance. |

## Project And Surface Impact

The packet affects repository guidance, docs, Task commands, analyzer package
references, coverage configuration, architecture tests, MCP configuration
defaults, App tests, Core and adapter tests, and CI. No MCP tool, resource, or
prompt names are removed.

## Current Open Questions

None. Implementation choices are fixed by the SRD and SADD.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable.
- [x] Alternatives and tradeoffs are recorded.
- [x] Project boundaries and public behavior are explicit.
- [x] Implementation phases have exit criteria.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Baseline `task lint` | Passed | Existing built-in analyzer build is green. |
| 2026-07-03 | `dotnet format --verify-no-changes` | Passed | Existing formatting baseline is green. |
| 2026-07-03 | MCP surface report | Passed | 118 tools, 16 resources, 18 prompts; all tools have titles and descriptions. |
| 2026-07-03 | Documentation foundation | Passed | North-star docs, scoped guidance, LLM index, templates, whitespace, and relative links validated. |
| 2026-07-03 | Strict analyzer and format gate | Passed | `task lint` passed with build-only analyzers and all-member documentation enforcement. |
| 2026-07-03 | Plan-mode default tests | Passed | Focused App and process-level MCP tests verified plan defaults and explicit apply compatibility. |
| 2026-07-03 | Canonical coverage measurement | Passed | All eight production assemblies exceed the 90 percent line gate; App is 90.52 percent. |
| 2026-07-03 | Follow-up product PLCs | Passed | Trust evidence aligned; configurable decision and provider evidence packets created. |
| 2026-07-03 | Final acceptance | Passed | `git diff --check`, instruction discovery, format, lint, focused tests, `task test`, `task coverage`, surface report, MCP smoke, and `task ci` passed. |

## Completion Notes

The repository now has durable north-star and agent guidance, a reusable
five-file PLC template, strict analyzer-backed documentation and complexity
checks, a least-privilege plan default, and 90 percent per-assembly line gates.
The product follow-up packets remain planned and intentionally unimplemented.
