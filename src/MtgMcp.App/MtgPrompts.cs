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
    /// Handles brew commander deck.
    /// </summary>
    [McpServerPrompt(Name = "brew_commander_deck")]
    [Description("Plan a Commander deck from a commander, theme, and constraints.")]
    public string BrewCommanderDeck(string commander, string theme = "", string budget = "")
    {
        return $"""
            Brew a Commander deck for {commander}.
            Theme: {theme}
            Budget: {budget}

            Use mtg-mcp tools to search Scryfall, validate color identity,
            create a local workspace, refresh card snapshots, summarize the deck workspace,
            add cards by role, and explain the key packages before finalizing.
            """;
    }

    /// <summary>
    /// Handles tune existing deck.
    /// </summary>
    [McpServerPrompt(Name = "tune_existing_deck")]
    [Description("Analyze and tune an existing local or Archidekt-bound workspace.")]
    public string TuneExistingDeck(string workspaceId, string goal = "")
    {
        return $"""
            Tune deck workspace {workspaceId}.
            Goal: {goal}

            Read get_deck_intent first. If no intent exists, use suggest_deck_intent and ask the user
            whether to save it. Refresh deck card snapshots if needed, summarize the deck workspace, analyze draw odds,
            identify weak roles or curve issues, gather candidate data with explicit Scryfall queries,
            and only create or apply deck plans after the user approves the exact change direction.
            """;
    }

    /// <summary>
    /// Researches budget replacement data.
    /// </summary>
    [McpServerPrompt(Name = "research_budget_replacements")]
    [Description("Gather cheaper card replacement data for a deck workspace.")]
    public string ResearchBudgetReplacements(string workspaceId, string budgetTarget = "")
    {
        return $"""
            Find budget replacements for deck workspace {workspaceId}.
            Budget target: {budgetTarget}

            Use get_deck_intent, refresh_deck_card_snapshots, analyze_deck_cost, and explicit
            query_cards_for_deck lookups. Preserve color identity, role, protected cards/packages,
            mana curve, and format legality. Return candidate data first; create a plan only after
            the replacement query and target cards are explicit.
            """;
    }

    /// <summary>
    /// Handles reduce deck cost.
    /// </summary>
    [McpServerPrompt(Name = "reduce_deck_cost")]
    [Description("Reduce deck cost while preserving roles, color identity, legality, and core functionality.")]
    public string ReduceDeckCost(string workspaceId, string budgetTarget = "")
    {
        return $"""
            Reduce the cost of deck workspace {workspaceId}.
            Budget target: {budgetTarget}

            Use refresh_deck_card_snapshots, analyze_deck_cost, summarize_deck_workspace, and explicit
            query_cards_for_deck lookups. Preserve color identity, role coverage, format legality, and
            the deck's stated game plan. Return source data and candidate swaps; create and preview a plan
            only after the user approves the proposed query and targets.
            """;
    }

    /// <summary>
    /// Handles upgrade deck power.
    /// </summary>
    [McpServerPrompt(Name = "upgrade_deck_power")]
    [Description("Increase deck power with targeted upgrades and plan preview.")]
    public string UpgradeDeckPower(string workspaceId, string focus = "balanced", string maxPrice = "")
    {
        return $"""
            Upgrade deck workspace {workspaceId}.
            Focus: {focus}
            Max price: {maxPrice}

            Use refresh_deck_card_snapshots, summarize_deck_workspace, analyze_deck_consistency,
            and query_cards_for_deck with explicit Scryfall syntax. Preserve color identity and legality.
            Return source data first; create and preview a plan only after the user approves the targets.
            """;
    }

    /// <summary>
    /// Handles reduce deck power.
    /// </summary>
    [McpServerPrompt(Name = "reduce_deck_power")]
    [Description("Reduce deck power or salt while keeping the deck functional.")]
    public string ReduceDeckPower(string workspaceId, string targetPower = "casual")
    {
        return $"""
            Reduce the power of deck workspace {workspaceId}.
            Target power: {targetPower}

            Use refresh_deck_card_snapshots, summarize_deck_workspace, estimate_commander_bracket,
            and query_cards_for_deck with explicit replacement searches. Prefer gentler replacements over
            removing core identity cards. Return source data and likely gameplay impact; create a plan only
            after the user approves the exact changes.
            """;
    }

    /// <summary>
    /// Handles lower commander bracket.
    /// </summary>
    [McpServerPrompt(Name = "lower_commander_bracket")]
    [Description("Lower an estimated Commander bracket using Game Changer and power-pressure analysis.")]
    public string LowerCommanderBracket(string workspaceId, int targetBracket = 2)
    {
        return $"""
            Lower deck workspace {workspaceId} toward Commander bracket {targetBracket}.

            Use refresh_deck_card_snapshots, estimate_commander_bracket, get_deck_facets, and explicit
            query_cards_for_deck lookups for replacement data. Treat bracket output as an advisory estimate
            for pregame discussion, not an official ruling. Game Changer data comes live from Scryfall.
            Create a plan only after the user approves exact changes.

            Bracket beta context: https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026
            """;
    }

    /// <summary>
    /// Handles optimize mana base.
    /// </summary>
    [McpServerPrompt(Name = "optimize_mana_base")]
    [Description("Improve a deck mana base with analysis, recommendations, and preview.")]
    public string OptimizeManaBase(string workspaceId, string maxPrice = "10")
    {
        return $"""
            Optimize the mana base for deck workspace {workspaceId}.
            Max price: {maxPrice}

            Use refresh_deck_card_snapshots, analyze_mana_base, and explicit query_cards_for_deck
            lookups for lands or fixing. Preserve color identity, legality, and budget. Return
            land-count, color-source, tapped-land, and fixing data before creating any plan.
            """;
    }

    /// <summary>
    /// Handles improve deck consistency.
    /// </summary>
    [McpServerPrompt(Name = "improve_deck_consistency")]
    [Description("Improve ramp, draw, tutor, card-selection, or balanced consistency.")]
    public string ImproveDeckConsistency(string workspaceId, string focus = "balanced", string maxPrice = "10")
    {
        return $"""
            Improve consistency for deck workspace {workspaceId}.
            Focus: {focus}
            Max price: {maxPrice}

            Use refresh_deck_card_snapshots, analyze_deck_consistency, analyze_draw_odds, and
            explicit query_cards_for_deck lookups. Preserve color identity, legality, and the deck's
            core plan. Return source data and role-density changes before creating any plan.
            """;
    }

    /// <summary>
    /// Handles tuning for a local meta.
    /// </summary>
    [McpServerPrompt(Name = "tune_for_local_meta")]
    [Description("Tune a deck for stated local metagame pressures.")]
    public string TuneForLocalMeta(string workspaceId, string meta, string budget = "10")
    {
        return $"""
            Tune deck workspace {workspaceId} for this local meta: {meta}
            Budget per card: {budget}

            Use analyze_deck_best_practices, query_cards_for_deck when you can produce
            a precise Scryfall query, estimate_combo_pressure, and source data tools. Create a deck
            plan only after the query and exact add/remove choices are explicit. Explain which
            local-meta problem each package addresses.
            """;
    }

    /// <summary>
    /// Handles reviewing new releases.
    /// </summary>
    [McpServerPrompt(Name = "review_new_releases_for_deck")]
    [Description("Review newly released cards that fit a deck.")]
    public string ReviewNewReleasesForDeck(string workspaceId, string since = "", string setCode = "", string maxPrice = "")
    {
        return $"""
            Review new cards for deck workspace {workspaceId}.
            Since: {since}
            Set: {setCode}
            Max price: {maxPrice}

            Use find_new_cards_for_deck, compare_to_commander_meta, and summarize_deck_workspace.
            Return cards by role, theme fit, price, and likely cuts when relevant.
            """;
    }

    /// <summary>
    /// Handles goldfishing a deck.
    /// </summary>
    [McpServerPrompt(Name = "goldfish_deck")]
    [Description("Project board state and win timing when a deck is not interacted with.")]
    public string GoldfishDeck(string workspaceId, int targetTurn = 5, int simulations = 1000)
    {
        return $"""
            Goldfish deck workspace {workspaceId}.
            Target turn: {targetTurn}
            Simulations: {simulations}

            Use simulate_goldfish, project_board_state, and estimate_win_turn. Explain that the
            output assumes no opponent interaction and uses heuristics rather than a full rules engine.
            """;
    }

    /// <summary>
    /// Handles improving a deck toward a natural-language goal.
    /// </summary>
    [McpServerPrompt(Name = "make_deck_do_goal_better")]
    [Description("Find a previewable package that makes a deck better at a stated goal.")]
    public string MakeDeckDoGoalBetter(string workspaceId, string goal, string budget = "10")
    {
        return $"""
            Make deck workspace {workspaceId} better at: {goal}
            Budget per card: {budget}

            Prefer query_cards_for_deck when you can produce a precise Scryfall query. Create a deck
            plan only after the query and exact add/remove choices are explicit. Also run
            analyze_deck_best_practices. Return candidate source data, likely tradeoffs, and any
            plan id only after explicit approval.
            """;
    }

    /// <summary>
    /// Handles rules and rulings check.
    /// </summary>
    [McpServerPrompt(Name = "rules_and_rulings_check")]
    [Description("Check rulings and legality for specific cards or deck interactions.")]
    public string RulesAndRulingsCheck(string cardsOrWorkspace, string question)
    {
        return $"""
            Check Magic rules and card rulings for: {cardsOrWorkspace}
            Question: {question}

            Use get_card and get_rulings for the named cards. Separate official rulings from strategic interpretation.
            """;
    }
}
