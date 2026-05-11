# Deck Performance Roadmap

## Summary

The deck performance roadmap is centered on one production track:

1. Build a lightweight, .NET-only Stats Lab that gives deckbuilders immediate, explainable performance insight.

The baseline experience should remain easy to install through the existing `mtg-mcp` tool. Heavy simulator dependencies are not part of the current implementation.

## Phase 1: Stats Lab v1

Status: implemented.

Implemented pure C# performance analysis in Core and exposed it through MCP.

Planned tools:

- `analyze_deck_performance`
- `compare_plan_performance`

Core features:

- Opening-hand quality scoring.
- London mulligan distribution.
- Land-drop odds by turn.
- Ramp timing and early mana development.
- Card draw and card-selection timing.
- Interaction availability by turn.
- Colored-source reliability.
- Commander castability by turn.
- Basic combo and tutor assembly odds.
- Stranded-card rates for expensive or color-intensive cards.
- Before/after performance deltas for persisted deck edit plans.
- Confidence intervals for simulation-derived metrics.
- Metric contracts and validation expectations in `docs/stats-lab-metrics.md`.

Results should clearly state that this is abstract statistical analysis, not full rules enforcement.

## Phase 2: Scenario Simulation

Status: implemented for the first built-in scenario suite, with more domain-specific scenarios still possible.

Added named deckbuilding scenarios on top of the Stats Lab. These remain pure .NET and use deck intent when available.

Example scenarios:

- Cast commander by turn N.
- Cast commander by turn N while holding protection.
- Have graveyard hate before an expected opposing combo turn.
- Have two or three required colors by a target turn.
- Develop the board while holding up interaction.
- Find a combo piece plus tutor by a target turn.
- Avoid hands with too many stranded high-mana cards.

Scenario output identifies the relevant cards, the modeled assumptions, the probability band, and failure-driver counts.

## Phase 3: Stats Lab Refinement

Status: next.

Improve the deterministic performance model before adding any full-game simulator integration.

Possible refinements:

- Better profile-specific sequencing for archetypes such as stax, reanimator, spellslinger, and creature combo.
- Role density sensitivity analysis that estimates the impact of adding one more ramp, land, draw, tutor, or interaction card.
- More nuanced mulligan heuristics by deck intent and commander plan.
- More explicit tapland, color-fixing, and curve pressure summaries.

## Phase 4: Deferred Rules-Engine Research

Status: deferred, not implemented.

Full rules-engine matchup simulation should come back only after a proof of concept can run end to end:

1. Accept two or more decklists from `mtg-mcp`.
2. Run a real game batch without manual setup.
3. Produce stable machine-readable results.
4. Report coverage gaps and unimplemented-card failures.
5. Stay outside normal install and normal tests.

XMage and mage-bench are useful prior art, but they are not currently a drop-in `mtg-mcp` simulation backend.

## Phase 5: Advanced Research Ideas

Longer-term possibilities:

- Matchup matrices across a local deck suite.
- Corpus-informed performance predictions.
- Learned card and deck embeddings.
- Simulation-informed recommendations.
- Replay and blunder analysis.
- Side-by-side pilot comparisons.
- "Why did this deck lose?" summaries.
- Identification of cards that look strong statistically but underperform in simulated lines.
- Identification of cards that overperform in simulations but are underrepresented in corpus data.

These ideas should be treated as research features until the data quality, pilot strength, and confidence reporting are good enough for deckbuilder-facing output.

## Dependency Policy

- Baseline performance features must remain .NET-only.
- Normal tests must remain offline and must not require external engines, API keys, network access, or real Archidekt decks.
- Any future external-engine experiment must be explicitly isolated from normal install, normal config, and normal `task test`.

## Prior Art

- [XMage](https://github.com/magefree/mage): Java Magic engine with broad card coverage, rules enforcement, Commander, multiplayer, and AI opponents.
- [mage-bench](https://github.com/GregorStocks/mage-bench): XMage-based benchmark and orchestration stack for LLMs playing Magic through MCP tools.
- [Forge](https://github.com/Card-Forge/forge): Mature open-source Magic rules engine and AI play environment.
- [Magarena MCTS notes](https://github.com/magarena/magarena/wiki/AIMonteCarloTreeSearch): Older but relevant reference for Monte Carlo Tree Search in an MTG-like engine.
- [MTGJSON](https://www.mtgjson.net/): Structured MTG card data source that may be useful for future offline/card-data workflows.
