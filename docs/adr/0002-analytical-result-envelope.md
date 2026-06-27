# ADR 0002: Analytical Result Envelope

Status: Accepted

Date: 2026-06-27

## Context

Analytical tools currently return useful but inconsistent metadata names for
status, warnings, assumptions, source context, and deterministic replay facts.
Phase 2 owns the public contract vocabulary; later phases can type and expose
schemas without inventing competing envelopes.

## Decision

Analytical MCP results should converge on this top-level vocabulary when a tool
returns a shaped analytical result:

- `status`: short machine-readable outcome such as `ok`, `partial`,
  `not-applicable`, or a domain-specific unavailable state.
- `warnings`: bounded user-visible risks or caveats.
- `assumptions`: bounded model or simulation assumptions.
- `sources`: bounded source/provenance rows when external evidence is used.
- `determinism`: replay metadata such as model name/version, seed, RNG kind,
  fingerprints, and profile resolution when deterministic analysis is involved.
- Domain payload fields remain named for the tool, such as `mana`,
  `consistency`, `performance`, `goldfish`, or `recommendations`.

Existing result types migrate opportunistically when touched for Phase 3 schema
work, Phase 4 closed-set typing, or Phase 7 analytical changes. Do not wrap
every existing response in a generic envelope object just to satisfy the ADR.

## Consequences

New analytical tools have a stable vocabulary, and existing tools can converge
without one high-risk model-wide rewrite.

## Alternatives Considered

- Rewrite every analytical return type in Phase 2. This is a large behavioral
  diff with little immediate user value.
- Use one generic `data` field for all domain payloads. That hides useful
  tool-specific structure and makes output schemas less helpful.
