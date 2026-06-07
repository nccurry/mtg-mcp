using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MtgMcp.App;

/// <summary>
/// Provides mtg prompts behavior.
/// </summary>
[McpServerPromptType]
public sealed class MtgPrompts
{
    /// <summary>
    /// Builds a prompt for planning a Commander deck from a commander and theme.
    /// </summary>
    [McpServerPrompt(Name = "brew_commander_deck")]
    [Description("Plan a Commander deck from a commander, theme, and constraints.")]
    public string BrewCommanderDeck(string commander, string theme = "", string budget = "")
    {
        return $"""
            Brew a Commander deck for {commander}.
            Theme: {theme}
            Budget: {budget}

            Use mtg-mcp tools to card_search, validate color identity,
            workspace_start a local workspace, deck_refresh_card_metadata, deck_summarize,
            add cards by role, and explain the key packages before finalizing.
            """;
    }

    /// <summary>
    /// Builds a prompt for analyzing and tuning an existing workspace.
    /// </summary>
    [McpServerPrompt(Name = "tune_existing_deck")]
    [Description("Analyze and tune an existing local or Archidekt-bound workspace.")]
    public string TuneExistingDeck(string workspaceId, string goal = "")
    {
        return $"""
            Tune deck workspace {workspaceId}.
            Goal: {goal}

            Read deck_intent_get first. If no intent exists, use deck_intent_suggest and ask the user
            whether to save it. Run deck_refresh_card_metadata if needed, deck_summarize, deck_analyze_draw_odds,
            identify weak roles or curve issues, gather candidate data with deck_query_cards,
            and only create or apply deck plans after the user approves the exact change direction.
            """;
    }

    /// <summary>
    /// Builds a prompt for iterative evidence-first deck review after edits or re-imports.
    /// </summary>
    [McpServerPrompt(Name = "iterative_deck_review")]
    [Description("Review what changed, current holes, role evidence, weak-slot rows, and previewable packages for an iterative deck-tuning pass.")]
    public string IterativeDeckReview(string workspaceId, string previousWorkspaceId = "", string goal = "")
    {
        return $"""
            Iteratively review deck workspace {workspaceId}.
            Previous workspace: {previousWorkspaceId}
            Goal: {goal}

            Read mtg://workspace/{workspaceId}/state and mtg://workspace/{workspaceId}/assistant-context.
            If previousWorkspaceId is provided, call workspace_diff with that explicit baseline and cite
            the returned baseline id, source, and timestamp. Use deck_review_weak_spots for evidence-only
            weak-slot rows, deck_explain_role_counts for any disputed role totals, and source tools when
            popularity or discussion evidence matters. For candidate packages, use deck_preview_card_package
            before creating a persistent plan. Keep deterministic evidence separate from final synthesis,
            and do not apply mutations without explicit user approval.
            """;
    }

    /// <summary>
    /// Builds a prompt for commander aggregate card research.
    /// </summary>
    [McpServerPrompt(Name = "research_commander_common_cards")]
    [Description("Gather source-backed cards commonly associated with a commander.")]
    public string ResearchCommanderCommonCards(string commander, string theme = "", string source = "")
    {
        return $"""
            Research common cards for commander {commander}.
            Theme: {theme}
            Source: {source}

            Use commander_get_aggregate_cards and commander_get_tags. Keep source populations separate,
            cite source metadata, and do not merge counts across unlike sources. Treat Reddit as raw
            discussion evidence and TopDeck as tournament/decklist sample evidence.
            """;
    }

    /// <summary>
    /// Builds a prompt for commander win-condition evidence research.
    /// </summary>
    [McpServerPrompt(Name = "research_commander_win_conditions")]
    [Description("Gather structured win-condition evidence for a commander.")]
    public string ResearchCommanderWinConditions(string commander, string theme = "")
    {
        return $"""
            Research win-condition evidence for commander {commander}.
            Theme: {theme}

            Use commander_get_win_condition_evidence, combo_get_details when a combo needs inspection,
            card_classify_win_routes for route labels, and wincon_find_payoffs for non-terminal routes.
            Return structured evidence first; write any conclusions separately and label source caveats.
            """;
    }

