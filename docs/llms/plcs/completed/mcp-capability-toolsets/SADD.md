# MCP Capability Toolsets Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Executive Summary

App gains one explicit static registry of implemented capability descriptors.
Configuration resolves a selection before stdio opens; the host registers only
selected groups and then applies the existing mode filter. The capability
resource projects that same registry, preventing a second surface inventory.

The design deliberately rejects runtime switching and a generic router. A
restart is cheaper and more reliable than depending on uneven client cache and
list-change behavior, while typed tools retain precise schemas.

## Chosen Design

### Registry

`MtgMcp.App` owns a closed `CapabilityToolset` category and an ordered registry
of implemented descriptors. Each descriptor contains its stable name,
default-enabled flag, availability projection, read registration group, write
registration group, and exact mode-aware visible count. No descriptor or MCP
SDK type enters Core.

Current `DeckReadTools`, `DeckWriteTools`, `DeckInterchangeReadTools`, and
`DeckInterchangeWriteTools` compose the single `decks` descriptor. Later
children add descriptors at the App composition root rather than editing
selection logic.

### Configuration Resolution

Existing JSON/environment/CLI precedence yields one raw string. Parsing trims
surrounding whitespace, rejects blank segments and duplicates, and recognizes
either one reserved selection or explicit exact names. Explicit names are
canonicalized in registry order. Omitted input resolves to `default`.

The data root and toolset availability inspection remain non-mutating.
Configuration errors are sanitized before transport, exactly like existing
mode and data-root failures.

### Registration Flow

```text
validated configuration
        |
        v
resolve static selection -----> capability projection
        |
        v
implemented descriptor registry
        |
        v
selected descriptors
        |
        v
mode-filtered read/write registration
        |
        v
static tools/list for session
```

Mode guards still execute inside every write wrapper. Toolset filtering reduces
context; it is not an authorization boundary.

### Capability Resource

Schema version 2 removes the overlapping `modules` collection and adds one
`toolsets` object. `items` contains implemented descriptors only, in registry
order. Disabled descriptors remain visible as metadata with `enabled=false`;
unimplemented planned capabilities do not appear. Rows identify availability,
stability, default membership, exact visible count, and a description that
separates relevance from authority. Experimental descriptors are selectable
only by exact name and never enter `default` or `all`.

### Tool Versus Resource Decision

Toolset discovery is server metadata, so it belongs in the existing capability
resource rather than a new tool. Capability operations remain typed tools when
the model must supply parameters, perform calculations, query providers, or
mutate state. Later children must separately justify addressable immutable data
that might be better exposed as a resource template.

## Alternatives Considered

| Option | Decision | Reason |
| --- | --- | --- |
| Advertise every tool | Rejected | Complete access becomes the default context and degrades selection. |
| Split into six executables | Rejected | Duplicates configuration and complicates shared local deck identity. |
| Generic `mtg(action, payload)` router | Rejected | Hides schemas and makes model selection less reliable. |
| Runtime toolset switching with `listChanged` | Rejected | Adds state/cache complexity and uneven client behavior. |
| Per-tool allow/exclude lists | Deferred | Capability groups solve the stable requirement with less policy surface. |
| Static startup toolsets | Chosen | Predictable discovery, small default context, and complete opt-in access. |

## Failure Modes

| Failure | Result |
| --- | --- |
| Blank, duplicate, unknown, or mixed-reserved selection | Sanitized invalid configuration before stdout/transport. |
| Enabled provider lacks credentials | Toolset remains selected; capability row and tool results report unavailable without a secret. |
| Direct write wrapper invoked outside registration | Existing operation-mode guard rejects it. |
| Descriptor count differs from discovery | Architecture/E2E reconciliation fails. |
| Child adds an unassigned tool | Architecture test fails before merge. |

## Project Boundaries

Core and Decks gain no toolset dependency. App owns parsing, descriptor
registration, mode intersection, and public capability projection. Tests own
exact manifests and official-client matrices. No package dependency is added.

## Test Architecture

- Unit tests cover parsing, precedence, canonical ordering, reserved names,
  duplicates, and sanitized errors.
- App tests cover descriptor resolution, mode intersection, and direct guards.
- Architecture tests prove one assignment per tool, no assembly scanning,
  no Core/toolset dependency, and no `listChanged` advertisement.
- Official-client E2E covers omitted/default, all, none, and permuted explicit
  selection in every mode.
- Package smoke repeats default, all, and none against the installed tool.

## Decisions, Risks, And Deferred Work

| Item | Type | Resolution |
| --- | --- | --- |
| Default profile grows as capabilities land | Risk | Only descriptors explicitly marked default-enabled enter it; cutover reviews the final default manifest. |
| Client ignores capability metadata | Risk | Actual registration is filtered; correctness does not depend on client interpretation. |
| Semantic tool search | Deferred | Host-specific feature, not required for stable server correctness. |
| Individual tool filtering | Deferred | Reconsider only with observed workflow evidence after cutover. |
