# North Star

`mtg-mcp` helps LLMs make informed Magic: The Gathering deckbuilding decisions
by supplying grounded evidence. The MCP gathers and computes; the calling LLM
connects that evidence to a player's goals and makes the judgment.

For stable `0.9.0`, this means provider data, exact cached evidence, explicit
deck/provider workflow operations, and exact mathematics. It does not include
advisor prompts, inferred deck intent, weak-card judgments, replacement
recommendations, blended quality scores, or strategic automation. Those legacy
features are removal targets, not abstractions to preserve.

## Product Outcome

An LLM using `mtg-mcp` should be able to explain:

- What is known about a card or deck and where that information came from.
- What was calculated, which inputs were used, and how uncertainty was measured.
- What is a parser result, heuristic inference, or blended score rather than a
  source fact.
- Which assumptions, unsupported mechanics, stale sources, or missing data
  limit a conclusion.
- Which deck changes are only proposed and which require explicit permission to
  apply locally or through Archidekt.

## Evidence Order

1. **Source facts**: oracle text, legality, prices, workspace contents, and
   directly observed provider fields.
2. **Source evidence**: Scryfall Tagger classifications and attributable
   Archidekt or Playgroup observations. Popularity, tournament, and combo
   sources require separately approved future PLCs.
3. **Derived mathematics**: counts, exact probabilities, and reproducible
   statistics calculated from declared inputs.
4. **Sampled estimates**: a post-cutover experimental category that would need
   model version, seed, sample count, confidence interval, assumptions, and
   input fingerprints.
5. **Parser-derived and heuristic evidence**: a separately labeled category,
   not source fact and not authority for an MCP-owned deckbuilding choice.
6. **Blended model scores**: excluded from stable `0.9.0`; any future experiment
   must expose components and meaning and remain outside the factual surface.
7. **Unsupported or unknown**: an explicit state, never an invitation to invent
   a value.

Community tags and aggregate popularity are attributable evidence, not universal
truth. Tournament evidence describes its format and population. Deterministic
output can still be heuristic.

## Non-Goals

- Do not invent facts to fill provider or card-data gaps.
- Do not claim popularity proves card quality or deck fit.
- Do not ship simulation in the stable rewrite without a separately approved
  post-cutover feasibility PLC, and never present an estimate as a real matchup
  win rate.
- Do not make the MCP a comprehensive Magic rules engine.
- Do not make deck changes without explicit authority for the affected local or
  remote operation and the workflow safeguards defined by its approved PLC.
- Do not replace the player's goals or the LLM's final reasoning with one opaque
  power score.

See the [rewrite guide](rewrite-guide.md), [design goals](design-goals.md),
[heuristic model constraints](heuristic-models.md), and the
[potential-features registry](potential-features.md).
