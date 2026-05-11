# Deck Performance Architecture

## Purpose

`mtg-mcp` should help deckbuilders understand how a Magic: The Gathering deck is likely to perform before they spend table time or money on changes. The performance layer should explain consistency, castability, tempo, interaction timing, combo assembly, matchup pressure, and the expected impact of deck edit plans.

The server should remain an MCP-native insight tool: an external LLM chooses workflows and explains results, while `mtg-mcp` provides grounded card data, deterministic analysis, and safe planning tools.

## Current State

Current deck intelligence is deterministic and heuristic. It provides useful structure, but it is not a full Magic rules engine and it is not a trained deck performance model.

Existing capabilities include:

- Scryfall-backed card metadata, prices, legality, rulings, prints, and card search.
- Local and Archidekt-backed deck workspaces.
- Role and tag classification for deck cards.
- Hypergeometric and Monte Carlo draw odds for roles or tags.
- Heuristic no-opponent goldfish projection.
- Commander best-practice profiles and deck intent guidance.
- Commander Spellbook combo and near-miss detection.
- Corpus/context signals from Scryfall, Commander Spellbook, TopDeck, and Spicerack.
- Previewable deck edit plans before any local or Archidekt mutation.
- Stats Lab whole-deck performance analysis for opening hands, land drops, colors, castability, commander timing, combo/tutor assembly, stranded-card risk, and named scenarios.
- Previewed plan performance comparison with before/after deltas and confidence interval context.

These tools answer questions like "how much ramp do I have?" or "what are my odds of seeing draw by turn 3?" They do not currently answer full rules questions like "what is my real win rate against this opponent deck under legal game actions?"

## Design Goals

- Keep the default install lightweight and .NET-only.
- Keep `MtgMcp.Core` independent from adapter and host projects.
- Prefer explainable statistical analysis over opaque full-game simulation.
- Return assumptions, confidence intervals, warnings, and failure modes with performance results.
- Avoid presenting abstract simulation as full Magic rules enforcement.
- Keep normal tests offline, deterministic, and free of real Archidekt mutations.

## Stats Lab Design

The first performance layer should live in Core as pure C# analysis over `DeckWorkspace`, card snapshots, deck intent, roles, tags, and deck edit plans.

The Stats Lab should model:

- Opening-hand quality and London mulligan outcomes.
- Land drop reliability by turn.
- Ramp, draw, tutor, and interaction timing.
- Colored-source availability and spell castability by turn.
- Exclusive mana-source payment for colored, hybrid, colorless, X, and Phyrexian-style costs using cached Scryfall mana data.
- Tapland pressure and early tempo loss.
- Commander castability and commander-on-curve odds.
- Combo assembly odds, including tutor-equivalent cards when realistically castable.
- Stranded-card rates for expensive or color-intensive cards.
- Before/after performance deltas for previewed deck edit plans.
- Confidence intervals and sensitivity signals for recommended changes.

Metric definitions and validation expectations are documented in `docs/stats-lab-metrics.md`.

This layer is an abstract scenario simulator, not a rules engine. It should make that explicit in every high-level result.

## MCP Tool Shape

Implemented tools are high-level and deckbuilder-facing:

- `analyze_deck_performance`
- `compare_plan_performance`

These tools return compact structured summaries that an LLM can explain without guessing. Results include the analysis profile, simulation count, seed, assumptions, confidence, warnings, and key metrics.

No external matchup simulation tools are currently exposed.

## Deferred Full-Game Simulation

Full rules simulation is deferred until there is a proven end-to-end adapter that can accept decklists, run games, and return stable machine-readable results. It should not be required for baseline deck tuning.

A future backend flow would need to:

1. Export a `DeckWorkspace` into the backend's deck format.
2. Write a temporary backend configuration with format, decks, pilots, game count, and output directory.
3. Launch the backend runner as a subprocess.
4. Parse machine-readable result files or logs.
5. Return a normalized MCP result with summary metrics and artifact paths.

Future backend results should report:

- Backend name and version.
- Pilot type, such as CPU, deterministic autopilot, or LLM.
- Games requested and games completed.
- Win rate with confidence interval.
- Average win/loss turn.
- Mulligan statistics.
- Unimplemented-card or runner warnings.
- Representative game logs or raw artifact paths.

## XMage and mage-bench Positioning

XMage is a Java client/server Magic engine with broad card coverage, rules enforcement, Commander and multiplayer support, and computer opponents. It is powerful but not a small embedded library.

mage-bench is a benchmark and orchestration stack built on XMage. It has:

- An XMage server for rules enforcement and game state.
- Java bridge clients that expose MCP tools to in-game LLM pilots.
- A Python puppeteer that starts processes, connects pilots, records logs, tracks costs, and optionally records video.

mage-bench is closer to a local game-lab stack than a simple CLI binary. It requires Java 21+, Maven, Python 3.11+, `uv`, Make, and optionally FFmpeg, card images, and LLM API keys. Because it is not a drop-in deck simulation API, `mtg-mcp` does not currently expose XMage or mage-bench functionality.

## Non-Goals

- Do not write a full Magic rules engine inside `mtg-mcp`.
- Do not vendor XMage or mage-bench into `mtg-mcp`.
- Do not require external simulator dependencies for ordinary deck tuning.
- Do not claim abstract statistical simulation provides true matchup win rates.
- Do not make normal tests depend on network access, external engines, or real Archidekt writeback.
