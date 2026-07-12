# Local Scryfall Cohort Analysis Design Seed

## Status

- Lifecycle: Deferred post-cutover design seed
- Implementation authorized: No
- Related future capability: `local-scryfall-query-engine`
- Current runtime: no cohort-analysis tools

This document records a narrowly bounded future capability for deterministic
cross-card questions over the shared Scryfall corpus. It is not a PLC, does not
change the `0.9.0` cutover, and does not authorize implementation.

## Problem

The MCP already exposes structured Scryfall cards, Oracle/art tags, immutable
corpus generations, exact card queries, and exact statistics. An LLM can use
those primitives to answer individual questions, but it must currently perform
large joins and feature counts itself. That makes recurring questions about
tribes, tags, and card behavior unnecessarily expensive and difficult to audit.

Representative questions include:

- Which creature subtypes are most common among blue-white creatures?
- Which Oracle tags and card-type components occur most often among Pirates?
- Which observable tags, keywords, or text features distinguish Spirits from
  Birds in a specified color population?
- Are there more qualifying Spirits or Birds, and how much do the cohorts
  overlap?
- Which explicit tribal-synergy evidence is more common for one cohort than
  another?

## North-Star Boundary

The future capability returns deterministic evidence, counts, denominators,
overlaps, and provenance. It does not decide what a tribe means, whether a card
is good, whether a feature is strategically desirable, or which commander to
build.

The caller/LLM must explicitly provide:

- color versus color-identity semantics;
- exact cohort predicates;
- whether multi-typed and changeling cards count;
- feature families to inspect;
- distinct-card versus quantity counting; and
- the active corpus generation or freshness policy.

"Synergy" is never a hidden score. Results expose separate evidence families:
exact subtype references, Oracle/art tags, keywords, type-line components, and
explicit Oracle-text predicates. The LLM may interpret those facts.

## Proposed Evidence Model

```text
CohortDefinition
  source: local-corpus
  corpusGenerationId
  cardPredicate
    colors | colorIdentity
    typeLine components
    exact subtype membership
    tag membership
    explicit query constraints

FeatureDefinition
  family: subtype | tag | keyword | type-component | oracle-text
  exact selector or normalized extraction rule

CohortResult
  cohort fingerprint
  matched distinct cards
  optional quantity total
  denominator
  feature rows: value, count, proportion, supporting card IDs
  evidence origin and corpus generation

CohortComparison
  left and right fingerprints
  intersection and exclusive counts
  shared and exclusive feature rows
  explicit zero/unknown/unavailable states
```

The first useful implementation should support local corpus reads and bounded
feature grouping. It should not attempt to reproduce all Scryfall query syntax
until the separate local query-engine PLC proves each supported construct by
differential testing against Scryfall.

## Candidate Surface

The exact surface belongs to a future PLC. A likely minimal boundary is:

1. A cohort query/preview operation that validates and fingerprints an explicit
   local population.
2. A feature-frequency operation that groups the population by caller-selected
   feature families.
3. A cohort-comparison operation that reports overlap and feature differences.

These operations may remain one or more `scryfall_*` tools depending on schema
size and MCP context limits. They must not add automatic deck recommendations,
legality assumptions, popularity scoring, or category assignments.

## Determinism And Provenance

- Corpus generation, normalized predicate, feature definition, and schema
  version are part of every fingerprint.
- Results use stable ordering and exact integer counts.
- Percentages are derived from explicit denominators and documented rounding.
- Empty, unresolved, unsupported, and unavailable states remain distinct.
- Card evidence retains Oracle ID, printing identity where relevant, source
  origin, and corpus generation.
- Different corpus generations produce different evidence fingerprints.
- No provider request is hidden inside a local cohort read.

## Required Future PLC Gates

Before implementation, the PLC must define and test:

- supported predicate and feature coverage;
- local-query differential tests and provider fallback boundaries;
- subtype normalization, dual types, changelings, and split/face handling;
- exact versus quantity counts and denominator semantics;
- text-feature extraction limits and parser-derived evidence labels;
- bounded result sizes and pagination;
- cache/corpus generation replay;
- schema descriptions and mode visibility; and
- a realistic Spirit-versus-Bird and Pirate feature-count fixture.

The capability should be implemented only after categorization and the
stabilization child have closed, unless the owner explicitly reprioritizes it
as a separate post-cutover capability.

## Explicit Non-Goals

- No hidden tribal taxonomy or automatic definition of “synergy”.
- No blended scores, rankings, recommendations, or deck legality decisions.
- No Magic rules engine or semantic Oracle-text understanding beyond documented
  exact predicates and parser-derived classifications.
- No popularity/deck-population joins; those remain in the separate popularity
  evidence roadmap.
- No new Scryfall provider scraping or background synchronization.