    /// <summary>
    /// Builds a prompt for lowering deck cost while preserving the game plan.
    /// </summary>
    [McpServerPrompt(Name = "reduce_deck_cost")]
    [Description("Reduce deck cost while preserving roles, color identity, legality, and core functionality.")]
    public string ReduceDeckCost(string workspaceId, string budgetTarget = "")
    {
        return $"""
            Reduce the cost of deck workspace {workspaceId}.
            Budget target: {budgetTarget}

            Use deck_refresh_card_metadata, deck_analyze_cost, deck_summarize, and explicit
            deck_query_cards lookups. Preserve color identity, role coverage, format legality, and
            the deck's stated game plan. Return source data and candidate swaps; create and preview a plan
            only after the user approves the proposed query and targets.
            """;
    }

    /// <summary>
    /// Builds a prompt for targeted deck power upgrades.
    /// </summary>
    [McpServerPrompt(Name = "upgrade_deck_power")]
    [Description("Increase deck power with targeted upgrades and plan preview.")]
    public string UpgradeDeckPower(string workspaceId, string focus = "balanced", string maxPrice = "")
    {
        return $"""
            Upgrade deck workspace {workspaceId}.
            Focus: {focus}
            Max price: {maxPrice}

            Use deck_refresh_card_metadata, deck_summarize, deck_analyze_consistency,
            and deck_query_cards with explicit Scryfall syntax. Preserve color identity and legality.
            Return source data first; create and preview a plan only after the user approves the targets.
            """;
    }

    /// <summary>
    /// Builds a prompt for gentler replacements that reduce deck power or salt.
    /// </summary>
    [McpServerPrompt(Name = "reduce_deck_power")]
    [Description("Reduce deck power or salt while keeping the deck functional.")]
    public string ReduceDeckPower(string workspaceId, string targetPower = "casual")
    {
        return $"""
            Reduce the power of deck workspace {workspaceId}.
            Target power: {targetPower}

            Use deck_refresh_card_metadata, deck_summarize, deck_estimate_commander_bracket,
            and deck_query_cards with explicit replacement searches. Prefer gentler replacements over
            removing core identity cards. Return source data and likely gameplay impact; create a plan only
            after the user approves the exact changes.
            """;
    }

    /// <summary>
    /// Builds a prompt for lowering an estimated Commander bracket.
    /// </summary>
    [McpServerPrompt(Name = "lower_commander_bracket")]
    [Description("Lower an estimated Commander bracket using Game Changer and power-pressure analysis.")]
    public string LowerCommanderBracket(string workspaceId, int targetBracket = 2)
    {
        return $"""
            Lower deck workspace {workspaceId} toward Commander bracket {targetBracket}.

            Use deck_refresh_card_metadata, deck_estimate_commander_bracket, deck_facets_get, and explicit
            deck_query_cards lookups for replacement data. Treat bracket output as an advisory estimate
            for pregame discussion, not an official ruling. Game Changer data comes live from Scryfall.
            Create a plan only after the user approves exact changes.

            Bracket beta context: https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026
            """;
    }

    /// <summary>
    /// Builds a prompt for mana-base analysis and replacement searches.
    /// </summary>
    [McpServerPrompt(Name = "optimize_mana_base")]
    [Description("Improve a deck mana base with analysis, recommendations, and preview.")]
    public string OptimizeManaBase(string workspaceId, string maxPrice = "10")
    {
        return $"""
            Optimize the mana base for deck workspace {workspaceId}.
            Max price: {maxPrice}

            Use deck_refresh_card_metadata, deck_analyze_mana, and explicit deck_query_cards
            lookups for lands or fixing. Preserve color identity, legality, and budget. Return
            land-count, color-source, tapped-land, and fixing data before creating any plan.
            """;
    }

    /// <summary>
    /// Builds a prompt for improving ramp, draw, tutor, or card-selection consistency.
    /// </summary>
    [McpServerPrompt(Name = "improve_deck_consistency")]
    [Description("Improve ramp, draw, tutor, card-selection, or balanced consistency.")]
    public string ImproveDeckConsistency(string workspaceId, string focus = "balanced", string maxPrice = "10")
    {
        return $"""
            Improve consistency for deck workspace {workspaceId}.
            Focus: {focus}
            Max price: {maxPrice}

            Use deck_refresh_card_metadata, deck_analyze_consistency, deck_analyze_draw_odds, and
            explicit deck_query_cards lookups. Preserve color identity, legality, and the deck's
            core plan. Return source data and role-density changes before creating any plan.
            """;
    }

