# North Star

`mtg-mcp` helps LLMs make informed Magic: The Gathering deckbuilding decisions
by supplying grounded evidence. The MCP gathers and computes; the calling LLM
connects that evidence to a player's goals and makes the judgment.

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
2. **Source evidence**: Scryfall Tagger classifications, EDHREC aggregates,
   tournament decklists, Commander Spellbook rows, and Playgroup observations.
3. **Derived mathematics**: counts, exact probabilities, and reproducible
   statistics calculated from declared inputs.
4. **Sampled estimates**: Monte Carlo results with model version, seed, sample
   count, confidence interval, assumptions, and input fingerprints.
5. **Parser-derived and heuristic evidence**: repeatable classifications that
   are not direct provider facts.
6. **Blended model scores**: weighted or blended outputs whose components and meaning
   must remain visible.
7. **Unsupported or unknown**: an explicit state, never an invitation to invent
   a value.

Community tags and aggregate popularity are attributable evidence, not universal
truth. Tournament evidence describes its format and population. Deterministic
output can still be heuristic.

## Non-Goals

- Do not invent facts to fill provider or card-data gaps.
- Do not claim popularity proves card quality or deck fit.
- Do not present a simulation estimate as a real matchup win rate.
- Do not make the MCP a comprehensive Magic rules engine.
- Do not make irreversible deck changes without explicit apply permission.
- Do not replace the player's goals or the LLM's final reasoning with one opaque
  power score.

See [design goals](design-goals.md), [heuristic models](heuristic-models.md), and
the [potential-features registry](potential-features.md). Historical rationale
also remains in the planned
[MCP trust evidence PLC](llms/plcs/planned/mcp-trust-evidence/README.md).
