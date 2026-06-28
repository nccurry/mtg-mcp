# <Feature Name> Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: <link to packet README>
- Owner: mtg-mcp
- Reviewers: <names or roles>
- Last updated: <YYYY-MM-DD>
- Related SADD: <link>
- Related implementation plan: <link>

## Executive Summary

State the capability, target users or maintainers, expected outcome, and why
these requirements belong in a durable PLC instead of a local scratch plan.

## Audience

Name the people or agents who should use this document and what they should know
before reading it.

## References

List source-of-truth docs, code paths, project references, provider docs,
schemas, standards, and external references. Prefer stable official references
for external systems.

## User And Maintainer Outcomes

Describe the user-facing, agent-facing, or maintainer-facing outcomes this work
must enable. Include success signals that can be observed during review,
testing, MCP inspection, or provider fixture validation.

| Outcome | Success signal | Notes |
| --- | --- | --- |
| <Outcome> | <Measurable or observable signal> | <Notes> |

## System Overview

Describe the system from the user's or maintainer's point of view, the repo and
project boundaries, and the main workflows the software must support.

## Scope And Non-Scope

- In scope: <capabilities that must be delivered>
- Out of scope: <nearby capabilities intentionally deferred>
- Compatibility target: <MCP clients, provider APIs, config keys, data files,
  workspace formats, docs, or downstream users>
- Explicit non-goals: <things a reader might reasonably expect but this PLC will
  not cover>

## Stakeholders And Affected Systems

List users, maintainers, projects, adapters, provider services, MCP clients,
docs, generated artifacts, data formats, caches, and test suites affected by
this work.

## Requirements

Use stable requirement IDs and testable statements. Recommended priority values
are Must, Should, Could, and Later. Requirements should be clear, atomic,
implementation-neutral, traceable, and objectively verifiable.

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| REQ-001 | Must | Functional | The software shall <observable behavior>. | <Why this matters> | <How this is verified> |

## Requirement Quality Checklist

- [ ] Every Must requirement has acceptance criteria.
- [ ] Every requirement states one behavior or constraint.
- [ ] Requirements avoid vague phrases unless paired with measurable criteria.
- [ ] Implementation details appear only when they are true constraints.
- [ ] TBD/TBR items include owner, reason, and resolution plan.

## Interfaces, Data, States, And Modes

Record required MCP tools/resources/prompts, operation-mode visibility, config
keys, file formats, schemas, cache ownership, state transitions, diagnostics,
and error surfaces. Avoid implementation details that belong only in the SADD.

## Quality Attributes

Capture measurable or testable requirements for determinism, performance,
maintainability, usability, compatibility, security, privacy, diagnostics,
offline testability, and provider safety.

| Attribute | Scenario | Measure |
| --- | --- | --- |
| <Attribute> | <When/stimulus/context/response> | <Pass/fail measure> |

## Phased Delivery

List implementation phases with the minimum shippable behavior and validation
expected for each phase.

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| <Phase 1> | <Goal> | <REQ IDs> | <Validation and review evidence> |

## Traceability

Map requirements to planned design sections and validation categories.

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| REQ-001 | <SADD section> | <unit/integration/e2e/docs/manual> | <test, command, or inspection> |

## Risks, Assumptions, And Open Questions

List assumptions, open questions, deferred compatibility targets, and risks that
could alter scope.

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| <Item> | <Risk/Assumption/Question/TBD/TBR> | <Impact> | <Owner> | <Plan> |

## Validation

List the commands, tests, fixtures, inspections, and review checks required
before implementation branches can be considered complete.

## Definition Of Done

- [ ] Must requirements are implemented or explicitly deferred by the owner.
- [ ] Acceptance criteria are satisfied with objective evidence.
- [ ] Traceability and validation notes are current.
- [ ] SADD reflects the implemented design.
- [ ] Remaining risks and follow-up work are recorded.