    /// <summary>
    /// Builds a prompt for tuning against stated local metagame pressures.
    /// </summary>
    [McpServerPrompt(Name = "tune_for_local_meta")]
    [Description("Tune a deck for stated local metagame pressures.")]
    public string TuneForLocalMeta(string workspaceId, string meta, string budget = "10")
    {
        return $"""
            Tune deck workspace {workspaceId} for this local meta: {meta}
            Budget per card: {budget}

            Use deck_analyze_best_practices, deck_score_cards_for_playgroup_meta when a
            Playgroup URL or ranked candidate list is available, deck_query_cards
            when you can produce a precise Scryfall query, deck_analyze_combos,
            and source data tools. Create a deck plan only after the query and exact
            add/remove choices are explicit. Explain which local-meta problem each
            package addresses.
            """;
    }

    /// <summary>
    /// Builds a prompt for reviewing new card releases against a workspace.
    /// </summary>
    [McpServerPrompt(Name = "review_new_card_swaps")]
    [Description("Review new card candidates and deterministic cuts for a deck.")]
    public string ReviewNewCardSwaps(string workspaceId, string since = "", string setCode = "", string maxPrice = "")
    {
        return $"""
            Review new-card swaps for deck workspace {workspaceId}.
            Since: {since}
            Set: {setCode}
            Max price: {maxPrice}

            Use deck_review_new_card_swaps and deck_summarize. Return candidate rows and deterministic
            cut evidence before suggesting any edits. Create a deck plan only after the user approves
            exact adds and cuts.
            """;
    }

    /// <summary>
    /// Builds a prompt for checking early land-drop risk.
    /// </summary>
    [McpServerPrompt(Name = "check_land_drop_risk")]
    [Description("Check whether a deck is likely to miss early land drops.")]
    public string CheckLandDropRisk(string workspaceId, int turn = 3, bool onThePlay = false)
    {
        return $"""
            Check land-drop risk for deck workspace {workspaceId}.
            Turn: {turn}
            On the play: {onThePlay}

            Use deck_analyze_land_drop_odds and deck_analyze_mana. Explain exact no-mulligan odds
            separately from deterministic mulligan simulation, including assumptions and failure drivers.
            """;
    }

    /// <summary>
    /// Builds a prompt for finding missing combo pieces.
    /// </summary>
    [McpServerPrompt(Name = "find_missing_combo_pieces")]
    [Description("Find missing combo pieces and payoff needs for a deck.")]
    public string FindMissingComboPieces(string workspaceId)
    {
        return $"""
            Find missing combo pieces for deck workspace {workspaceId}.

            Use deck_analyze_combos with near misses enabled, combo_get_details for catalog ids,
            and card_classify_win_routes to separate terminal routes from loops that need payoffs.
            Use wincon_find_payoffs only for non-terminal route labels.
            """;
    }

    /// <summary>
    /// Builds a prompt for projecting no-interaction deck performance.
    /// </summary>
    [McpServerPrompt(Name = "goldfish_deck")]
    [Description("Project board state and win timing when a deck is not interacted with.")]
    public string GoldfishDeck(string workspaceId, int targetTurn = 5, int simulations = 1000)
    {
        return $"""
            Goldfish deck workspace {workspaceId}.
            Target turn: {targetTurn}
            Simulations: {simulations}

            Use deck_simulate_goldfish, deck_project_board_state, and deck_estimate_win_turn. Explain that the
            output assumes no opponent interaction and uses heuristics rather than a full rules engine.
            """;
    }

    /// <summary>
    /// Builds a prompt for improving a deck toward a natural-language goal.
    /// </summary>
    [McpServerPrompt(Name = "improve_deck_for_goal")]
    [Description("Find a previewable package that makes a deck better at a stated goal.")]
    public string MakeDeckDoGoalBetter(string workspaceId, string goal, string budget = "10")
    {
        return $"""
            Make deck workspace {workspaceId} better at: {goal}
            Budget per card: {budget}

            Prefer deck_query_cards when you can produce a precise Scryfall query. Create a deck
            plan only after the query and exact add/remove choices are explicit. Also run
            deck_analyze_best_practices. Return candidate source data, likely tradeoffs, and any
            plan id only after explicit approval.
            """;
    }

    /// <summary>
    /// Builds a prompt for checking card rulings and interaction questions.
    /// </summary>
    [McpServerPrompt(Name = "rules_and_rulings_check")]
    [Description("Check rulings and legality for specific cards or deck interactions.")]
    public string RulesAndRulingsCheck(string cardsOrWorkspace, string question)
    {
        return $"""
            Check Magic rules and card rulings for: {cardsOrWorkspace}
            Question: {question}

            Use card_get and card_get_rulings for the named cards. Separate official rulings from strategic interpretation.
            """;
    }
}
