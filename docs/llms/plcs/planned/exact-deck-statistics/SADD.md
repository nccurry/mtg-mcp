# Exact Deck Statistics Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Statistics` is a pure library referencing only BCL and Core records.
App resolves optional deck selectors through the deck service, freezes them to
explicit counts/entry IDs, and invokes the library. The library never loads a
deck or provider itself.

### Exact number contract

`ExactProbability` stores reduced nonnegative `BigInteger` numerator and
positive denominator. It serializes both as decimal strings, plus decimal and
percent strings rounded to 12 fractional places using midpoint-to-even. Zero is
`0/1`; one is `1/1`. Formula results include expectation/variance as exact
rationals when defined.

### Engines

- `CombinationCache`: exact binomial coefficients with symmetry and bounded
  memoization.
- `HypergeometricEngine`: univariate probability mass/range/complements.
- `MembershipBucketEngine`: converts explicit group membership bitmasks into
  disjoint counts and enumerates feasible draw vectors.
- `ManaPaymentMatcher`: tests whether one drawn/played source vector can satisfy
  explicit colored/colorless/generic requirements through bounded bipartite
  matching.
- `MulliganEngine`: evaluates independent reshuffled seven-card attempts,
  explicit keep constraints, and deterministic caller bottoming.
- `InverseSolver`: monotone scan/binary refinement with neighboring proof.
- `DeckSummaryCalculator`: exact counts and stable numeric distributions.

### Input forms

Raw requests provide population/bucket counts. Deck-backed requests provide
deck ID/revision and group selectors over explicit entry IDs, zones,
categories, or already stored source fields. App returns the resolved entry IDs
and quantities in result evidence. A card in several groups receives one
membership mask, preventing double counting.

### Turn and mana semantics

Turn tables derive cards seen only from the supplied draw schedule. Mana events
use the supplied number of playable sources by turn, capability masks, and
cost. They do not infer tapped timing, land types, ramp activation, cost
reduction, or spell sequencing. Callers can model those facts by defining
separate source buckets and usable turns.

### Mulligan semantics

The caller supplies max mulligans, keep predicate over named groups, and bottom
priority rules. The engine exactly enumerates the configured opening-hand
distribution for each independent full-reshuffle attempt. On a kept attempt,
deterministic priority removes the required
number of cards; the final predicate is evaluated if requested. No default keep
rule or bottom order exists.

### Inverse request cases

`stats_minimum_population` accepts a closed `InverseEvent` union, target reduced
rational, fixed deck/draw/turn inputs, and bounded variable-count range. Initial
cases are hypergeometric at-least copies, turn-table at-least copies, and mana
availability with one explicitly repeated source-capability template. The
engine owns and tests the monotonicity proof for every case. Arbitrary
predicates, multivariate min/max combinations not in the union, and caller-
asserted `isMonotone` flags are invalid input.

### Summary percentiles

Numeric deck-summary percentiles exclude missing values, sort exact numeric
values, and select one-based rank `ceil(p * includedCount)`. Output includes
`percentileMethod: nearest-rank`, included count, and missing count; it never
silently interpolates or treats missing values as zero.

Deck composition always reports quantities by the entry's stored zone. There is
no built-in meaning for “included” or “excluded.” A caller may supply disjoint
zone-name sets for those labels; the result validates the sets and reports
`total = included + excluded + uncovered`. Category names, including a primary
category, never affect the partition.

## Algorithms And Bounds

Hypergeometric terms use `C(K,k) * C(N-K,n-k) / C(N,n)`. Multivariate terms use
the product of combinations across disjoint membership buckets over the common
denominator. Enumeration estimates its state count before work and stops if it
can exceed one million. No floating-point value participates in comparisons or
inverse target decisions.

## Toolset And North-Star Design

App assigns all eight tools to the default-enabled `stats` toolset. Because the
tools are read-only, their visibility is identical in every operation mode.
The acceptance workflow starts from an explicit population or local deck
revision, accepts caller-supplied groups and assumptions, returns exact
derivations or a bounded unsupported state, and ends before strategic
interpretation. Separate typed event families remain easier to select and
validate than a free-form expression engine; no recommendation alias or generic
router is allowed.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Decimal-only probabilities | Rejected; rounding cannot prove exactness. |
| Monte Carlo fallback | Rejected; changes evidence class and repeatability. |
| Semantic role classifier inside stats | Rejected; caller/LLM owns groups. |
| Full turn simulator for mana | Rejected; becomes a rules/strategy engine. |
| Third-party statistics library | Rejected; required math is small and exact BCL implementation is auditable. |

## Failure Modes

Invalid population relationships return invalid input with field paths.
Over-limit exact problems return unsupported with `limitKind`, configured
`limit`, safely computed `estimatedStates` when available, actual population,
group count, maximum turn, and mechanical `reductionOptions` such as lowering
turn, reducing groups, or splitting independent requests. No field is a
strategy recommendation and no probability accompanies the result.
Non-monotone inverse event requests are rejected. Missing deck metadata is
reported and excluded only when the caller's selector explicitly permits it.
No error returns an approximate probability.

## Test Architecture

Exhaustively enumerate every hand for small labeled decks and compare all
engines. Use independent known vectors for standard hypergeometric examples,
property tests for complement/normalization/monotonicity, culture replay, and
boundary/overflow tests. Deck integration tests prove selector transparency;
MCP snapshots prove exact string serialization.
They include closed inverse-event schema rejection, neighboring monotonicity
proofs, and overlapping deck-backed selectors that resolve to disclosed entry
IDs without double counting.
