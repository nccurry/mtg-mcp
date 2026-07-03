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
value, types, exact Tagger overlap, and other attributable features. Similarity
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

## Explicitly Not Queued

- Automated Moxfield network access while its terms prohibit automated access.
- Bulk or background crawling of Scryfall Tagger.
- A comprehensive Magic rules engine.
- Advisor prompts, intent inference, opaque power scores, weak-card judgments,
  or MCP-selected replacements.

These boundaries may change only through explicit product and policy review;
they are not ordinary backlog items.
