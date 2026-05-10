namespace MtgMcp.Core;

/// <summary>
/// Suggests starter deck intent sections.
/// </summary>
public static partial class DeckIntentText
{
    /// <summary>
    /// Creates a starter intent for a workspace.
    /// </summary>
    public static DeckIntent Suggest(DeckWorkspace workspace)
    {
        DeckIntent intent = new()
        {
            Format = workspace.Format,
            Commander = FindCommander(workspace),
            Archetype = SuggestArchetype(workspace),
            PowerLevel = "tuned-casual",
            HeuristicProfile = "auto",
            Budget = new DeckIntentBudget
            {
                Text = "prefer cheaper swaps unless a card is core",
                PreferCheaperSwaps = true
            }
        };

        intent.Targets[DeckRoles.Lands] = Target("36-37", 36, 37);
        intent.Targets[DeckRoles.Ramp] = Target("8-10", 8, 10);
        intent.Targets[DeckRoles.Draw] = Target("9-11", 9, 11);
        intent.Targets[DeckRoles.Interaction] = Target("10-14", 10, 14);
        intent.Targets[DeckRoles.BoardWipes] = Target("2-4", 2, 4);
        intent.Priorities = new ReplacementWeights();
        intent.Prefer.AddRange(SuggestPreferences(workspace));
        intent.Avoid.AddRange(["infinite combos", "hard stax"]);
        intent.Protect.AddRange(SuggestProtectedCards(workspace));
        return intent;
    }

    /// <summary>
    /// Finds the commander card.
    /// </summary>
    private static string? FindCommander(DeckWorkspace workspace)
    {
        return workspace.Cards
            .FirstOrDefault(card =>
                string.Equals(card.PrimaryCategory, DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
                || (card.Categories ?? []).Any(category => category.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)))
            ?.Name;
    }

    /// <summary>
    /// Suggests a broad archetype from tags and categories.
    /// </summary>
    private static string SuggestArchetype(DeckWorkspace workspace)
    {
        string text = string.Join(' ', workspace.Categories.Select(category => category.Name));
        if (workspace.Cards.Any(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Discard, StringComparer.OrdinalIgnoreCase))
            || ContainsAny(text, "discard"))
        {
            return "discard-control";
        }

        if (ContainsAny(text, "aristocrats", "death", "sacrifice"))
        {
            return "aristocrats";
        }

        return "synergy";
    }

    /// <summary>
    /// Suggests preference lines from the current deck.
    /// </summary>
    private static IEnumerable<string> SuggestPreferences(DeckWorkspace workspace)
    {
        string archetype = SuggestArchetype(workspace);
        if (archetype == "discard-control")
        {
            return ["repeatable discard", "discard payoffs", "cards that work without the commander"];
        }

        if (archetype == "aristocrats")
        {
            return ["death triggers", "sacrifice outlets", "recursive threats"];
        }

        return ["role fit", "mana efficiency", "cards that support the current plan"];
    }

    /// <summary>
    /// Suggests protected cards.
    /// </summary>
    private static IEnumerable<string> SuggestProtectedCards(DeckWorkspace workspace)
    {
        List<string> protectedCards = [];
        string? commander = FindCommander(workspace);
        if (!string.IsNullOrWhiteSpace(commander))
        {
            protectedCards.Add(commander);
        }

        return protectedCards;
    }
}
