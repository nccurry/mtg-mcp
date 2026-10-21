# MtgMcp.Core — Agent Instructions

The root `AGENTS.md` and `src/AGENTS.md` still apply. This file adds rules for
`src/MtgMcp.Core/`.

## What this folder owns

Dependency-light, provider-neutral evidence, identifiers, failures, and shared
contracts. Deck persistence belongs in Decks and exact mathematics in
Statistics.

## Rules

- Keep Core free of runtime third-party package references and independent of
  App, adapters, HTTP payloads, MCP SDK types, configuration binding, and host
  services.
- Do not restore legacy planning, intent, recommendation, scoring, or
  simulation types.
- Keep I/O behind small contracts so behavior stays testable. Removed legacy
  interfaces are not required.
- Prefer immutable inputs and deterministic pure computations where practical.
- Return explicit assumptions, warnings, provenance, and fingerprints for
  evidence and exact derivations. Sampled or heuristic metadata applies only to
  a separately approved experimental capability.
- Do not add a generic rules engine, arbitrary expression evaluator, advisor
  policy, or stable simulation profile. Experimental work needs its own
  approved PLC.

## Checks

Add focused unit tests for success, failure, unsupported, and boundary cases.

```sh
task test:unit
task coverage:core
```
