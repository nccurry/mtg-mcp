namespace MtgMcp.Core;

/// <summary>
/// Shares role, count, snapshot, and plan helper methods across deck services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Refreshes cached card snapshots for cards matching a normalized scope.
    /// </summary>
    protected async Task<DeckNormalizationResult> NormalizeWorkspaceCardsAsync(
        DeckWorkspace workspace,
        string normalizedScope,
        CancellationToken cancellationToken)
    {
        List<DeckCard> targetCards = workspace.Cards
            .Where(card => ShouldNormalize(card, workspace, normalizedScope))
            .ToList();

        IReadOnlyDictionary<string, CardInfo> cardsByName = await CardCatalog
            .GetCardsByNamesAsync(targetCards.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        List<string> missingCards = [];
        int updatedCards = 0;
        foreach (DeckCard card in targetCards)
        {
            if (!cardsByName.TryGetValue(card.Name, out CardInfo? cardInfo))
            {
                missingCards.Add(card.Name);
                continue;
            }

            card.ScryfallId = cardInfo.Id;
            card.ScryfallOracleId = cardInfo.OracleId;
            ApplyCardSnapshot(card, cardInfo);
            updatedCards++;
        }

        return new DeckNormalizationResult
        {
            WorkspaceId = workspace.Id,
            Scope = normalizedScope,
            RequestedCards = targetCards.Count,
            UpdatedCards = updatedCards,
            MissingCards = missingCards,
            Workspace = workspace
        };
    }

    /// <summary>
    /// Determines whether a card should be normalized.
    /// </summary>
    protected static bool ShouldNormalize(DeckCard card, DeckWorkspace workspace, string scope)
    {
        return scope switch
        {
            "all" => true,
            "included" => IsIncluded(workspace, card),
            "maybeboard" => string.Equals(DeckCategoryOrdering.PrimaryCategory(card), DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase),
            "missing" => string.IsNullOrWhiteSpace(GetSnapshot(card).TypeLine)
                || string.IsNullOrWhiteSpace(GetSnapshot(card).OracleText)
                || GetSnapshot(card).Prices.Count == 0,
            _ => true
        };
    }

    /// <summary>
    /// Enumerates included workspace cards.
    /// </summary>
    protected static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Determines whether a card is included in the deck.
    /// </summary>
    protected static bool IsIncluded(DeckWorkspace workspace, DeckCard card)
    {
        return DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
    }

    /// <summary>
    /// Parses draw odds targets.
    /// </summary>
    protected static List<string> ParseTargets(string? targets, DeckIntent? intent)
    {
        if (string.IsNullOrWhiteSpace(targets))
        {
            if (intent?.Targets.Count > 0)
            {
                return intent.Targets.Keys
                    .Where(target => DeckRoles.Primary.Contains(target, StringComparer.OrdinalIgnoreCase)
                        || DeckTags.Secondary.Contains(target, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return
            [
                DeckRoles.Lands,
                DeckRoles.Ramp,
                DeckRoles.Draw,
                DeckRoles.Interaction,
                DeckRoles.BoardWipes,
                DeckTags.Discard
            ];
        }

        return targets
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds summary notes.
    /// </summary>
    protected static void AddSummaryNotes(DeckPlanSummary summary, DeckIntent? intent)
    {
        int lands = Count(summary.RoleCounts, DeckRoles.Lands);
        int ramp = Count(summary.RoleCounts, DeckRoles.Ramp);
        int draw = Count(summary.RoleCounts, DeckRoles.Draw);
        int interaction = Count(summary.RoleCounts, DeckRoles.Interaction) + Count(summary.RoleCounts, DeckRoles.BoardWipes);
        int landTarget = TargetMinimum(intent, DeckRoles.Lands, 35);
        int rampTarget = TargetMinimum(intent, DeckRoles.Ramp, 8);
        int drawTarget = TargetMinimum(intent, DeckRoles.Draw, 8);
        int interactionTarget = TargetMinimum(intent, DeckRoles.Interaction, 8);

        if (intent is not null)
        {
            summary.IntentNotes.Add("Summary thresholds are using the deck intent stored in the description.");
            if (!string.IsNullOrWhiteSpace(intent.Archetype))
            {
                summary.IntentNotes.Add($"Intent archetype: {intent.Archetype}.");
            }
        }

        if (lands >= landTarget)
        {
            summary.Strengths.Add("Land count looks healthy for Commander.");
        }
        else
        {
            summary.Risks.Add("Land count may be low for a Commander deck.");
        }

        if (ramp >= rampTarget)
        {
            summary.Strengths.Add("Ramp density is in a strong range.");
        }
        else
        {
            summary.Risks.Add("Ramp count may be light.");
        }

        if (draw >= drawTarget)
        {
            summary.Strengths.Add("Card draw appears well represented.");
        }
        else
        {
            summary.Risks.Add("Card draw may need reinforcement.");
        }

        if (interaction < interactionTarget)
        {
            summary.Risks.Add("Interaction and board wipe density may be low.");
        }

        summary.NextSteps.Add("Run deck_analyze_draw_odds for lands, ramp, draw, discard, interaction, and board wipes.");
        summary.NextSteps.Add("Review category counts and card facets before applying category changes.");
    }

    /// <summary>
    /// Reads the minimum target for a role.
    /// </summary>
    protected static int TargetMinimum(DeckIntent? intent, string role, int fallback)
    {
        return intent?.Targets.TryGetValue(role, out DeckIntentTarget? target) == true
            ? target.Minimum ?? fallback
            : fallback;
    }

    /// <summary>
    /// Suggests a role for a category.
    /// </summary>
    protected static string SuggestRoleForCategory(DeckWorkspace workspace, string category)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards.Where(card => (card.Categories ?? []).Any(value => value.Equals(category, StringComparison.OrdinalIgnoreCase))))
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            AddCount(counts, assignment.PrimaryRole, card.Quantity);
        }

        return counts.OrderByDescending(pair => pair.Value).FirstOrDefault().Key ?? DeckRoles.Utility;
    }

    /// <summary>
    /// Creates a deck edit plan.
    /// </summary>
    protected static DeckEditPlan CreatePlan(DeckWorkspace workspace, string name, string kind)
    {
        return new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = name,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace)
        };
    }

    /// <summary>
    /// Gets a card snapshot safely.
    /// </summary>
    protected static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Adds a quantity to a count dictionary.
    /// </summary>
    protected static void AddCount(Dictionary<string, int> counts, string key, int quantity)
    {
        counts[key] = counts.GetValueOrDefault(key) + Math.Max(0, quantity);
    }

    /// <summary>
    /// Gets a count value.
    /// </summary>
    protected static int Count(Dictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out int count) ? count : 0;
    }

    /// <summary>
    /// Requires an operation value.
    /// </summary>
    protected static string Require(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Deck edit operation is missing required field '{name}'.");
    }

    /// <summary>
    /// Requires the plan Repository.
    /// </summary>
    protected IDeckPlanRepository RequirePlanRepository()
    {
        return PlanRepository ?? throw new InvalidOperationException("Deck edit plan persistence is not configured.");
    }
}
