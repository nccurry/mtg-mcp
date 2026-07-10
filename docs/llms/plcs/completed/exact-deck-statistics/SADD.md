# Exact Deck Statistics Software Architecture And Design Document

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-09
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Statistics` is a pure library that references only BCL and Core. App
resolves optional deck selectors through the local read store, freezes them to
explicit disjoint population buckets and entry evidence, and invokes the
library. Statistics never loads a deck, database, provider, or MCP session.

### Public calculation outcome

Tools return `OperationResult<StatisticsCalculation<T>>`. Malformed requests
use `OperationInvalidInput`, missing decks use `OperationNotFound`, and stale
deck revisions use `OperationConflict`. `StatisticsCalculation<T>` is a closed
union:

- `exact`: the complete typed exact result;
- `bounded-unsupported`: `limitKind`, configured integer limit, saturated
  estimated work when safely computable, observed population/group/turn/
  attempt dimensions, and mechanical reduction options.

The bounded case never contains a rational, partial table, partial candidate,
or approximation. The common Core operation union is not widened.

### Exact number contract

Internal rationals use normalized `BigInteger` numerator and positive
denominator. Public numerator, denominator, and rational inputs are invariant
base-10 strings. Public results are reduced. Probability display fields are
fixed 12-place decimal and percent strings without scientific notation.
Rounding uses integer quotient/remainder midpoint-to-even; no floating-point or
`decimal` conversion participates in math or comparisons. Zero is `0/1`; one
is `1/1`.

Formula IDs are stable kebab-case names. Every completed result carries
calculation version `exact-v1`, package implementation version, an
`ExactDerivationDescriptor`, canonical ordinally ordered inputs, and explicit
assumptions. Canonical input permutation cannot change output bytes.

### Population input and deck selectors

Probability tools share a JSON-polymorphic population union:

- `raw`: a positive count for each already-disjoint bucket plus the exact
  group names associated with that bucket;
- `deck`: deck ID, expected revision, one or more population selector terms,
  and named group selector collections.

A selector term is one closed variant:

- exact entry IDs;
- exact case-sensitive zone names;
- category IDs.

Terms combine by set union. App rejects an empty selector collection, missing
IDs, duplicate group names, a named group that selects outside the population,
or a selected population above 1,000 cards after quantity expansion. Duplicate
matches collapse by entry ID. App orders selected and excluded entries by the
deck's canonical entry order and records entry ID plus quantity. Deck format,
deck name, Commander conventions, and `deck_validate` never affect selection.

App converts selected entries to disjoint buckets keyed by their exact set of
caller group names. The library receives only bucket counts and optional frozen
selection evidence. Raw callers provide those buckets directly.

### Engines

- `CombinationCache`: exact binomial coefficients with symmetry and bounded
  memoization.
- `HypergeometricEngine`: univariate exact/range probability, complement,
  expectation, and variance.
- `MembershipBucketEngine`: canonicalizes group membership masks and enumerates
  feasible draw vectors without double counting.
- `AllocationMatcher`: proves one-use source or package-slot assignments.
- `ManaAvailabilityEngine`: enumerates source evidence and matches W/U/B/R/G/C
  plus generic requirements under a caller-supplied maximum usable-source cap.
- `MulliganEngine`: combines independent reshuffled attempts, typed keep
  constraints, explicit bottom counts, forced attempts, and deterministic
  caller bottom priorities.
- `InverseCountSolver`: scans an engine-owned closed monotone case range and
  returns the first success with its preceding proof.
- `DeckSummaryCalculator`: structural quantity counts and caller-value numeric
  distributions.

### Multivariate and package semantics

Multivariate group predicates are observations: one drawn card may contribute
to several overlapping group counts. Membership masks prevent the card's
combinatorial weight from being counted twice.

Package assembly is allocation: the caller supplies required slots and the
card or tutor groups capable of satisfying each slot. One physical drawn copy
can fill at most one slot. A deterministic bipartite capacity match decides
feasibility for each enumerated draw vector. No card is assumed to tutor or
replace another unless the caller declares that capability.

### Turn semantics

`stats_turn_table` accepts opening-hand size plus an ordered `drawsByTurn`
array. Each row gives the complete count of additional cards seen during that
turn. The calculator accumulates exactly those values and evaluates one typed
hypergeometric event plus its complement at each row. It does not add a normal
draw, skip turn one, interpret play/draw, or infer multiplayer rules.

The schedule has at most 50 rows, uses strictly increasing positive turn
numbers, and may not push cumulative cards seen above the selected population.

### Mana semantics

The caller maps named source groups to one or more of W/U/B/R/G/C. A selected
card may map to at most one source group. Each available copy produces at most
one mana unit, is used at most once, and may pay one capability it declares or
one generic unit. `maximumUsableSources` bounds the total sources that may be
assigned; it has no implied land-drop meaning.

The engine reports the exact availability event and its complement. It does
not infer whether a card is a land, enters tapped, activates in time, costs
mana, produces multiple mana, or can pay hybrid, phyrexian, snow, or alternate
costs. Unsupported symbols fail before enumeration.

### Mulligan semantics

The caller supplies an ordered schedule of at most eight attempts. Each attempt
specifies its draw count, bottom count, and whether it is forced. Non-final
forced attempts and any attempt after a forced attempt are invalid. Every
non-forced attempt evaluates a conjunction of typed group minimum/maximum keep
constraints. Failure proceeds to the next independently reshuffled attempt.

Bottom priority is an ordered list of group names. For a kept hand, the engine
removes matching disjoint membership buckets in caller order and uses canonical
membership-mask order only as an explicit tie-break/fallback. Each removed copy
is used once. The result reports reach/keep probability per attempt, optional
no-keep probability when the final attempt is not forced, and the exact
probability of the caller's optional final-hand condition. There is no default
keep rule, free mulligan, bottom count, or strategic priority.

### Inverse request cases

`stats_minimum_count` accepts a closed event union, an exact target rational,
fixed inputs, and inclusive minimum/maximum copy counts. Initial cases are:

- hypergeometric at-least copies;
- explicit turn-schedule at-least copies;
- mana availability with one repeated declared source template.

The engine owns a monotonicity proof for every case. It handles lower bounds,
target zero and one, and no solution in range. A successful result contains the
returned count, its exact probability, and the preceding in-range count and
probability when one exists. Arbitrary predicates and caller-supplied
`isMonotone` flags are invalid.

### Deck summary semantics

`stats_deck_summary` requires deck ID and expected revision and may use the same
explicit selector terms. It reports quantity by exact zone, category ID/name,
and printing identity. Category totals may overlap and say so. Missing printing
IDs use deterministic fallback buckets based on exact set/collector/language,
then Oracle ID, then unresolved card name.

Optional numeric series contain a name plus exact decimal-string value keyed by
entry ID. Values are quantity-expanded. Output reports a canonical histogram,
exact average, requested nearest-rank percentiles, included entry/quantity
counts, missing entry count, and missing quantity count. Missing values are not
zero and never trigger provider I/O.

Deck composition always reports stored zones. Optional included/excluded zone
sets must be exact and disjoint; output proves
`total = included + excluded + uncovered`. Categories never define that
partition.

## Aggregate Work Budget And Bounds

One `StatisticsWorkBudget` with limit 1,000,000 is created per tool request and
shared across bucket states, turn rows, allocation checks, inverse candidates,
and mulligan attempts. Consuming unit 1,000,000 is allowed; the next unit stops
the complete request. Estimates use saturating integer arithmetic and never a
wall-clock cutoff. Population is at most 1,000, observed groups at most eight,
turn rows at most 50, and mulligan attempts at most eight.

## Toolset And Composition

App adds `StatisticsToolsetManifest` after Scryfall in registry order. It owns
all eight read tools and is default-enabled. Statistics has
`credentialState: not-required` and a null authentication-status tool while
capability schema remains version 6.

An explicit `stats` session may use deck-backed requests even when `decks` is
hidden, so App constructs a read-only deck-store boundary whenever either
toolset is enabled. Tool visibility remains independent: selecting `stats`
does not expose `deck_*` tools.

The post-child counts are 30/51/51 for `default` and 55/77/90 for `all`.
Project, solution, test, task, coverage, smoke, release, live-manifest, and
architecture inventories include Statistics explicitly.

## Public Schema Rules

Every tool uses a typed request. Every root parameter, nested property,
selector variant, population variant, event variant, inverse variant, rational
string, bound, and discriminator has a useful description. Schema tests assert
variant-specific required properties and prohibit irrelevant fields. There is
no generic router or expression language.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Decimal-only probabilities | Rejected; rounding cannot prove exactness. |
| Monte Carlo fallback | Rejected; it changes evidence class and replay behavior. |
| Semantic role or legality inference | Rejected; caller owns groups and population. |
| Implicit 99-card Commander library | Rejected; format does not select entries. |
| Full turn or mana simulator | Rejected; it becomes a rules/strategy engine. |
| Generic selector expression language | Rejected; typed selectors are safer and easier for LLMs. |
| Widen common `OperationUnsupported` | Rejected; structured statistics limits remain capability-owned. |
| Third-party statistics library | Rejected; the exact bounded math is small and BCL-auditable. |

## Test Architecture

Independent exhaustive labeled-card oracles compare every small-population
engine. Known hypergeometric vectors, exact allocation brute force, complement
and normalization properties, culture/permutation replay, schema snapshots,
budget boundaries, and stale-deck behavior are mandatory. A realistic
Commander-shaped deck explicitly selects its 99-card library and exercises all
eight tools; a custom-format deck proves identical math and no legality path.
