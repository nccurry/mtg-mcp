# Exact Deck Statistics Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are exact draw distributions, joint conditions, turn tables, source
availability, package assembly, London mulligans, inverse copy solvers, and
deterministic composition summaries. Rules-text parsing, sequencing spells,
tapped-land inference, replacement effects, strategic thresholds, sampling,
and a Magic rules engine are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| STAT-001 | Must | Every probability shall return reduced numerator/denominator and a 12-place midpoint-to-even decimal/percentage. | Rational reduction and rendering tests pass. |
| STAT-002 | Must | Hypergeometric analysis shall support exactly, zero, at least, at most, and inclusive range plus expectation and variance. | Known vectors and exhaustive small decks match. |
| STAT-003 | Must | Multivariate analysis shall support min/max conditions over up to eight explicit overlapping groups. | Exhaustive membership fixtures match enumeration. |
| STAT-004 | Must | Overlapping groups shall be transformed into disjoint membership buckets without double-counting cards. | Overlap regression fixtures pass. |
| STAT-005 | Must | Turn tables shall accept explicit deck size, opening hand, draw-on-turn-one flag, per-turn draw schedule, and maximum turn. | Play/draw/multiplayer fixtures match manual counts. |
| STAT-006 | Must | Mana availability shall accept explicit source capability masks, usable-turn constraints, land-play cap, and colored/generic requirements. | Exhaustive small source decks and payment matching pass. |
| STAT-007 | Must | Failure-to-have-mana shall be the exact complement of the same availability event, not a separate heuristic. | Complement sums exactly to one. |
| STAT-008 | Must | Package assembly shall accept explicit pieces, minimum counts, and tutor-equivalent memberships. | Combo/tutor fixtures match enumeration. |
| STAT-009 | Must | London mulligan analysis shall accept attempts, keep constraints, final hand size, and deterministic bottom priority supplied by the caller. | Exhaustive small-deck mulligan fixtures match. |
| STAT-010 | Must | Inverse solver shall find the minimum population count meeting an exact target for a supported event. | Neighboring count proof shows `n-1` fails and `n` passes. |
| STAT-011 | Must | Deck summary shall compute explicit zone/category/printing counts and numeric distributions with missing-data counts. | Golden summary fixtures pass. |
| STAT-012 | Must | Deck-backed inputs shall record deck ID/revision and exact selected entry IDs; selector resolution shall not infer semantic roles. | Result evidence lists counted and excluded entries. |
| STAT-013 | Must | Exact enumeration shall stop before one million states, population 1,000, eight groups, or turn 50 and return bounded unsupported detail. | Limit fixtures pass with no partial probability. |
| STAT-014 | Must | Identical canonical inputs shall produce identical results independent of culture, thread scheduling, or wall clock. | Replay/culture/concurrency tests pass. |
| STAT-015 | Must | `MtgMcp.Statistics` shall use only BCL and provider-neutral Core contracts. | Project/package architecture tests pass. |
| STAT-016 | Must | Every result shall identify formula/event, inputs, assumptions, exact evidence kind, and implementation version. | Output schema snapshots pass. |
| STAT-017 | Must | An over-limit unsupported result shall identify the violated limit, exact configured limit, estimated work when safely computable, observed population/group/turn values, and mechanical request-reduction options without returning a probability. | Boundary schema fixtures contain no partial rational and enough fields to simplify the request. |
| STAT-018 | Must | The inverse solver shall accept only a closed union of engine-proven monotone event cases and a bounded variable-count range; callers shall not assert that an arbitrary predicate is monotone. | Supported-case neighbor proofs pass and custom/non-monotone requests are rejected before calculation. |
| STAT-019 | Must | Deck-summary percentiles shall use nearest-rank over non-missing values and report `percentileMethod`, included count, and missing count. | Golden percentile boundary and missing-data fixtures pass. |
| STAT-020 | Must | Deck summaries shall always return exact per-zone quantities and shall expose included/excluded partitions only when the caller supplies disjoint zone-name sets; categories shall never determine partition membership. | Partition fixtures prove total equals included plus excluded plus uncovered and category changes leave zone results unchanged. |
| STAT-021 | Must | Every tool shall belong only to the default-enabled `stats` toolset and shall pass the packet's north-star acceptance workflow using explicit caller-supplied groups and assumptions, without recommendation aliases, a free-form expression engine, or a generic router. | Default/all/explicit/none profile tests and the composed local-deck-to-exact-result fixture pass identically in every operation mode. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Exactness | Exhaustive enumeration agrees for all small fixtures. |
| Determinism | Canonical input replay is byte-equivalent. |
| Boundedness | Hard limits return unsupported without sampling. |
| Transparency | Counted sets, assumptions, and formula IDs are visible. |
| Independence | No HTTP, SQLite, rules parser, or provider package dependency. |

## Definition Of Done

- [ ] All exact functions pass exhaustive small-deck comparison.
- [ ] Rational and display rendering are stable.
- [ ] No heuristic threshold or sampled fallback exists.
- [ ] Package maintains 90-percent coverage with boundary tests.
- [ ] Toolset assignment and the north-star acceptance workflow are proven.
