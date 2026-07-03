# Core Instructions

Root and `src/AGENTS.md` remain authoritative. This file adds defaults for
`MtgMcp.Core`.

## Boundary

- Keep Core free of runtime third-party package references and independent of
  App, adapters, HTTP payloads, MCP SDK types, configuration binding, and host
  services.
- Model provider-neutral deck, card, evidence, analysis, planning, and
  simulation concepts. Adapter projects own translation into these models.
- Keep I/O behind small existing contracts so domain behavior stays testable
  with in-memory repositories and fixture data.

## Domain Design

- Prefer immutable inputs and deterministic pure computations where practical.
- Return explicit assumptions, warnings, provenance, confidence, model version,
  seed, and fingerprints for evidence and sampled results that need them.
- Use unions for payload-bearing closed outcomes and exhaustive switches. Use
  enums for labels without case-specific state and records for independent data.
- Do not add a generic rules engine or arbitrary expression evaluator. Extend
  typed simulation profiles and policy primitives only when a requirement
  proves the need.

## Validation

- Add focused unit tests for success, failure, unsupported, and boundary cases.
- Keep tests offline and deterministic, and preserve Core's 90 percent line
  coverage floor.
