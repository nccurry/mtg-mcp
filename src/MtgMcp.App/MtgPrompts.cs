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
            create a local workspace, normalize card metadata, summarize the deck plan,
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

            Normalize the deck first if needed. Then summarize the deck plan, analyze draw odds,
            identify weak roles or curve issues, create recommendation plans, and only use
            apply_deck_plan after the user approves the plan.
            """;
    }

    /// <summary>
    /// Finds the budget replacements.
    /// </summary>
    [McpServerPrompt(Name = "find_budget_replacements")]
    [Description("Find cheaper card replacements for a deck workspace.")]
    public string FindBudgetReplacements(string workspaceId, string budgetTarget = "")
    {
        return $"""
            Find budget replacements for deck workspace {workspaceId}.
            Budget target: {budgetTarget}

            Use normalize_deck_cards and find_budget_replacements. Preserve color identity,
            role, mana curve, and format legality. Return the persisted plan id and do not
            mutate the deck unless the user explicitly asks to apply the plan.
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

            Use normalize_deck_cards, analyze_deck_cost, summarize_deck_plan, find_budget_replacements,
            and preview_deck_plan. Preserve color identity, role coverage, format legality, and the deck's
            stated game plan. Return the plan id, preview deltas, and tradeoffs. Do not use apply_deck_plan
            unless the user explicitly approves the preview.
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

            Use normalize_deck_cards, summarize_deck_plan, analyze_deck_consistency,
            find_power_upgrades, and preview_deck_plan. Preserve color identity and legality.
            Return the plan id and before/after metrics. Do not use apply_deck_plan unless the user
            explicitly approves the preview.
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

            Use normalize_deck_cards, summarize_deck_plan, estimate_commander_bracket,
            find_power_reduction_candidates, and preview_deck_plan. Prefer gentler replacements over
            removing core identity cards. Return the plan id, preview deltas, and likely gameplay impact.
            Do not use apply_deck_plan unless the user explicitly approves the preview.
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

            Use normalize_deck_cards, estimate_commander_bracket, find_bracket_reduction_candidates,
            and preview_deck_plan. Treat bracket output as an advisory estimate for pregame discussion,
            not an official ruling. Game Changer data comes live from Scryfall. Return the plan id,
            bracket signals addressed, and before/after bracket estimate. Do not use apply_deck_plan
            unless the user explicitly approves the preview.

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

            Use normalize_deck_cards, analyze_mana_base, find_mana_base_improvements,
            and preview_deck_plan. Preserve color identity, legality, and budget. Return land-count,
            color-source, tapped-land, and fixing deltas. Do not use apply_deck_plan unless the user
            explicitly approves the preview.
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

            Use normalize_deck_cards, analyze_deck_consistency, find_consistency_improvements,
            and preview_deck_plan. Preserve color identity, legality, and the deck's core plan.
            Return the plan id, key draw-odds deltas, and role-density changes. Do not use
            apply_deck_plan unless the user explicitly approves the preview.
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
