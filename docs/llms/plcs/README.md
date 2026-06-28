# Plan-Led Changes

A Plan-Led Change, or PLC, is a requirements-backed planning packet for work
that is too large or cross-cutting for an ordinary implementation plan. Use a
PLC when a change crosses project boundaries, introduces or changes public MCP
tools/resources/prompts, changes operation modes, changes adapter HTTP
contracts, affects persistence formats, changes Stats Lab or simulation
assumptions, changes source-provider evidence semantics, or needs phased
delivery.

## Lifecycle Folders

- `planned/`: PLC packets being drafted, reviewed, or queued. Agents may update
  requirements and design here, but should not treat the packet as permission to
  start implementation unless the user asks for implementation.
- `in-progress/`: PLC packets actively guiding code changes. Move a packet here
  before the first implementation edit, then keep phase status, scope changes,
  decisions, and validation evidence current.
- `completed/`: PLC packets that are validated, landed, abandoned with a clear
  outcome, or superseded. Completed packets are historical context, not a
  stronger source of truth than current code and tests.

The PLC root is for lifecycle guidance and templates. Active PLC packets belong
in the lifecycle folders.

## Packet Shape

Create each new PLC as a folder named with a short kebab-case slug:

```text
docs/llms/plcs/planned/<feature-slug>/
  README.md
  SRD.md
  SADD.md
  IMPLEMENTATION_PLAN.md
  FIXTURES.md
```

Use `PLC-README-template.md`, `SRD-template.md`, `SADD-template.md`,
`IMPLEMENTATION_PLAN-template.md`, and `FIXTURES-template.md` as starting
points. Delete sections that truly do not apply, but do not remove lifecycle
status, scope, traceability, validation, or completion notes.

## Planning Readiness

Before moving a packet from `planned/` to `in-progress/`, check that:

- The packet README names lifecycle status, owner, decision summary, and active
  open questions.
- The SRD states audience, purpose, scope, non-scope, outcomes, testable
  requirements, acceptance criteria, risks, assumptions, and validation
  expectations.
- The SADD states the chosen design, constraints, alternatives considered,
  building blocks, runtime/data flow, public surfaces, lifetimes, error paths,
  project boundaries, and test architecture.
- MCP surface changes document tools, resources, prompts, annotations,
  operation-mode visibility, and expected surface tests.
- Adapter changes document provider contract ownership, auth, pacing, retries,
  cache behavior, error sanitization, and fixture strategy.
- Stats Lab, simulation, and recommendation changes document assumptions,
  confidence, warnings, deterministic seeds, source metadata, and calibration
  impact.
- Must-have requirements map to acceptance criteria and at least one objective
  verification method.
- Deferred work is explicit and does not hide a requirement needed for the next
  phase.

## Implementation Discipline

Agents implementing an in-progress PLC should:

- Read the packet README first, then SRD, SADD, IMPLEMENTATION_PLAN, and
  FIXTURES when present.
- Keep implementation scoped to the current phase unless the user expands
  scope.
- Update the packet when implementation reveals a changed requirement,
  different design choice, new risk, or deferred item.
- Preserve Core, App, adapter, and test boundaries.
- Add validation evidence before marking a phase complete.
- Move the packet to `completed/` only after validation is done or the closure
  reason is recorded.

## Template Basis

The templates combine lightweight product, requirements, and architecture
practices:

- Requirements docs should capture purpose, scope, system overview, functional
  and quality requirements, interfaces, data, verification, validation, and
  maintenance.
- Architecture docs should capture design decisions, decomposition, interfaces,
  runtime/data flow, quality attributes, rationale, and traceability.
- Durable design docs should put audience, scope/non-scope, key decisions,
  alternatives, tradeoffs, and validation evidence up front.
