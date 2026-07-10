# Exact Deck Statistics Fixtures And Acceptance Matrix

## Exact Vector Inventory

| ID | Scenario | Invariant |
| --- | --- | --- |
| STAT-FIX-001 | 99-card population, 36 successes, draw 7 | Exact/at-least/zero terms and complement match direct combinations. |
| STAT-FIX-002 | Draw all/none, target zero/one, and invalid bounds | Exact boundary value or invalid input. |
| STAT-FIX-003 | Land and interaction observations with one overlapping bucket | Joint observation has no combinatorial double count. |
| STAT-FIX-004 | Explicit draws-by-turn schedules through turn 5 | Only supplied draw rows change cumulative cards seen. |
| STAT-FIX-005 | Mono-color, dual, any-color, and colorless source groups | Exact source allocation matches brute force and complement. |
| STAT-FIX-006 | Two required package slots plus one flexible tutor group | Each drawn copy fills at most one slot and matches brute-force allocation. |
| STAT-FIX-007 | Three explicit mulligan attempts with caller bottom priority | Reach, keep, no-keep, and final event match exhaustive reshuffles. |
| STAT-FIX-008 | Minimum copies for an exact target | Lower bound is honored; preceding in-range count fails and returned count passes. |
| STAT-FIX-009 | Deck summary with caller values missing from some entries | Quantity-weighted histogram/percentiles and missing entry/quantity counts are exact. |
| STAT-FIX-010 | Aggregate work would consume unit 1,000,001 | Bounded unsupported; no partial exact payload. |
| STAT-FIX-011 | Population 1,001, nine groups, 51 turns, or nine attempts | Invalid or bounded result identifies the exact violated contract. |
| STAT-FIX-012 | Custom inverse predicate or caller `isMonotone` flag | Schema/validation rejects it before calculation. |
| STAT-FIX-013 | Entry/zone/category selectors overlap | Entry IDs deduplicate, quantities expand, and selected/excluded evidence is canonical. |
| STAT-FIX-014 | Nearest-rank percentiles with quantities and missing values | Output names nearest-rank, included/missing quantities, rank, and exact value. |
| STAT-FIX-015 | Caller partitions zones with one uncovered zone | `total = included + excluded + uncovered`; category edits change no zone result. |
| STAT-FIX-016 | Same selected population in `commander` and custom-format decks | Every exact result is identical; no commander zone or legality rule is consulted. |
| STAT-FIX-017 | Midpoint-to-even positive and negative rational display vectors | Fixed 12-place strings match integer quotient/remainder rounding. |
| STAT-FIX-018 | Population/group/bucket input permutations | Canonical serialized results are byte-equivalent. |
| STAT-FIX-019 | Missing deck and stale expected revision | Top-level not-found/conflict outcomes contain no calculation. |
| STAT-FIX-020 | One card qualifies for two observed groups and two package slots | Observation counts both groups; allocation fills at most one package slot. |

## Tool Matrix

| Tool | Core event | Required caller evidence |
| --- | --- | --- |
| `stats_hypergeometric` | Univariate mass/range/expectation/variance | Population and success group |
| `stats_multivariate` | Joint group min/max observation | Population and condition groups |
| `stats_turn_table` | One success event across explicit cumulative draws | Population, event, and draws-by-turn |
| `stats_mana_availability` | One-use W/U/B/R/G/C/generic source allocation | Population, source groups, requirements, maximum usable sources |
| `stats_package_assembly` | One-use card/tutor allocation to required slots | Population and requirement capabilities |
| `stats_mulligan` | Explicit independent attempt schedule | Population, keep constraints, bottom priority, optional final event |
| `stats_minimum_count` | Closed monotone inverse target | Typed event, target rational, inclusive count range |
| `stats_deck_summary` | Stored deck structure and caller numeric series | Deck revision, selectors, values, percentiles, optional zone partition |

Every tool is visible in `read-only`, `local`, and `remote`; annotations are
read-only, idempotent, non-destructive, and closed-world.

## Property Matrix

- Probability is between zero and one.
- Full distributions normalize to exactly one.
- Event and complement sum exactly to one.
- Increasing successes cannot reduce an engine-owned at-least event.
- Canonical input permutation cannot change output bytes.
- Rounded fields never participate in exact comparison.
- Quantity expansion equals a labeled-card oracle.
- One-use allocation never spends a physical copy twice.
- Cancellation or budget failure returns no partial rational or table.

## Surface Matrix After Statistics

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 30 | 51 | 51 |
| `all` | 55 | 77 | 90 |
| explicit `stats` | 8 | 8 | 8 |
| `none` | 0 | 0 | 0 |

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| STAT-001 | STAT-FIX-001, 002, 017; rational reduction and display vectors. |
| STAT-002 | STAT-FIX-001, 002; exhaustive univariate oracle. |
| STAT-003, 004 | STAT-FIX-003, 018, 020; exhaustive membership-bucket oracle. |
| STAT-005 | STAT-FIX-004; explicit schedule and complement vectors. |
| STAT-006, 007 | STAT-FIX-005; allocation oracle and unsupported-symbol rejection. |
| STAT-008 | STAT-FIX-006, 020; one-use allocation oracle. |
| STAT-009 | STAT-FIX-007, 011; exhaustive attempts and request-wide budget. |
| STAT-010, 018 | STAT-FIX-002, 008, 012; neighboring-count proof. |
| STAT-011, 019, 020 | STAT-FIX-009, 014, 015; stored-field and caller-value summaries. |
| STAT-012, 023 | STAT-FIX-013, 016, 019; selector evidence and format neutrality. |
| STAT-013, 017, 024 | STAT-FIX-010, 011; no partial exact payload. |
| STAT-014, 016 | STAT-FIX-017, 018; culture/concurrency replay and schema snapshots. |
| STAT-015 | Project/package/source architecture tests. |
| STAT-021 | Surface matrix and profile/mode official-client tests. |
| STAT-022 | Input-schema description and discriminator snapshots for all variants. |
| STAT-025 | Exact, bounded, invalid, not-found, and conflict serialization tests. |

## Realistic Deck Acceptance

Create a local 100-card Commander-shaped fixture but explicitly select only 99
library cards. Assign caller groups for lands, white sources, red sources,
interaction, and a two-piece package. Exercise all eight tools through the
official client and independently verify at least:

- opening-hand land probability from direct combinations;
- turn-table probability from the cumulative supplied draw counts;
- mana event/complement normalization;
- package and mulligan results against exhaustive small equivalent fixtures;
- inverse returned/previous count proof;
- deck-summary quantities, zone partition, and caller numeric values.

Repeat one probability on a custom-format deck with the same 99-card selected
population and no commander-zone entry. The result must be byte-equivalent
apart from disclosed deck identity/revision evidence.

## Live Tests

None. Statistics and deck selection are deterministic and offline. The
installed-package method manifest exercises these tools locally and must not
label them provider-live operations.
