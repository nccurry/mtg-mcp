# Core Instructions

Root and `src/AGENTS.md` remain authoritative. This file adds defaults for
`MtgMcp.Core`.

## Boundary

- Keep Core free of runtime third-party package references and independent of
  App, adapters, HTTP payloads, MCP SDK types, configuration binding, and host
  services.
- In the rewrite, model only dependency-light provider-neutral evidence,
  identifiers, failures, and shared contracts assigned by the active child.
  Deck persistence belongs in Decks and exact mathematics in Statistics.
- Legacy planning, intent, recommendation, scoring, and simulation types may be
  maintained while the current server exists, but do not expand or transplant
  them into stable `0.9.0`.
- For current maintenance, keep I/O behind small existing contracts. For the
  rewrite, introduce only the contracts assigned by the active child so
  behavior stays testable without treating legacy interfaces as mandatory.

## Domain Design

- Prefer immutable inputs and deterministic pure computations where practical.
- Return explicit assumptions, warnings, provenance, and fingerprints for
  evidence and exact derivations. Sampled/heuristic metadata applies only to an
  independently approved experimental capability.
- Use unions for payload-bearing closed outcomes and exhaustive switches. Use
  enums for labels without case-specific state and records for independent data.
- Do not add a generic rules engine, arbitrary expression evaluator, advisor
  policy, or stable simulation profile. Experimental work requires its own
  approved post-cutover PLC.

## Validation

- Add focused unit tests for success, failure, unsupported, and boundary cases.
- Keep tests offline and deterministic, and preserve Core's 90 percent line
  coverage floor.
