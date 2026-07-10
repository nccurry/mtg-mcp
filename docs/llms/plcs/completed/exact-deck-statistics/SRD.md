# Exact Deck Statistics Software Requirements Document

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-09
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are exact draw distributions, joint group conditions, explicit draw
schedules, source availability, package allocation, explicit London-mulligan
attempts, inverse copy-count solvers, and deterministic local-deck summaries.
Rules-text parsing, deck legality, format policy, spell sequencing, land-drop
inference, tapped-state inference, replacement effects, strategic thresholds,
sampling, and a Magic rules engine are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| STAT-001 | Must | Every probability shall return reduced base-10 numerator/denominator strings plus invariant decimal and percent strings with exactly 12 fractional places, rounded midpoint-to-even by integer quotient/remainder. | Reduction, sign, zero/one, midpoint, and no-scientific-notation vectors pass. |
| STAT-002 | Must | Hypergeometric analysis shall support exact, zero, at-least, at-most, and inclusive-range success events plus exact expectation and variance. | Known vectors and exhaustive small populations match independent enumeration. |
| STAT-003 | Must | Multivariate analysis shall support conjunctions of min/max conditions over up to eight explicit overlapping groups. | Exhaustive membership fixtures match labeled-card enumeration. |
| STAT-004 | Must | Overlapping observed groups shall become disjoint membership buckets without double-counting physical card copies. | Overlap and input-permutation regression fixtures pass. |
| STAT-005 | Must | Turn tables shall accept an opening-hand size and an ordered `drawsByTurn` array whose values are the complete additional cards seen on each turn, plus one closed success event. | Cumulative cards-seen, event, and complement rows match direct hypergeometric vectors without inferred turn rules. |
| STAT-006 | Must | Mana availability shall accept explicit source groups, W/U/B/R/G/C capability sets, a generic requirement, and `maximumUsableSources`. Each selected source produces at most one unit and is assigned once. | Exhaustive small source populations and independent allocation matching pass. |
| STAT-007 | Must | Failure-to-have-mana shall be the exact complement of the same availability event. Hybrid, phyrexian, snow, activation-cost, sequencing, and tapped-state semantics are unsupported. | Event and complement sum exactly to one; unsupported symbols fail before calculation. |
| STAT-008 | Must | Package assembly shall accept explicit required slots and card/tutor capability groups. One physical copy may satisfy at most one slot. | Flexible-tutor and overlapping-membership fixtures match independent bipartite allocation. |
| STAT-009 | Must | Mulligan analysis shall accept an ordered attempt schedule containing draw count, bottom count, and forced status; bounded conjunctions of group min/max keep constraints; deterministic caller bottom priority; and an optional final-hand event. | Attempt, no-keep, and final-event probabilities match exhaustive reshuffle enumeration. |
| STAT-010 | Must | `stats_minimum_count` shall find the lowest copy count in a bounded range meeting an exact target for an engine-proven monotone event. | Lower-bound, target zero/one, neighboring proof, and no-solution fixtures pass. |
| STAT-011 | Must | Deck summary shall compute exact stored zone, category, printing, and quantity counts. Numeric distributions shall use caller-supplied exact values keyed by entry ID and report quantity-weighted histograms/nearest-rank percentiles, missing-entry count, and missing-quantity count. | Golden summaries prove missing values are never fetched or treated as zero. |
| STAT-012 | Must | Every deck-backed population shall require a deck ID, expected revision, and one or more closed selector terms: exact entry IDs, exact zone names, or category IDs. Terms combine by union, deduplicate entry IDs, expand quantities, and require group selections to be population subsets. | Results disclose selected/excluded entry IDs and quantities in canonical deck order; stale revisions conflict. |
| STAT-013 | Must | Exact requests shall reject population above 1,000, more than eight groups, more than 50 turn rows, more than eight mulligan attempts, or invalid draw/population relationships. | Boundary fixtures fail before partial calculation. |
| STAT-014 | Must | Identical canonical inputs shall produce byte-equivalent results independent of culture, input ordering, thread scheduling, or wall clock. | Replay, culture, permutation, and concurrency tests pass. |
| STAT-015 | Must | `MtgMcp.Statistics` shall reference only BCL and provider-neutral Core contracts; it shall not use HTTP, SQLite, Decks, adapters, or MCP types. | Project and source-boundary architecture tests pass. |
| STAT-016 | Must | Every exact result shall identify a stable formula ID, `exact-v1` calculation version, canonical inputs, assumptions, exact evidence kind, and package implementation version. | Output-schema snapshots pass for all eight tools. |
| STAT-017 | Must | A supported request exceeding the exact-work budget shall return `bounded-unsupported` inside the common operation result with limit kind/value, saturated safe estimate, observed dimensions, and mechanical reduction options, and with no probability or partial table. | Boundary schema fixtures contain complete detail and no exact payload. |
| STAT-018 | Must | The inverse solver shall accept only a closed union of engine-proven monotone cases and a bounded variable-count range. Callers cannot assert arbitrary monotonicity. | Unsupported/custom predicates fail before work; returned count includes neighboring proof. |
| STAT-019 | Must | Deck-summary percentiles shall use one-based nearest rank over quantity-expanded non-missing values and report method, requested percentile, rank, included quantity, and missing quantity. | Percentile boundary and quantity-weighted fixtures pass. |
| STAT-020 | Must | Deck summaries shall always report per-zone quantities and shall expose included/excluded partitions only for caller-supplied disjoint exact zone-name sets. Categories shall never determine partition membership. | `total = included + excluded + uncovered`; category edits do not affect zone partitions. |
| STAT-021 | Must | Every tool shall belong only to the default-enabled `stats` toolset and remain read-only in every mode. | `stats` exposes 8 tools; post-child default is 30/51/51 and all is 55/77/90. |
| STAT-022 | Must | Every root and nested MCP input, closed-union discriminator, rational string, bound, and selector field shall have a useful generated schema description and exact required/optional shape. | Official-client schema tests inspect every statistics tool and union variant. |
| STAT-023 | Must | No statistics or deck-validation path shall inspect deck format, require a commander zone, infer a 99-card library, or return a legality judgment. | Commander-shaped and custom-format fixtures produce identical results for identical selected populations. |
| STAT-024 | Must | One request-wide work budget shall cover all bucket states, table rows, inverse candidates, mulligan attempts, and allocation checks. Exactly 1,000,000 units are allowed; the next unit returns bounded unsupported with no partial result. | Aggregate budget and saturating-estimate boundary tests pass. |
| STAT-025 | Must | Missing decks return not-found, stale expected revisions return conflict, malformed requests return invalid-input, exact results return `kind: exact`, and bounded work returns `kind: bounded-unsupported`. | Result-union serialization and official-client failure tests pass. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Exactness | Independent exhaustive enumeration agrees for all small fixtures. |
| Determinism | Canonical replay is byte-equivalent. |
| Boundedness | The aggregate work budget returns structured unsupported without sampling or partial data. |
| Transparency | Selected entries, counted groups, assumptions, formula, and exact values are visible. |
| Independence | No provider, network, SQLite, rules parser, or format-legality dependency exists in Statistics. |

## Definition Of Done

- [ ] Every engine matches independent exhaustive small-population or allocation oracles.
- [ ] Rational and fixed display serialization is stable.
- [ ] No heuristic threshold, sampled fallback, role inference, or legality behavior exists.
- [ ] Every production assembly, including Statistics, maintains at least 90-percent line coverage.
- [ ] All eight tools and nested schemas pass official-client surface tests.
- [ ] A realistic explicitly selected deck workflow exercises all eight tools.
