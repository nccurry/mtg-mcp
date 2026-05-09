namespace MtgMcp.Core;

/// <summary>
/// Provides deck recommendation workspace behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Finds budget replacements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice,
        decimal minSavings,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ReplacementWeights normalizedWeights = NormalizeWeights(weights);
        List<ReplacementSuggestion> suggestions = [];

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => ReadUsdPrice(GetSnapshot(card)) >= maxPrice + minSavings)
            .OrderByDescending(card => ReadUsdPrice(GetSnapshot(card)) ?? 0)
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice,
                minSavings,
                budgetMode: true,
                normalizedWeights,
                cancellationToken).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
            }
        }

        DeckEditPlan plan = await SaveReplacementPlanAsync(
            workspace,
            "Budget replacement plan",
            "budget-replacements",
            suggestions,
            normalizedWeights,
            cancellationToken).ConfigureAwait(false);

        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds card upgrades.
    /// </summary>
    public async Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ReplacementWeights normalizedWeights = NormalizeWeights(weights);
        List<ReplacementSuggestion> suggestions = [];

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(ShouldConsiderUpgrade)
            .OrderBy(card => DeckRoleClassifier.Classify(card).Confidence)
            .ThenByDescending(card => GetSnapshot(card).EdhrecRank ?? int.MaxValue)
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice: null,
                minSavings: 0,
                budgetMode: false,
                normalizedWeights,
                cancellationToken).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
            }
        }

        DeckEditPlan plan = await SaveReplacementPlanAsync(
            workspace,
            "Card upgrade plan",
            "card-upgrades",
            suggestions,
            normalizedWeights,
            cancellationToken).ConfigureAwait(false);

        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds the best replacement for a card.
    /// </summary>
    private async Task<ReplacementSuggestion?> FindReplacementAsync(
        DeckWorkspace workspace,
        DeckCard currentCard,
        decimal? maxPrice,
        decimal minSavings,
        bool budgetMode,
        ReplacementWeights weights,
        CancellationToken cancellationToken)
    {
        CardRoleAssignment currentRole = DeckRoleClassifier.Classify(currentCard);
        if (currentRole.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string query = DeckRoleClassifier.QueryForRole(currentRole.PrimaryRole, workspace.Format, maxPrice);
        if (string.IsNullOrWhiteSpace(query))
        {
            query = string.IsNullOrWhiteSpace(workspace.Format) ? currentRole.PrimaryRole : $"legal:{workspace.Format}";
        }

        IReadOnlyList<CardSearchResult> searchResults = await cardCatalog
            .SearchCardsAsync(query, limit: 12, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> candidatesByName = await cardCatalog
            .GetCardsByNamesAsync(searchResults.Select(result => result.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> existingNames = workspace.Cards
            .Select(card => card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        (bool colorIdentityKnown, HashSet<string> deckColorIdentity) = GetDeckColorIdentity(workspace);

        ReplacementSuggestion? bestSuggestion = null;
        foreach (CardInfo candidate in candidatesByName.Values)
        {
            if (existingNames.Contains(candidate.Name)
                || candidate.Name.Equals(currentCard.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsLegalInFormat(candidate, workspace.Format)
                || !IsInDeckColorIdentity(candidate, colorIdentityKnown, deckColorIdentity))
            {
                continue;
            }

            ReplacementSuggestion? suggestion = ScoreReplacement(currentCard, candidate, currentRole, weights, budgetMode, workspace.Format);
            if (suggestion is null)
            {
                continue;
            }

            if (budgetMode
                && suggestion.EstimatedSavings.GetValueOrDefault() < minSavings)
            {
                continue;
            }

            if (bestSuggestion is null || suggestion.Score > bestSuggestion.Score)
            {
                bestSuggestion = suggestion;
            }
        }

        return bestSuggestion;
    }

    /// <summary>
    /// Scores a replacement candidate.
    /// </summary>
    private static ReplacementSuggestion? ScoreReplacement(
        DeckCard currentCard,
        CardInfo candidate,
        CardRoleAssignment currentRole,
        ReplacementWeights weights,
        bool budgetMode,
        string format)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
        decimal? currentPrice = ReadUsdPrice(GetSnapshot(currentCard));
        decimal? candidatePrice = ReadUsdPrice(candidate);
        decimal? estimatedSavings = currentPrice.HasValue && candidatePrice.HasValue
            ? currentPrice.Value - candidatePrice.Value
            : null;

        double roleScore = RoleScore(currentRole, candidateRole);
        if (roleScore < 0.65)
        {
            return null;
        }

        double powerScore = PowerScore(candidate, currentCard, format);
        double priceScore = PriceScore(currentPrice, candidatePrice, budgetMode);
        double score = (roleScore * weights.Role) + (powerScore * weights.Power) + (priceScore * weights.Price);

        if (budgetMode && (!candidatePrice.HasValue || candidatePrice.Value <= 0))
        {
            return null;
        }

        return new ReplacementSuggestion
        {
            ReplaceCard = currentCard.Name,
            WithCard = candidate.Name,
            Role = currentRole.PrimaryRole,
            Score = score,
            RoleScore = roleScore,
            PowerScore = powerScore,
            PriceScore = priceScore,
            CurrentPrice = currentPrice,
            CandidatePrice = candidatePrice,
            EstimatedSavings = estimatedSavings,
            Rationale = $"{candidate.Name} fits {currentRole.PrimaryRole} at score {score:0.00}."
        };
    }

    /// <summary>
    /// Saves a replacement plan.
    /// </summary>
    private async Task<DeckEditPlan> SaveReplacementPlanAsync(
        DeckWorkspace workspace,
        string name,
        string kind,
        IReadOnlyList<ReplacementSuggestion> suggestions,
        ReplacementWeights weights,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = CreatePlan(workspace, name, kind);
        plan.Rationale = $"Weighted replacement plan using role={weights.Role:0.##}, power={weights.Power:0.##}, price={weights.Price:0.##}.";
        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Score);
        if (suggestions.Count == 0)
        {
            plan.Warnings.Add("No replacements met the current filters.");
        }

        foreach (ReplacementSuggestion suggestion in suggestions)
        {
            DeckCard? currentCard = workspace.Cards.FirstOrDefault(card =>
                card.Name.Equals(suggestion.ReplaceCard, StringComparison.OrdinalIgnoreCase));
            int quantity = currentCard?.Quantity ?? 1;
            string category = currentCard?.PrimaryCategory ?? DeckDefaults.Mainboard;

            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = suggestion.ReplaceCard,
                Quantity = quantity,
                Category = category,
                Rationale = suggestion.Rationale
            });
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.AddCard,
                CardName = suggestion.WithCard,
                Quantity = quantity,
                Category = category,
                Rationale = suggestion.Rationale
            });
        }

        return await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a candidate deck card.
    /// </summary>
    private static DeckCard CreateCandidateCard(CardInfo candidate)
    {
        DeckCard card = new()
        {
            Name = candidate.Name,
            Quantity = 1,
            PrimaryCategory = DeckDefaults.Mainboard,
            Categories = [DeckDefaults.Mainboard],
            ScryfallId = candidate.Id,
            ScryfallOracleId = candidate.OracleId
        };
        ApplyCardSnapshot(card, candidate);
        return card;
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

        CardSnapshot snapshot = GetSnapshot(currentCard);
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
    /// Reads a USD price from a snapshot.
    /// </summary>
    private static decimal? ReadUsdPrice(CardSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return TryReadDecimal(snapshot.Prices, "usd")
            ?? TryReadDecimal(snapshot.Prices, "usd_etched")
            ?? TryReadDecimal(snapshot.Prices, "usd_foil");
    }

    /// <summary>
    /// Reads a USD price from card info.
    /// </summary>
    private static decimal? ReadUsdPrice(CardInfo card)
    {
        return TryReadDecimal(card.Prices, "usd")
            ?? TryReadDecimal(card.Prices, "usd_etched")
            ?? TryReadDecimal(card.Prices, "usd_foil");
    }

    /// <summary>
    /// Reads a decimal dictionary value.
    /// </summary>
    private static decimal? TryReadDecimal(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            && decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal result)
                ? result
                : null;
    }

    /// <summary>
    /// Checks whether the card is legal in a format.
    /// </summary>
    private static bool IsLegalInFormat(CardInfo card, string format)
    {
        string legalityKey = NormalizeFormat(format);
        return !card.Legalities.TryGetValue(legalityKey, out string? legality)
            || legality.Equals("legal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a format name.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "edh" => "commander",
            _ => normalized
        };
    }

    /// <summary>
    /// Gets the deck color identity.
    /// </summary>
    private static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(DeckWorkspace workspace)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        bool foundCommander = false;

        foreach (DeckCard card in workspace.Cards)
        {
            if (!IsCommanderCard(card))
            {
                continue;
            }

            foundCommander = true;
            AddColors(colors, GetSnapshot(card).ColorIdentity);
        }

        if (foundCommander)
        {
            return (true, colors);
        }

        foreach (DeckCard card in IncludedCards(workspace))
        {
            AddColors(colors, GetSnapshot(card).ColorIdentity);
        }

        return (colors.Count > 0, colors);
    }

    /// <summary>
    /// Checks whether the card is a commander.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return string.Equals(card.PrimaryCategory, DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
            || (card.Categories ?? []).Any(category => category.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase));
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
    /// Adds colors to a color set.
    /// </summary>
    private static void AddColors(HashSet<string> colors, IEnumerable<string> colorIdentity)
    {
        foreach (string color in colorIdentity)
        {
            if (!string.IsNullOrWhiteSpace(color))
            {
                colors.Add(color);
            }
        }
    }
}
