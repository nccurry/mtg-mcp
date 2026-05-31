# Deck Performance Architecture

## Purpose

`mtg-mcp` should help deckbuilders understand how a Magic: The Gathering deck is likely to perform before they spend table time or money on changes. The performance layer should explain consistency, castability, tempo, interaction timing, combo assembly, matchup pressure, and the expected impact of deck edit plans.

The server should remain an MCP-native insight tool: an external LLM chooses workflows and explains results, while `mtg-mcp` provides grounded card data, deterministic analysis, and safe planning tools.

## Current State

Current deck intelligence is deterministic and heuristic. It provides useful structure, but it is not a full Magic rules engine and it is not a trained deck performance model.

Existing capabilities include:

- Scryfall-backed card metadata, prices, legality, rulings, prints, and card search.
- Local and Archidekt-backed deck workspaces.
- Factual card facets and explicit predicate counts over Scryfall snapshots, workspace categories, and local annotations.
- Role and tag classification for deck cards.
- Hypergeometric and Monte Carlo draw odds for roles, tags, and turn-by-turn land drops.
- Heuristic no-opponent goldfish projection.
- Commander best-practice profiles, simulation profiles, and deck intent guidance.
- Deck-local win routes with deterministic route predicate evidence.
- Commander Spellbook catalog combo search, raw combo details, and near-miss detection.
- Evidence-first source signals from Scryfall card facts, Commander Spellbook combo catalog rows, TopDeck/Spicerack decklist samples, EDHREC-style aggregate JSON, and bounded Reddit discussion search.
- Playgroup.gg deck ranking and local-meta candidate scoring.
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

## Recommendation Source Boundaries

Recommendation sources are runtime data providers, not roadmap entries. Normal
source listings should include implemented providers only, and each provider
should report whether it is available, disabled, missing required configuration,
or failed during a lookup.

New recommendation source providers should fit one of these categories:

- Official or documented APIs with terms that allow deckbuilding evidence,
  recommendations, and attribution.
- Permissioned or permission-sensitive structured JSON endpoints where the
  integration is opt-in, bounded, clearly labeled unofficial, and cached.
- Local snapshots or fixtures that are checked into `docs/reference` for
  deterministic offline behavior.

Do not add providers that require HTML scraping, browser automation, private
web app contracts, or bulk crawling. Reverse-engineered structured endpoints
must report permission sensitivity, be bounded and cached, provide an opt-out
when default-enabled, and use fixture tests instead of live network tests.
Deck import/writeback support, such as Archidekt and Moxfield workspaces, is a
separate integration surface from source-scale deck search or recommendation
evidence.

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

Simulation profiles tune the deterministic assumptions used by goldfish and
performance analysis. Profile resolution is explicit tool argument, deck intent,
auto inference, then `neutral`. Deck intent v2 can also add local win routes
whose predicates are evaluated and returned as route evidence.

Metric definitions and validation expectations are documented in `docs/stats-lab-metrics.md`.
Profile and route syntax is documented in `docs/simulation-profiles.md`.

This layer is an abstract scenario simulator, not a rules engine. It should make that explicit in every high-level result.

## MCP Tool Shape

The public MCP surface is evidence-first and workflow-oriented. Tools return
structured rows, counts, labels, source metadata, assumptions, warnings, and
deterministic sort keys. The calling LLM is expected to do the judgment and
synthesis for the user.

Core workflow groups are:

- Card facts: `card_search`, `card_get`, `card_get_prints`, and
  `card_get_rulings`.
- Workspace lifecycle: `workspace_start`, `workspace_list`, `workspace_open`,
  `workspace_parse_decklist`, `workspace_export`, and `workspace_validate`.
- Deck structure and simulation: `deck_summarize`,
  `deck_analyze_structure`, `deck_analyze_mana`,
  `deck_analyze_consistency`, `deck_analyze_land_drop_odds`,
  `deck_analyze_performance`, `deck_simulate_goldfish`,
  `deck_project_board_state`, `deck_estimate_win_turn`, and
  `deck_plan_compare_performance`.
- Combo and win-condition evidence: `deck_analyze_combos`,
  `combo_search_by_card`, `combo_get_details`,
  `card_classify_win_routes`, `wincon_find_payoffs`,
  `commander_get_aggregate_cards`, `commander_get_tags`, and
  `commander_get_win_condition_evidence`.
- Source-backed recommendation evidence: `deck_review_new_card_swaps`,
  `deck_query_cards`, `deck_find_lesser_known_cards`,
  `deck_find_exemplar_decks`, `deck_analyze_commander_trends`,
  `source_list`, `source_search_evidence`,
  `source_search_reddit_discussions`, and
  `source_explain_card_signal`.
- Provider-local evidence and actions: `archidekt_*` provider tools and
  `playgroup_*` tools, including `deck_score_cards_for_playgroup_meta` for
  Playgroup.gg-scoped pressure.

Performance results include the selected analysis or simulation profile,
simulation count, seed, assumptions, confidence, warnings, and key metrics.
Source-backed results preserve source, source kind, source URI, cache status,
retrieval time, confidence, determinism, and notes where available.

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
