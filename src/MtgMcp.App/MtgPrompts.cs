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
            create a local workspace, add cards by role, and explain the key packages before finalizing.
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

            Analyze the deck, identify weak categories or curve issues, search for replacements,
            then use writeback tools when the deck is Archidekt-bound.
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

            Use Scryfall price filters and deck analysis.
            Preserve color identity, role, mana curve, and format legality.
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
