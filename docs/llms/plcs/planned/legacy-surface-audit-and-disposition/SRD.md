# Legacy Surface Audit And Disposition Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner and rewrite-foundation author
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Parent program: [Evidence-First MCP Rewrite Program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Executive Summary

The audit must establish what the current application actually exposes, what
evidence supports each capability, and which outcomes belong in the rewrite.
It prevents a clean rewrite from either copying accidental architecture or
silently losing valuable fixtures and exact behavior.

## Scope And Non-Scope

In scope are all registered MCP tools, resources, prompts, production projects,
provider adapters, repositories, caches, background workflows, task claims,
fixture families, existing PLCs and ordinary plans that overlap the rewrite,
and already documented correctness defects. Out of scope are production
deletion, new schemas, implementation estimates, and compatibility migration.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| AUD-001 | Must | Inventory every registered MCP tool, resource, and prompt by stable name. | FIXTURES lists 118 tools, 16 resources, and 18 prompts without duplicates. |
| AUD-002 | Must | Inventory every production project and provider adapter. | All eight production projects have ownership and disposition. |
| AUD-003 | Must | Inventory durable persistence, caches, configuration, and background execution. | JSON stores, corpus cache modes, data directory, and absence of background services are recorded. |
| AUD-004 | Must | Classify each public surface as rebuild, remove, experimental, unsupported, misleading, or fixture-only. | Every surface name appears in exactly one disposition row. |
| AUD-005 | Must | Identify behavior that claims more confidence or coverage than evidence supports. | Tagger, Playgroup ranking, live tests, simulation, and known analysis gaps are documented with source locations. |
| AUD-006 | Must | Produce deletion and reuse allowlists without authorizing code edits. | README lists both and approval remains planning-only. |
| AUD-007 | Must | Separate production reachability from test/fixture-only value. | Reuse candidates name fixtures and test vectors, not whole services. |
| AUD-008 | Must | Preserve the umbrella product boundary. | Stable recommendations, intent, and advisor prompts are classified for removal. |
| AUD-009 | Must | Record validation gaps honestly. | The locked surface-report build is recorded, and static evidence is identified. |
| AUD-010 | Must | Classify every existing planned, partial, and relevant completed PLC or ordinary plan that overlaps the rewrite. | The disposition matrix names owner, disposition, target child or post-cutover slug, approval action, and blocking status for every known overlapping packet. |
| AUD-011 | Must | Identify cross-document conflicts that must be amended before foundation implementation. | Operation-mode and `0.9` clean-break conflicts are recorded as blockers or review items before child 2 consumes this audit. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Completeness | Registered surface counts match source attributes and checked-in inventory tests. |
| Traceability | Findings link to concrete files, tools, or existing defect PLCs. |
| Conservatism | Uncertain reuse is not pre-approved; provider drift is not guessed. |
| Safety | The packet changes no production behavior or provider state. |
| Maintainability | Foundation authors can consume one explicit deletion and reuse policy. |

## Traceability

| Requirement | Design | Evidence |
| --- | --- | --- |
| AUD-001, AUD-004 | SADD surface classification | FIX-SURFACE-TOOLS, FIX-SURFACE-RESOURCES, FIX-SURFACE-PROMPTS |
| AUD-002 | SADD project disposition | FIX-PROJECTS |
| AUD-003 | SADD state and workflow inventory | FIX-PERSISTENCE |
| AUD-005, AUD-009 | SADD trust findings | FIX-GAPS |
| AUD-006, AUD-007 | README allowlists | Review inspection |
| AUD-008 | SADD stable-boundary decision | Surface disposition review |
| AUD-010, AUD-011 | SADD existing-PLC disposition | FIX-PLC-DISPOSITION |

## Risks And Assumptions

- Attribute registration and reflection mean apparent call-site absence does
  not imply dead code; the audit follows registered entry points.
- External provider facts can drift and must be re-verified in their child PLC.
- Existing tests may prove current behavior without proving that behavior
  belongs in the new product.
- A disposition of rebuild preserves the user outcome, not the current schema
  or implementation.

## Validation

- Compare source attributes, ToolRegistry registration, README inventory, and
  MCP surface tests.
- Search test sources for the live category filter and annotation.
- Inspect host DI composition and every production project.
- Review the existing-PLC disposition matrix before moving, editing, or
  superseding any other PLC solely because of the rewrite.
- Resolve packet links and run `git diff --check`.
- Do not run provider calls or production mutations.

## Definition Of Done

- [ ] Every public surface is classified.
- [ ] Projects, persistence, providers, and workflow execution are inventoried.
- [ ] Trust gaps and known defects are concrete.
- [ ] Existing PLC and ordinary-plan dispositions are reviewed.
- [ ] Deletion and reuse allowlists are reviewed.
- [ ] No production implementation is included.
