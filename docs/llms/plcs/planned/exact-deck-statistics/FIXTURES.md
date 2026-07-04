# Exact Deck Statistics Fixtures And Acceptance Matrix

## Exact Vector Inventory

| ID | Scenario | Invariant |
| --- | --- | --- |
| STAT-FIX-001 | 99-card deck, 10 successes, opening 7 | Exact/at-least/zero terms normalize. |
| STAT-FIX-002 | Draw all/none/invalid bounds | Boundary values or invalid input. |
| STAT-FIX-003 | Land and interaction groups with one overlapping card | Joint event has no double count. |
| STAT-FIX-004 | Opening 7, on play vs draw through turn 5 | Explicit schedule changes cards seen only as configured. |
| STAT-FIX-005 | Mono-color, dual, any-color, colorless source masks | Payment probability matches exhaustive matching. |
| STAT-FIX-006 | Required two combo pieces plus explicit flexible tutor | Assembly probability matches labeled-deck enumeration. |
| STAT-FIX-007 | Three London attempts with caller keep/bottom rules | Attempt and final-hand probabilities match exhaustive enumeration. |
| STAT-FIX-008 | Minimum sources for target rational | Previous count fails; returned count passes. |
| STAT-FIX-009 | Deck summary with missing mana values | Counts/histogram/nearest-rank percentiles and missing count are exact. |
| STAT-FIX-010 | Estimated enumeration exceeds one million | Unsupported; no probability value. |
| STAT-FIX-011 | Population 1,001, nine groups, or turn 51 | Unsupported names the violated bound and mechanical reduction fields; no rational returned. |
| STAT-FIX-012 | Custom/non-monotone inverse predicate | Invalid input before calculation; caller cannot assert monotonicity. |
| STAT-FIX-013 | Two overlapping deck-backed selectors | Resolved entry IDs are disclosed and membership buckets avoid double counting. |
| STAT-FIX-014 | Nearest-rank percentile with missing values | Output names nearest-rank, included count, rank/value, and missing count. |
| STAT-FIX-015 | Caller partitions main/commander versus sideboard/maybeboard with another uncovered zone | Exact invariant holds; renaming/reassigning categories changes no zone count. |

## Tool Matrix

| Tool | Core event |
| --- | --- |
| `stats_hypergeometric` | Univariate mass/range/expectation/variance |
| `stats_multivariate` | Joint min/max group conditions |
| `stats_turn_table` | Explicit draw schedule by turn |
| `stats_mana_availability` | Explicit source/cost feasibility and complement |
| `stats_package_assembly` | Pieces and tutor-equivalent groups |
| `stats_mulligan` | Caller keep/bottom policy |
| `stats_minimum_population` | Exact monotone inverse target |
| `stats_deck_summary` | Explicit stored deck composition |

All tools are visible in `read-only`, `local`, and `remote` and are annotated
read-only, idempotent, non-destructive, and closed-world.

## Property Matrix

- Probability is between zero and one.
- Full distribution sums exactly to one.
- Event and complement sum exactly to one.
- Increasing successes cannot reduce an at-least event.
- Canonical input permutation does not change output.
- Decimal rendering never affects exact comparison.
- Cancellation/limit failure returns no partial rational.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| STAT-001 | STAT-FIX-001, STAT-FIX-002, rational reduction, and rendering vectors. |
| STAT-002 | STAT-FIX-001, STAT-FIX-002, and exhaustive univariate enumeration. |
| STAT-003, STAT-004 | STAT-FIX-003 and exhaustive membership-bucket enumeration. |
| STAT-005 | STAT-FIX-004 and explicit play/draw/multiplayer schedule vectors. |
| STAT-006, STAT-007 | STAT-FIX-005, payment matching, and complement property. |
| STAT-008 | STAT-FIX-006 and labeled-deck enumeration. |
| STAT-009 | STAT-FIX-007 and exhaustive London-mulligan enumeration. |
| STAT-010 | STAT-FIX-008 and adjacent-count proof. |
| STAT-011, STAT-012 | STAT-FIX-009 plus exact selection/evidence snapshots. |
| STAT-013 | STAT-FIX-010 and all documented boundary vectors. |
| STAT-014 | Replay, culture, permutation, and concurrency property tests. |
| STAT-015 | Project/package architecture tests. |
| STAT-016 | Output schema snapshots for every tool. |
| STAT-017 | STAT-FIX-010 and STAT-FIX-011 unsupported-detail snapshots. |
| STAT-018 | STAT-FIX-008, STAT-FIX-012, and neighboring-count properties. |
| STAT-019 | STAT-FIX-009 and STAT-FIX-014. |
| STAT-020 | STAT-FIX-015 and local-deck zone/category independence fixture. |
| STAT-021 | Default/all/none/explicit `stats` profile matrix plus a local-deck-and-explicit-groups-to-exact-result workflow. |

## North-Star Workflow Fixture

Given a revisioned local Commander deck, explicit success groups, and a stated
draw or mana scenario, the client receives a reduced exact rational, documented
display value, formula, inputs, assumptions, and counted entries. An over-limit
case returns unsupported without a probability. No threshold, keep decision,
role inference, or deckbuilding recommendation is returned.

## Live Tests

None. All mathematics and deck selection are deterministic and offline.
