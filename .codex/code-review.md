# Code Review Checklist

Use this checklist for broad, risky, or review-only changes. Findings should be
specific, actionable, and tied to files and lines when reporting a review.

## Correctness

- Does the change satisfy the user-visible behavior, not just compile?
- Are failure paths, invalid inputs, cancellation, disposal, and state
  transitions handled deliberately?
- Are mutating MCP tools protected by `OperationModeGuard`?
- Are tool, resource, prompt, annotation, and operation-mode changes reflected
  in surface tests?
- Do performance, simulation, and recommendation results preserve assumptions,
  confidence, warnings, deterministic inputs, and source metadata?

For an evidence-first rewrite change, also verify that the child is authorized,
the implementation matches its approved manifest, and no legacy advisor,
intent, recommendation, weak-card, blended-score, prompt, or strategic-
simulation surface was retained without an umbrella amendment.

## Boundaries

- Does `MtgMcp.Core` remain free of adapter and host references?
- Do adapter projects own third-party HTTP request and response contracts?
- Are provider-specific auth, pacing, retry, cache, and error handling kept in
  the owning adapter unless a shared Core primitive already exists?
- Are new dependencies justified against existing helpers and placed at the
  correct boundary?
- For rewrite code, do Decks, Statistics, provider adapters, and App own the
  responsibilities assigned by the active child instead of accumulating in
  Core?

## Abstraction Quality

- Can any new type, interface, helper, factory, registry, or manager be deleted
  without losing clarity?
- Does each abstraction remove real duplication, clarify ownership, or match a
  proven local pattern?
- Is behavior traceable without long pass-through call chains?
- Are obsolete, duplicate, or superseded paths removed when replacement work
  lands?

## C# Style

- Does the code follow local naming, XML comment, and layout conventions?
- Are loops used for multi-step behavior instead of complex multi-line LINQ?
- Are `CancellationToken` and `ConfigureAwait(false)` handled consistently in
  async library code?
- Are C# 15 features used only where they make domain shapes or call sites
  clearer?

## Tests

- Do tests prove observable behavior and important regressions?
- Are tests deterministic and free of network, real Archidekt mutation,
  machine-global state, and wall-clock timing?
- Do adapter tests use fixtures or fake HTTP where practical?
- Do architecture tests enforce durable invariants rather than prose snapshots?

## Dependencies And Security

- Are secrets absent from code, docs, tests, logs, scan artifacts, and sample
  files?
- Are provider errors sanitized?
- Are new dependencies considered for license, security, and package-boundary
  impact?

## Validation And Handoff

- Was the narrow relevant check run before broader gates?
- Were public surface, architecture, benchmark, or docs checks included when
  needed?
- Were skipped commands reported with reasons?
- Were generated artifacts left unmodified unless intentionally targeted?
