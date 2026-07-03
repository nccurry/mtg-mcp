# <Feature Name> Software Architecture And Design Document

The SADD explains how the requirements are realized and why the selected design
wins over its alternatives. Keep the simple path first. Delete optional
sections that do not apply instead of padding the packet.

## Document Control

- Lifecycle status: Planned | In progress | Completed
- PLC packet: <link to packet README>
- Owner: mtg-mcp
- Reviewers: <names or roles>
- Last updated: <YYYY-MM-DD>
- Related SRD: <link>
- Related implementation plan: <link>

## Revision History

| Date | Author | Summary of change |
| --- | --- | --- |
| <YYYY-MM-DD> | <Author> | Initial draft |

## Executive Summary

Describe the chosen architecture in a few paragraphs. Call out the highest-value
decision, the main constraint, and the most important rejected alternative.

## Goals, Non-Goals, And Design Drivers

List architecture goals, non-goals, project boundaries, dependency constraints,
compatibility targets, quality attributes, and implementation constraints from
the SRD.

## Context And Scope

Describe upstream MCP clients, downstream providers, file formats, generated
artifacts, runtime hosts, test hosts, external interfaces, and repo boundaries.
State what this design intentionally does not cover.

## Constraints

List language, package, dependency, compatibility, security, performance, and
organizational constraints that the design must respect.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| <Option A> | <Approach> | <Pros> | <Cons> | <Chosen/rejected/deferred> |

## Chosen Design

Explain the selected design and why it best satisfies the SRD. Include the
simple path first, then extension points only where known requirements need
them.

## Data Design

Describe state representations, persistence or cache formats, serialization,
versioning, migrations, retention, and ownership when the change affects data.

## Building Blocks

Describe the static decomposition into projects, modules, types, or components.

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| <Type/module> | <Responsibility> | <Owned state/resources> | <API/events/files> | <Dependencies> | <Test coverage> |

## Runtime And Data Flow

Describe the main workflows, sequencing, cache invalidation, retries,
cancellation, error handling, and state transitions. Use diagrams when they
clarify the flow.

## MCP Surface, Schemas, And Diagnostics

Sketch the public MCP tools, resources, prompts, annotations, detail levels,
options, records, schemas, and diagnostics that implementation branches must
converge on. Record operation-mode visibility for read, plan, and apply modes.

## Adapter And Provider Contracts

For provider work, document request/response ownership, auth, user-agent,
pacing, retries, caching, rate limits, error sanitization, fixture strategy,
permission sensitivity, and live-test boundaries.

## Error Handling And Failure Modes

Describe expected failures, how they are classified and sanitized, what callers
observe, retry or recovery behavior, and which failures remain fatal.

## Cross-Cutting Concepts

Document decisions that apply across multiple building blocks, such as
determinism, workspace compatibility, local persistence, cancellation, disposal,
logging, source attribution, cache invalidation, confidence, warnings, privacy,
security, and generated artifact handling.

## Project Boundaries

Describe how the design preserves Core/App/adapter/test layering. Record any
new project reference, dependency, or boundary exception and how architecture
tests will enforce it.

## Readability And Documentation

Describe naming, XML comments, abstraction reuse, docs updates, and code-removal
expectations that implementation agents must preserve.

## Quality Attribute Design

Explain how the design satisfies determinism, performance, maintainability,
testability, diagnostics, compatibility, provider safety, and usability
requirements.

| Requirement | Design response | Validation |
| --- | --- | --- |
| <REQ ID> | <How the design satisfies it> | <Test or inspection> |

## Implementation Phases

Break implementation into deliverables that can be reviewed independently. Keep
each phase small enough to validate with focused checks before broad gates.

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| <Phase 1> | <Files/projects> | <REQ IDs> | <Validation and review evidence> |

## Test Architecture

Map requirements to unit tests, fixture-backed adapter tests, architecture
tests, mocked MCP end-to-end tests, calibration tests, benchmark dry runs, and
manual checks. Include edge cases and failure modes.

## Framework And External Notes

If this work depends on MCP SDK behavior, provider APIs, Scryfall, Archidekt,
Moxfield, Playgroup.gg, Commander Spellbook, TopDeck, EDHREC-style data,
EDHTop16 data, or another external system, record the source paths, contracts,
constraints, and decisions that future agents need to understand.

## Decisions, Risks, And Deferred Work

Record accepted architecture decisions, rejected alternatives, known risks,
compatibility gaps, and explicit later-phase work.

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| <Item> | <Decision/Risk/Deferred> | <Impact> | <Resolution or owner> |

For architecture decisions, record context, selected option, and consequences
so a later packet can revisit the decision without reconstructing it.

## Glossary

Define project-specific terms that future agents must use consistently.
