namespace MtgMcp.Core;

/// <summary>
/// Evaluates Scryfall query results against deterministic deck recommendation filters.
/// </summary>
internal static class DeckQueryRecommendationEngine
{
    /// <summary>
    /// Builds a deterministic rejection record for a query result, or null when the card is eligible.
    /// </summary>
    public static DeckQueryRejectedCandidate? BuildRejection(
        CardInfo card,
        CardRoleAssignment role,
        decimal? price,
        DeckQueryEvaluationContext context)
    {
        List<string> reasons = [];

        if (context.ExistingCards.Contains(card.Name))
        {
            reasons.Add("Already in deck.");
        }

        if (!IsLegalInFormat(card, context.Format))
        {
            reasons.Add($"Not legal in {context.Format}.");
        }

        if (!IsInDeckColorIdentity(card, context.ColorIdentityKnown, context.ColorIdentity))
        {
            string cardColors = string.Join("", card.ColorIdentity.Order(StringComparer.OrdinalIgnoreCase));
            string deckColors = context.ColorIdentity.Count == 0
                ? "colorless"
                : string.Join("", context.ColorIdentity.Order(StringComparer.OrdinalIgnoreCase));
            reasons.Add($"Color identity {cardColors} is outside deck color identity {deckColors}.");
        }

        if (!IsPriceWithinBudget(price, context.MaxPrice))
        {
            reasons.Add(price.HasValue
                ? $"Price {price.Value:0.##} exceeds max price {context.MaxPrice:0.##}."
                : $"No known USD price for max price {context.MaxPrice:0.##}.");
        }

        if (context.RequiredRoles.Count > 0
            && !context.RequiredRoles.Any(target => role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add($"Does not match required role(s): {string.Join(", ", context.RequiredRoles)}.");
        }

        if (context.RequiredTags.Count > 0
            && !context.RequiredTags.Any(target => role.Tags.Contains(target, StringComparer.OrdinalIgnoreCase)))
        {
            reasons.Add($"Does not match required tag(s): {string.Join(", ", context.RequiredTags)}.");
        }

        string? excludedRole = context.ExcludedRoles.FirstOrDefault(target =>
            role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (excludedRole is not null)
        {
            reasons.Add($"Excluded role matched: {excludedRole}.");
        }

        List<string> matchedExcludedTags = role.Tags
            .Where(tag => context.ExcludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matchedExcludedTags.Count > 0)
        {
            reasons.Add($"Excluded tag(s) matched: {string.Join(", ", matchedExcludedTags)}.");
        }

        if (IsAvoidedCandidate(card, context.Intent))
        {
            reasons.Add("Matches deck intent avoid guidance.");
        }

        return reasons.Count == 0
            ? null
            : new DeckQueryRejectedCandidate
            {
                CardName = card.Name,
                Role = role.PrimaryRole,
                Tags = role.Tags,
                Price = price,
                Reasons = reasons
            };
    }

    /// <summary>
    /// Builds an accepted query candidate with score components and fit reasons.
    /// </summary>
    public static DeckQueryCandidate BuildCandidate(
        CardInfo card,
        CardRoleAssignment role,
        decimal? price,
        IReadOnlyList<string> requiredRoles,
        IReadOnlyList<string> requiredTags,
        decimal? maxPrice,
        string goal)
    {
        double roleScore = ScoreRoleMatch(role, requiredRoles);
        double tagScore = ScoreTagMatch(role, requiredTags);
        double rankScore = ScoreEdhrecRank(card.EdhrecRank);
        double priceScore = ScoreQueryPrice(price, maxPrice);
        List<string> reasons = BuildCandidateReasons(role, requiredRoles, requiredTags, price, maxPrice);
        double score = Math.Clamp(
            (roleScore * 0.35) + (tagScore * 0.30) + (rankScore * 0.20) + (priceScore * 0.15),
            0,
            1);

        return new DeckQueryCandidate
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            Score = score,
            RoleScore = roleScore,
            TagScore = tagScore,
            RankScore = rankScore,
            PriceScore = priceScore,
            Price = price,
            Reasons = reasons,
            Rationale = string.IsNullOrWhiteSpace(goal)
                ? $"{card.Name} matched the supplied query and deck constraints."
                : $"{card.Name} fits '{goal}' through {string.Join(", ", role.Tags.Prepend(role.PrimaryRole).Distinct(StringComparer.OrdinalIgnoreCase))}."
        };
    }

    /// <summary>
    /// Adds query quality warnings to a result.
    /// </summary>
    public static void AddWarnings(
        DeckQueryRecommendationResult result,
        IReadOnlyList<string> queries,
        int acceptedCount,
        int requestedCount,
        int searchLimit)
    {
        if (queries.All(string.IsNullOrWhiteSpace))
        {
            result.Warnings.Add("The Scryfall query was empty; only automatic deck constraints were available.");
        }

        if (acceptedCount == 0)
        {
            result.Warnings.Add("No searched cards survived deck constraints and role/tag filters.");
        }
        else if (acceptedCount < requestedCount)
        {
            result.Warnings.Add($"Only {acceptedCount} card(s) survived the filters for {requestedCount} requested candidate(s).");
        }

        if (result.Rejected.Count >= searchLimit)
        {
            result.Warnings.Add("Many search hits were rejected; the query may be too broad for the requested filters.");
        }
    }

    /// <summary>
    /// Checks whether a known price is within the requested cap.
    /// </summary>
    private static bool IsPriceWithinBudget(decimal? price, decimal? maxPrice)
    {
        return !maxPrice.HasValue || (price.HasValue && price.Value <= maxPrice.Value);
    }

    /// <summary>
    /// Checks whether the card is legal in a format.
    /// </summary>
    private static bool IsLegalInFormat(CardInfo card, string format)
    {
        return !card.Legalities.TryGetValue(format, out string? legality)
            || legality.Equals("legal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a candidate fits the deck color identity.
    /// </summary>
    private static bool IsInDeckColorIdentity(CardInfo candidate, bool colorIdentityKnown, HashSet<string> deckColorIdentity)
    {
        return !colorIdentityKnown
            || candidate.ColorIdentity.All(color => deckColorIdentity.Contains(color));
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
    /// Scores a primary-role match.
    /// </summary>
    private static double ScoreRoleMatch(CardRoleAssignment role, IReadOnlyList<string> requiredRoles)
    {
        if (requiredRoles.Count == 0)
        {
            return Math.Clamp(role.Confidence, 0.35, 0.85);
        }

        return requiredRoles.Any(target => role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    }

    /// <summary>
    /// Scores secondary-tag matches.
    /// </summary>
    private static double ScoreTagMatch(CardRoleAssignment role, IReadOnlyList<string> requiredTags)
    {
        if (requiredTags.Count == 0)
        {
            return role.Tags.Count > 0 ? 0.65 : 0.45;
        }

        int matches = requiredTags.Count(target => role.Tags.Contains(target, StringComparer.OrdinalIgnoreCase));
        return Math.Clamp((double)matches / requiredTags.Count, 0, 1);
    }

    /// <summary>
    /// Scores EDHREC popularity when available.
    /// </summary>
    private static double ScoreEdhrecRank(int? edhrecRank)
    {
        return edhrecRank switch
        {
            null => 0.45,
            <= 1_000 => 0.95,
            <= 5_000 => 0.75,
            <= 10_000 => 0.55,
            _ => 0.35
        };
    }

    /// <summary>
    /// Scores query candidates against an optional budget cap.
    /// </summary>
    private static double ScoreQueryPrice(decimal? price, decimal? maxPrice)
    {
        if (!maxPrice.HasValue)
        {
            return price.HasValue ? price.Value <= 5 ? 0.85 : 0.60 : 0.45;
        }

        if (!price.HasValue || maxPrice.Value <= 0)
        {
            return 0;
        }

        double fraction = Math.Clamp((double)(price.Value / maxPrice.Value), 0, 1);
        return 1 - (fraction * 0.35);
    }

    /// <summary>
    /// Builds concise positive fit reasons for an accepted candidate.
    /// </summary>
    private static List<string> BuildCandidateReasons(
        CardRoleAssignment role,
        IReadOnlyList<string> requiredRoles,
        IReadOnlyList<string> requiredTags,
        decimal? price,
        decimal? maxPrice)
    {
        List<string> reasons = [];
        if (requiredRoles.Any(target => role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add($"Matched required role {role.PrimaryRole}.");
        }
        else if (requiredRoles.Count == 0)
        {
            reasons.Add($"Classified as {role.PrimaryRole}.");
        }

        List<string> matchedTags = role.Tags
            .Where(tag => requiredTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matchedTags.Count > 0)
        {
            reasons.Add($"Matched required tag(s): {string.Join(", ", matchedTags)}.");
        }
        else if (requiredTags.Count == 0 && role.Tags.Count > 0)
        {
            reasons.Add($"Relevant tag(s): {string.Join(", ", role.Tags)}.");
        }

        if (maxPrice.HasValue && price.HasValue)
        {
            reasons.Add($"Within max price {maxPrice.Value:0.##}.");
        }

        return reasons;
    }

}

/// <summary>
/// Carries deterministic constraints for one query-candidate evaluation.
/// </summary>
internal sealed class DeckQueryEvaluationContext
{
    /// <summary>
    /// Gets or initializes names already present in the workspace.
    /// </summary>
    public required HashSet<string> ExistingCards { get; init; }

    /// <summary>
    /// Gets or initializes the normalized format used for legality checks.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// Gets or initializes whether color identity filtering is active.
    /// </summary>
    public required bool ColorIdentityKnown { get; init; }

    /// <summary>
    /// Gets or initializes the deck color identity.
    /// </summary>
    public required HashSet<string> ColorIdentity { get; init; }

    /// <summary>
    /// Gets or initializes the maximum card price.
    /// </summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Gets or initializes required primary roles.
    /// </summary>
    public required IReadOnlyList<string> RequiredRoles { get; init; }

    /// <summary>
    /// Gets or initializes required secondary tags.
    /// </summary>
    public required IReadOnlyList<string> RequiredTags { get; init; }

    /// <summary>
    /// Gets or initializes excluded primary roles.
    /// </summary>
    public required IReadOnlyList<string> ExcludedRoles { get; init; }

    /// <summary>
    /// Gets or initializes excluded secondary tags.
    /// </summary>
    public required IReadOnlyList<string> ExcludedTags { get; init; }

    /// <summary>
    /// Gets or initializes optional deck intent.
    /// </summary>
    public DeckIntent? Intent { get; init; }
}
