namespace MtgMcp.Core;

/// <summary>
/// Contains shared replacement helpers and workspace loading internals.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Builds a plan operation that adds a recommended role card.
    /// </summary>
    private static DeckEditOperation CreateAddOperation(CardInfo card, string role, string rationale)
    {
        return DeckEditOperation.AddCard(card.Name, 1, role, rationale);
    }

    /// <summary>
    /// Returns focus-specific weights.
    /// </summary>
    private static ReplacementWeights WeightsForFocus(string focus)
    {
        return NormalizeFocus(focus) switch
        {
            "speed" => new ReplacementWeights { Role = 0.35, Power = 0.50, Price = 0.15 },
            "budget" => new ReplacementWeights { Role = 0.45, Power = 0.20, Price = 0.35 },
            "interaction" => new ReplacementWeights { Role = 0.55, Power = 0.30, Price = 0.15 },
            _ => new ReplacementWeights()
        };
    }

    /// <summary>
    /// Checks whether a card is a power-pressure card.
    /// </summary>
    private static bool ShouldReducePower(DeckCard card, string targetPower)
    {
        if (IsCommanderCard(card))
        {
            return false;
        }

        CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = $"{card.Name} {snapshot.TypeLine} {snapshot.OracleText}";
        bool casualTarget = NormalizeFocus(targetPower) is "casual" or "low" or "precon";
        return DeckAnalysisMetrics.IsFastMana(card)
            || role.Tags.Contains(DeckTags.Stax)
            || role.Tags.Contains(DeckTags.ComboPiece)
            || DeckAnalysisMetrics.ContainsAny(text, "extra turn", "destroy all lands")
            || (casualTarget && role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns consistency roles that need more density.
    /// </summary>
    private static IEnumerable<string> ConsistencyRolesToImprove(
        DeckConsistencyAnalysis consistency,
        string focus)
    {
        string normalizedFocus = NormalizeFocus(focus);
        if (normalizedFocus is "ramp" or "balanced" && consistency.RampCount < 10)
        {
            yield return DeckRoles.Ramp;
        }

        if (normalizedFocus is "draw" or "balanced" && consistency.DrawCount < 10)
        {
            yield return DeckRoles.Draw;
        }

        if (normalizedFocus is "tutors" or "speed" && consistency.TutorCount < 3)
        {
            yield return DeckRoles.Tutors;
        }

        if (normalizedFocus is "selection" or "balanced" && consistency.CardSelectionCount < 4)
        {
            yield return DeckTags.CardSelection;
        }
    }

    /// <summary>
    /// Normalizes a focus value.
    /// </summary>
    private static string NormalizeFocus(string? focus)
    {
        return string.IsNullOrWhiteSpace(focus)
            ? "balanced"
            : focus.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Creates a candidate deck card.
    /// </summary>
    private static DeckCard CreateCandidateCard(CardInfo candidate)
    {
        return DeckRecommendationCardFacts.CreateCandidateCard(candidate);
    }

    /// <summary>
    /// Checks whether a card is categorized as the commander.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckRecommendationCardFacts.IsCommanderCard(card);
    }

    /// <summary>
    /// Reads a cached USD price from a card snapshot.
    /// </summary>
    private static decimal? ReadUsdPrice(CardSnapshot? snapshot)
    {
        return DeckRecommendationCardFacts.ReadUsdPrice(snapshot);
    }

    /// <summary>
    /// Reads a USD price from catalog card details.
    /// </summary>
    private static decimal? ReadUsdPrice(CardInfo card)
    {
        return DeckRecommendationCardFacts.ReadUsdPrice(card);
    }

    /// <summary>
    /// Checks whether a card has enough signal for upgrade suggestions.
    /// </summary>
    private static bool ShouldConsiderUpgrade(DeckCard card)
    {
        if (IsCommanderCard(card))
        {
            return false;
        }

        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return !role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Count > 0
            || role.Confidence >= 0.65;
    }

    /// <summary>
    /// Scores how closely a candidate matches the card being replaced.
    /// </summary>
    private static double RoleScore(CardRoleAssignment currentRole, CardRoleAssignment candidateRole)
    {
        bool sharedTags = candidateRole.Tags.Intersect(currentRole.Tags, StringComparer.OrdinalIgnoreCase).Any();
        if (currentRole.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase)
            && !sharedTags)
        {
            return 0.2;
        }

        if (candidateRole.PrimaryRole.Equals(currentRole.PrimaryRole, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return sharedTags ? 0.7 : 0.2;
    }

    /// <summary>
    /// Normalizes replacement weights.
    /// </summary>
    private static ReplacementWeights NormalizeWeights(ReplacementWeights? weights)
    {
        double role = Math.Max(0, weights?.Role ?? 0.45);
        double power = Math.Max(0, weights?.Power ?? 0.30);
        double price = Math.Max(0, weights?.Price ?? 0.25);
        double total = role + power + price;
        if (total <= 0)
        {
            return new ReplacementWeights();
        }

        return new ReplacementWeights
        {
            Role = role / total,
            Power = power / total,
            Price = price / total
        };
    }

    /// <summary>
    /// Uses intent weights when the request did not override defaults.
    /// </summary>
    private static ReplacementWeights? EffectiveWeights(ReplacementWeights? weights, DeckIntent? intent)
    {
        if (intent?.Priorities is null || weights is null)
        {
            return weights ?? intent?.Priorities;
        }

        bool requestUsesDefaults =
            Math.Abs(weights.Role - 0.45) < 0.0001
            && Math.Abs(weights.Power - 0.30) < 0.0001
            && Math.Abs(weights.Price - 0.25) < 0.0001;
        return requestUsesDefaults ? intent.Priorities : weights;
    }

    /// <summary>
    /// Uses intent budget when the request did not override the default price.
    /// </summary>
    private static decimal EffectiveMaxPrice(decimal maxPrice, DeckIntent? intent)
    {
        return intent?.Budget.MaxCardPrice is { } intentPrice && maxPrice == 5
            ? intentPrice
            : maxPrice;
    }

    /// <summary>
    /// Checks whether a candidate conflicts with intent avoid guidance.
    /// </summary>
    private static bool IsAvoidedCandidate(CardInfo candidate, DeckIntent? intent)
    {
        if (intent is null)
        {
            return false;
        }

        string text = $"{candidate.Name} {candidate.TypeLine} {candidate.OracleText}";
        return intent.Avoid.Any(value =>
            !string.IsNullOrWhiteSpace(value)
            && text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates a candidate power score.
    /// </summary>
    private static double PowerScore(CardInfo candidate, DeckCard currentCard, string format)
    {
        double rankScore = candidate.EdhrecRank switch
        {
            null => 0.45,
            <= 500 => 1.0,
            <= 1_500 => 0.85,
            <= 5_000 => 0.65,
            <= 10_000 => 0.45,
            _ => 0.25
        };

        CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(currentCard);
        double manaDelta = (candidate.ManaValue ?? snapshot.ManaValue ?? 0) - (snapshot.ManaValue ?? 0);
        double efficiency = manaDelta <= 0 ? 1 : Math.Max(0.2, 1 - (manaDelta * 0.15));
        string legalityKey = NormalizeFormat(format);
        double legality = candidate.Legalities.TryGetValue(legalityKey, out string? formatLegality)
            ? formatLegality.Equals("legal", StringComparison.OrdinalIgnoreCase) ? 1 : 0
            : 0.7;

        return (rankScore * 0.55) + (efficiency * 0.25) + (legality * 0.20);
    }

    /// <summary>
    /// Calculates a candidate price score.
    /// </summary>
    private static double PriceScore(decimal? currentPrice, decimal? candidatePrice, bool budgetMode)
    {
        if (!candidatePrice.HasValue)
        {
            return budgetMode ? 0 : 0.5;
        }

        if (!currentPrice.HasValue || currentPrice.Value <= 0)
        {
            return candidatePrice.Value <= 1 ? 1 : candidatePrice.Value <= 5 ? 0.75 : 0.4;
        }

        decimal savings = currentPrice.Value - candidatePrice.Value;
        if (budgetMode)
        {
            return Math.Clamp((double)(savings / currentPrice.Value), 0, 1);
        }

        return candidatePrice.Value <= currentPrice.Value * 2 ? 0.8 : 0.45;
    }

    /// <summary>
    /// Checks whether the card is legal in a format.
    /// </summary>
    private static bool IsLegalInFormat(CardInfo card, string format)
    {
        return DeckRecommendationCardFacts.IsLegalInFormat(card, format);
    }

    /// <summary>
    /// Normalizes a format name.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        return DeckRecommendationCardFacts.NormalizeFormat(format);
    }

    /// <summary>
    /// Gets the deck color identity.
    /// </summary>
    private static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(DeckWorkspace workspace)
    {
        return DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
    }

    /// <summary>
    /// Checks whether a candidate fits the deck color identity.
    /// </summary>
    private static bool IsInDeckColorIdentity(CardInfo candidate, bool colorIdentityKnown, HashSet<string> deckColorIdentity)
    {
        return DeckRecommendationCardFacts.IsInDeckColorIdentity(candidate, colorIdentityKnown, deckColorIdentity);
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
