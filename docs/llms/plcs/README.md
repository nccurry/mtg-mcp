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
  requirements and design here, but the packet is never implementation
  authority by itself. For the rewrite, explicit owner authorization,
  independent approval, and `Implementation authorized: Yes` are all required.
- `in-progress/`: PLC packets actively guiding code changes. Move a packet here
  before the first implementation edit, then keep phase status, scope changes,
  decisions, and validation evidence current.
- `completed/`: PLC packets that are validated, landed, abandoned with a clear
  outcome, or superseded. Completed packets are historical context, not a
  stronger source of truth than current code and tests.

The PLC root is for lifecycle guidance. Active PLC packets belong in the
lifecycle folders.

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

The evidence-first rewrite additionally follows
[`docs/rewrite-guide.md`](../../rewrite-guide.md): legacy code is reference
evidence, the umbrella owns cross-child guardrails, and only the active approved
child may direct implementation.
