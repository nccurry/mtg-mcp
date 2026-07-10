# Exact Deck Statistics Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-09
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Status | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- | --- |
| 0 | Complete | Reconcile independent review, remove format-specific deck validation, approve/activate the packet, and lock exact public contracts/oracles. | STAT-012, STAT-022, STAT-023, STAT-025 | Packet is approved in `in-progress/`; custom-format deck validation is structural only; schemas, counts, bounds, and oracle strategy are explicit. |
| 1 | Complete | Add Statistics project wiring, exact rational/combinations, work budget, population contracts, result contracts, and univariate hypergeometric engine. | STAT-001, STAT-002, STAT-013 through STAT-017, STAT-024, STAT-025 | Known vectors, exhaustive oracle, serialization, culture, and budget tests pass; project boundaries and focused coverage wiring pass. |
| 2 | Complete | Add canonical membership buckets, multivariate observations, and one-use package allocation. | STAT-003, STAT-004, STAT-008, STAT-024 | Exhaustive overlapping-group and allocation comparisons pass; no double counting or partial result exists. |
| 3 | Complete | Add explicit turn tables, mana allocation, and closed monotone inverse count analysis. | STAT-005 through STAT-007, STAT-010, STAT-018, STAT-024 | Draw-schedule, allocation, complement, lower-bound, neighbor, and rejection fixtures pass. |
| 4 | Complete | Add explicit mulligan attempts and stored-field/caller-value deck summaries. | STAT-009, STAT-011, STAT-012, STAT-019, STAT-020, STAT-023, STAT-024 | Exhaustive attempts, bottoming, selector, nearest-rank, missing-value, zone-partition, and custom-format tests pass. |
| 5 | Complete | Register the default `stats` toolset and prove the complete exact-evidence MCP workflow. | All | Eight tools, 90-tool all surface, profile/mode matrices, official-client schemas, realistic deck workflow, coverage, packaging, smokes, and all audits pass. |

## Inter-Phase Audit Gate

After each phase:

1. run the focused Statistics tests plus affected App/architecture tests;
2. inspect abstraction ownership and remove generic manager/helper layers;
3. inspect naming, control flow, comments, cancellation, determinism, and dead code;
4. compare tests against every phase requirement and add missing negative/boundary cases;
5. run dependency and documentation drift checks appropriate to the phase;
6. update the phase status and traceability evidence before beginning the next phase.

No later phase may compensate for a known earlier-phase defect.

## Implementation Rules

- Add independent exhaustive oracle tests before optimizing an engine.
- Never compare rounded displays or substitute sampling.
- Use one request-wide work budget; no per-row reset.
- Reject semantic inference rather than importing legacy classifiers or rules.
- Require explicit deck selectors; never infer a library from deck format or
  zone conventions.
- Never fetch numeric values or mana capabilities from a provider.
- Do not add recommendation aliases, a free-form expression engine, a generic
  router, a legality check, or a Commander-specific behavior.
- Keep every named type/member documented and every public MCP field described.

## Final Acceptance Sequence

1. Run focused unit, App, architecture, and E2E tests.
2. Run `task lint`, `task test`, `task surface:report`, and `task coverage`.
3. Run `task pack`, `task smoke:process`, `task smoke:mcp`, and
   `task release:tool-smoke`.
4. Run dependency vulnerability/deprecation/outdated checks, Markdown link
   validation, and `git diff --check`.
5. Apply abstraction, code-quality, visual, dead-code, test-quality,
   dependency, and docs audits and fix every valid finding.
6. Exercise all eight tools through the official client against a realistic
   explicitly selected 99-card library and independently verify recognizable
   probability vectors.
7. Exercise a nonstandard-format deck with the same explicit population and
   prove format and missing commander zones do not affect results.

## Rollback

Statistics is read-only and owns no persistence. Unregister the `stats`
descriptor and remove App/solution wiring to roll back; `decks.db` and
`scryfall.db` need no migration.
