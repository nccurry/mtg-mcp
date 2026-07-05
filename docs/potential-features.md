# Potential Features

This is the durable idea registry for work intentionally deferred beyond the
evidence-first `0.9.0` rewrite. An entry is not a commitment, approved PLC, or
implementation authorization. Before work starts, the idea needs its own narrow
PLC, current provider/policy research, explicit evidence boundaries, and an
independent review.

## Registered Post-Cutover Ideas

### Popularity and tournament evidence

Investigate permissioned or official sources for card/deck popularity and
tournament composition. Any result must retain source, format, population,
sample count, denominator, event level, time window, freshness, and known bias.
The MCP may return evidence but must not turn popularity into a quality score.

Open research includes EDH/Commander aggregates, sanctioned or permissioned
tournament feeds, price-provider usage data, and whether source licenses permit
local snapshots and redistribution.

#### Deck-population composition evidence

The future `popularity-evidence-sources` PLC must include this representative
acceptance question:

> Among the most popular decks matching an exact commander and theme, how many
> cards matching an explicit card predicate do those decks usually run?

For example, after the caller resolves what "Jarad," "monarch deck," and
"monarch-based card" mean, the MCP should be able to report the distribution
of matching-card counts across a source-defined cohort. This workflow is not
owned by exact single-deck statistics, the Scryfall card corpus, or the current
owned-deck Archidekt adapter alone. It requires a permissioned deck-population
source plus deterministic composition over joined Scryfall evidence.

At the workflow level this decomposes into three visible evidence operations:

1. Get the top `X` decks from one provider for an exact commander and optional
   caller-supplied deck filters, limited to an explicit update window such as
   the previous year. The caller selects an available provider ordering such
   as likes; the MCP does not substitute an unspecified popularity formula.
2. Resolve every deck entry to Scryfall identity and filter each complete deck
   using the caller's exact Scryfall query. Query pagination must be complete,
   and unresolved cards or unsupported query semantics remain explicit rather
   than being treated as nonmatches.
3. Count matching quantities per deck, then return the ordered per-deck rows
   and deterministic cohort statistics such as minimum, maximum, median, mean,
   percentiles, and a histogram.

The deck-cohort operation retains provider, format, exact commander identity,
update cutoff, requested/returned deck count, ordering metric, rank/metric
value, population size when known, stable source deck IDs, retrieval times,
and missing/private/unavailable rows. Results from different providers remain
separate unless the caller explicitly requests a documented union.

The filter/count operations distinguish quantity counts from unique-card
counts and commander/main/sideboard zones. Oracle facts, Scryfall community
tags, and exact Scryfall queries remain distinct inputs; the MCP does not
invent what "monarch-based" means. A replayable join fingerprint covers the
provider cohort, ordered deck versions, Scryfall evidence, exact query, and
aggregation version.

Popularity ordering is evidence about a provider population, not a statement
that a deck or card is good. "Usually" must be answered with the actual
distribution and denominators rather than a hidden representative value. The
future PLC may split deck-population acquisition from composition analysis if
provider research shows they need independent ownership, but this end-to-end
question remains an acceptance gate.

### Goldfish and multiplayer simulation feasibility

Determine whether useful seeded simulations are possible without presenting the
MCP as a comprehensive Magic rules engine. The first deliverable is a
feasibility PLC, not a simulator. It must define the supported rules subset,
unsupported mechanics, deterministic seed/model version, player policy inputs,
sample/error reporting, and the line between factual game state and heuristic
play choices.

Existing reference seeds include the planned `simulation-profile-evidence`,
`stats-lab-interaction-readiness`, and `conservative-goldfish-v2` packets. They
remain reference material until the feasibility decision is approved.

### Deck-weakness evidence

Research factual signals an LLM could use when assessing a deck: exact mana and
color-source probabilities, curve/concentration facts, interaction coverage,
redundancy, card-role evidence, dependency bottlenecks, price exposure, and
observed matchup/game data. The MCP must expose inputs and limitations and must
not label a card "weak" or choose cuts.

### Budget-alternative evidence

Research price and similarity evidence that lets an LLM compare cards without
the MCP recommending replacements. Candidate evidence includes current price
with market/date/currency, oracle characteristics, color/format legality, mana
value, types, exact community-tag overlap, and other attributable features. Similarity
must expose its components and never imply functional equivalence.

## Conditional Provider Expansions

- Broader Archidekt capabilities such as automatic activity/recent-change
  history, packages, deck tags, and collaboration may receive separate PLCs if
  they support the evidence/workflow mission. Folder organization and named
  snapshot lifecycle/restore are already stable child-6 scope. Social and
  account-administration automation need a stronger product justification.
- New official Playgroup operations, including a future deck-update or cleanup
  API, can be added through a reviewed contract update without preserving an
  obsolete tool count.
- Playgroup response snapshots may be considered if reproducible historical
  analysis becomes more valuable than live-only provider evidence.

## Local Scryfall Query Engine

A future PLC may investigate executing part of Scryfall's query language over
the local corpus. It is not a `0.9.0` cutover dependency. Any proposal must
publish query-coverage limits, differential-test supported semantics against
Scryfall, and fall back to the provider whenever complete local equivalence
cannot be proven. The unified corpus does not itself authorize local execution
of arbitrary searches.

## Explicitly Not Queued

- Automated Moxfield network access while its terms prohibit automated access.
- Tagger-site scraping or background Scryfall corpus downloads.
- A comprehensive Magic rules engine.
- Advisor prompts, intent inference, opaque power scores, weak-card judgments,
  or MCP-selected replacements.

These boundaries may change only through explicit product and policy review;
they are not ordinary backlog items.
