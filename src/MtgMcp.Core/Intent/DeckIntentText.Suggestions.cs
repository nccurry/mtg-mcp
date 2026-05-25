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
            Version = 2,
            Format = workspace.Format,
            Commander = FindCommander(workspace),
            Goal = "Refine the current commander plan with clear card roles, reliable mana, and explainable win routes",
            Archetype = SuggestArchetype(workspace),
            PowerTarget = "tuned casual to high power, based on user preference",
            PowerLevel = "tuned-casual",
            HeuristicProfile = "auto",
            SimulationProfile = SimulationProfileIds.Auto,
            ArchetypeTags = SuggestArchetypeTags(workspace).ToList(),
            Budget = new DeckIntentBudget
            {
                Text = "prefer cheaper swaps unless a card is core",
                PreferCheaperSwaps = true
            }
        };

        intent.BuildTargets[DeckRoles.Lands] = Target("36-37", 36, 37);
        intent.BuildTargets[DeckRoles.Ramp] = Target("8-10", 8, 10);
        intent.BuildTargets[DeckRoles.Draw] = Target("9-11", 9, 11);
        intent.BuildTargets[DeckRoles.Interaction] = Target("10-14", 10, 14);
        intent.BuildTargets[DeckRoles.BoardWipes] = Target("2-4", 2, 4);
        foreach (KeyValuePair<string, DeckIntentTarget> target in intent.BuildTargets)
        {
            intent.Targets[target.Key] = target.Value;
        }

        intent.Simulation = new DeckIntentSimulationSettings
        {
            MulliganStyle = "multiplayer-london",
            HoldInteractionFromTurn = 3,
            MinimumInteractionHeld = 1,
            PreferCommanderOnCurve = true,
            AcceptShieldDownWinAttempt = false,
            Values =
            {
                ["Mulligan Style"] = "multiplayer-london",
                ["Hold Interaction From Turn"] = "3",
                ["Minimum Interaction Held"] = "1",
                ["Prefer Commander On Curve"] = "true",
                ["Accept Shield Down Win Attempt"] = "false",
            }
        };

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
                DeckCategoryOrdering.PrimaryCategory(card).Equals(
                    DeckRoles.Commander,
                    StringComparison.OrdinalIgnoreCase))
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
    /// Suggests broad archetype tags from current roles and categories.
    /// </summary>
    private static IEnumerable<string> SuggestArchetypeTags(DeckWorkspace workspace)
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        string text = string.Join(' ', workspace.Categories.Select(category => category.Name));
        foreach (DeckCard card in workspace.Cards)
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            if (role.Tags.Contains(DeckTags.Blink, StringComparer.OrdinalIgnoreCase) || ContainsAny(text, "blink"))
            {
                tags.Add("blink");
            }

            if (role.Tags.Contains(DeckTags.Tokens, StringComparer.OrdinalIgnoreCase) || ContainsAny(text, "tokens"))
            {
                tags.Add("tokens");
            }

            if (role.Tags.Contains(DeckTags.Reanimation, StringComparer.OrdinalIgnoreCase) || ContainsAny(text, "reanimator", "graveyard"))
            {
                tags.Add("graveyard");
            }

            if (role.Tags.Contains(DeckTags.Aristocrats, StringComparer.OrdinalIgnoreCase) || ContainsAny(text, "aristocrats", "sacrifice"))
            {
                tags.Add("aristocrats");
            }
        }

        return tags.Count == 0 ? ["value"] : tags;
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
