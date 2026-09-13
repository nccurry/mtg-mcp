# Potential Features

This is a list of ideas deliberately postponed until after the evidence-first
`0.9.0` rewrite. An entry is not a commitment, approved PLC, or permission to
implement. Before work starts, the idea needs its own small PLC, current
provider and policy research, clear evidence limits, and an independent review.

The [evidence-first deckbuilding evolution PLC](llms/plcs/completed/evidence-first-deckbuilding-evolution/README.md)
records the completed follow-on work. New ideas below still need their own
narrow PLC and approval before implementation.

## Registered Post-Cutover Ideas

### Popularity and tournament evidence

Investigate permissioned or official sources for card/deck popularity and
tournament composition. Any result must retain source, format, population,
sample count, denominator, event level, time window, freshness, and known bias.
The MCP may return evidence but must not turn popularity into a quality score.

Open research includes EDH/Commander aggregates, sanctioned or permissioned
tournament feeds, price-provider usage data, and whether source licenses permit
local snapshots and redistribution.

Phase 5 checked Reddit, EDHREC, Moxfield, public Archidekt deck collection,
and Playgroup. It admitted no source: Reddit needs explicit approval, the
website terms do not support direct EDHREC, Moxfield, or public Archidekt
automation, and the current Playgroup API cannot supply complete deck entries
for card-use analysis. See the
[Phase 5 source-feasibility record](llms/plcs/completed/evidence-first-deckbuilding-evolution/SOURCE_FEASIBILITY.md).

#### Deck-group composition evidence

The future `popularity-evidence-sources` PLC must include this representative
acceptance question:

> Among the most popular decks matching an exact commander and theme, how many
> cards matching an explicit card rule do those decks usually run?

For example, after the caller resolves what "Jarad," "monarch deck," and
"monarch-based card" mean, the MCP should be able to report the distribution
of matching-card counts across a defined group of decks from one source. This
work is not owned by exact single-deck statistics, installed Scryfall card data,
or the current owned-deck Archidekt adapter alone. It requires a source that
permits deck-group access plus repeatable counting over joined Scryfall data.

This needs three visible evidence operations:

1. Get the top `X` decks from one provider for an exact commander and optional
   caller-supplied deck filters, limited to an explicit update window such as
   the previous year. The caller selects an available provider ordering such
   as likes; the MCP does not substitute an unspecified popularity formula.
2. Resolve every deck entry to Scryfall identity and filter each complete deck
   using the caller's exact Scryfall query. Query pagination must be complete,
   and unresolved cards or unsupported query semantics remain explicit rather
   than being treated as nonmatches.
3. Count matching quantities per deck, then return the ordered per-deck rows
   and repeatable group statistics such as minimum, maximum, median, mean,
   percentiles, and a histogram.

The deck-group operation retains provider, format, exact commander identity,
update cutoff, requested/returned deck count, ordering metric, rank/metric
value, population size when known, stable source deck IDs, retrieval times,
and missing/private/unavailable rows. Results from different providers remain
separate unless the caller explicitly requests a documented union.

The filter/count operations distinguish quantity counts from unique-card
counts and commander/main/sideboard zones. Oracle facts, Scryfall community
tags, and exact Scryfall queries remain distinct inputs; the MCP does not
invent what "monarch-based" means. A replay ID records the source deck group,
ordered deck versions, Scryfall evidence, exact query, and counting version.

Popularity ordering is evidence about a provider population, not a statement
that a deck or card is good. "Usually" must be answered with the actual
distribution and denominators rather than a hidden representative value. The
future PLC may split deck-population acquisition from composition analysis if
provider research shows they need independent ownership, but this end-to-end
question remains required proof before implementation.

### Deck mana and on-curve estimate

Phase 6 added one read-only estimate for an actual saved deck: whether a named
card can be cast by a named turn under caller-supplied simple land rules. It
uses direct installed Scryfall facts, a repeatable seed, a visible land-play
rule, sampled-result reporting, and a clear line between source facts and
modeled choices. The estimate is not an exact probability or a real-game
prediction.

Broad multiplayer or full-game simulation remains deferred. The planned
old simulation packets are historical reference material, not instructions to
add a rules engine or automatic card behavior.

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

- Broader Archidekt features such as automatic activity/recent-change
  history, packages, deck tags, and collaboration may receive separate PLCs if
  they support the evidence-first purpose. Folder organization and named
  snapshot lifecycle/restore are already stable child-6 scope. Social and
  account-administration automation need a stronger product justification.
- New official Playgroup operations, including a future deck-update or cleanup
  API, can be added through a reviewed contract update without preserving an
  obsolete tool count.
- Playgroup response snapshots may be considered if reproducible historical
  analysis becomes more valuable than live-only provider evidence.

## Local Scryfall Query Engine

A future PLC may investigate running part of Scryfall's query language over
installed Scryfall card data. It is not required for `0.9.0`. Any proposal must
state which query forms it supports, compare those forms with Scryfall, and use
the provider whenever the local result cannot be proven equivalent. Installed
card data alone does not justify running arbitrary searches locally.

### Local Scryfall group analysis

Build repeatable card-data queries for explicit local groups of cards:
subtype/tag frequency, feature distributions, Spirit-versus-Bird comparisons,
Pirate tag counts, and overlap between groups. The design seed is [local
Scryfall group analysis](llms/plans/local-scryfall-cohort-analysis.md).

This remains work for after `0.9.0` and must depend on the local query-engine
limits. Callers define colors versus color identity, exact subtype
membership, changeling treatment, feature families, and distinct-card versus
quantity counting. Results return exact counts, denominators, supporting card
IDs, installed card-data versions, and unknown/unsupported states. “Synergy” is
returned as separate observable evidence such as exact subtype references, tags,
keywords, type-line components, or exact Oracle-text predicates; the MCP does
not produce a synergy score or recommendation.

The future PLC should keep group selection, feature grouping, and group
comparison explicit and limited. It must not add legality assumptions, hidden
tribal taxonomies, popularity joins, or a Magic rules engine.

## Explicitly Not Queued

- Automated Moxfield network access while its terms prohibit automated access.
- Tagger-site scraping or background full Scryfall card-data downloads.
- A comprehensive Magic rules engine.
- Advisor prompts, intent inference, opaque power scores, weak-card judgments,
  or MCP-selected replacements.

These boundaries may change only through explicit product and policy review;
they are not ordinary backlog items.
