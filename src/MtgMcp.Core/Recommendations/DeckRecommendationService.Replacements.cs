namespace MtgMcp.Core;

/// <summary>
/// Provides deck recommendation workspace behavior.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
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
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        decimal effectiveMaxPrice = EffectiveMaxPrice(maxPrice, intent);
        ReplacementWeights normalizedWeights = NormalizeWeights(EffectiveWeights(weights, intent));
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !IsProtectedCard(card, intent))
            .Where(card => ReadUsdPrice(GetSnapshot(card)) >= effectiveMaxPrice + minSavings)
            .OrderByDescending(card => ReadUsdPrice(GetSnapshot(card)) ?? 0)
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                effectiveMaxPrice,
                minSavings,
                budgetMode: true,
                normalizedWeights,
                intent,
                cancellationToken,
                excludedCandidateNames: selectedReplacementNames).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
                selectedReplacementNames.Add(suggestion.WithCard);
            }
        }

        DeckEditPlan plan = await SaveReplacementPlanAsync(
            workspace,
            "Budget replacement plan",
            "budget-replacements",
            suggestions,
            normalizedWeights,
            intent,
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
        return await FindCardUpgradesAsync(
            workspaceId,
            focus: "balanced",
            maxPrice: null,
            limit,
            weights,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds card upgrades with optional focus and price constraints.
    /// </summary>
    public async Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
        string focus,
        decimal? maxPrice,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        ReplacementWeights normalizedWeights = NormalizeWeights(EffectiveWeights(weights ?? WeightsForFocus(focus), intent));
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(ShouldConsiderUpgrade)
            .Where(card => !IsProtectedCard(card, intent))
            .OrderBy(card => DeckRoleClassifier.Classify(card).Confidence)
            .ThenByDescending(card => GetSnapshot(card).EdhrecRank ?? int.MaxValue)
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice,
                minSavings: 0,
                budgetMode: false,
                normalizedWeights,
                intent,
                cancellationToken,
                excludedCandidateNames: selectedReplacementNames).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
                selectedReplacementNames.Add(suggestion.WithCard);
            }
        }

        DeckEditPlan plan = await SaveReplacementPlanAsync(
            workspace,
            "Card upgrade plan",
            "card-upgrades",
            suggestions,
            normalizedWeights,
            intent,
            cancellationToken).ConfigureAwait(false);

        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds bracket reduction candidates.
    /// </summary>
    public async Task<RecommendationPlanResult> FindBracketReductionCandidatesAsync(
        string workspaceId,
        int targetBracket,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        CommanderBracketEstimate estimate = EstimateCommanderBracket(workspace, gameChangers);
        ReplacementWeights weights = NormalizeWeights(new ReplacementWeights { Role = 0.55, Power = 0.15, Price = 0.30 });
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);
        DeckEditPlan plan = CreatePlan(workspace, "Commander bracket reduction plan", "bracket-reduction");
        plan.Rationale = $"Targets bracket {Math.Clamp(targetBracket, 1, 4)} by reducing Game Changer, fast mana, tutor, stax, combo, and extra-turn pressure.";

        foreach (string cardName in estimate.Signals
            .Where(signal => signal.SuggestedBracket > targetBracket && !string.IsNullOrWhiteSpace(signal.CardName))
            .OrderByDescending(signal => signal.Severity)
            .Select(signal => signal.CardName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 25)))
        {
            DeckCard? card = workspace.Cards.FirstOrDefault(value => value.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase));
            if (card is null || IsCommanderCard(card))
            {
                continue;
            }

            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice: 5,
                minSavings: 0,
                budgetMode: true,
                weights,
                intent: null,
                cancellationToken: cancellationToken,
                excludedCandidateNames: selectedReplacementNames).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
                selectedReplacementNames.Add(suggestion.WithCard);
                continue;
            }

            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = card.Name,
                Quantity = card.Quantity,
                Category = DeckCategoryOrdering.PrimaryCategory(card),
                Rationale = $"Remove {card.Name} to reduce bracket pressure."
            });
        }

        AddReplacementOperations(plan, workspace, suggestions);
        await SavePlanWithWarningsAsync(plan, suggestions.Count + plan.Operations.Count, cancellationToken).ConfigureAwait(false);
        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds power reduction candidates.
    /// </summary>
    public async Task<RecommendationPlanResult> FindPowerReductionCandidatesAsync(
        string workspaceId,
        string targetPower,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ReplacementWeights weights = NormalizeWeights(new ReplacementWeights { Role = 0.55, Power = 0.10, Price = 0.35 });
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(card => ShouldReducePower(card, targetPower))
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice: 5,
                minSavings: 0,
                budgetMode: true,
                weights,
                intent: null,
                cancellationToken: cancellationToken,
                excludedCandidateNames: selectedReplacementNames).ConfigureAwait(false);

            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
                selectedReplacementNames.Add(suggestion.WithCard);
            }
        }

        DeckEditPlan plan = await SaveReplacementPlanAsync(
            workspace,
            "Power reduction plan",
            "power-reduction",
            suggestions,
            weights,
            intent: null,
            cancellationToken).ConfigureAwait(false);
        plan.Rationale = $"Softens the deck toward {NormalizeFocus(targetPower)} tables by replacing fast mana, tutors, stax, combo, and extra-turn pressure.";
        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds mana base improvements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindManaBaseImprovementsAsync(
        string workspaceId,
        decimal maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ReplacementWeights weights = NormalizeWeights(new ReplacementWeights { Role = 0.70, Power = 0.10, Price = 0.20 });
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);
        DeckEditPlan plan = CreatePlan(workspace, "Mana base improvement plan", "mana-base-improvements");
        plan.Rationale = "Improves land count, fixing, and tapped-land pressure while preserving color identity.";

        foreach (DeckCard card in IncludedCards(workspace)
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            .Where(card => LooksTapped(GetSnapshot(card)))
            .Take(Math.Clamp(limit, 1, 25)))
        {
            ReplacementSuggestion? suggestion = await FindReplacementAsync(
                workspace,
                card,
                maxPrice,
                minSavings: 0,
                budgetMode: false,
                weights,
                intent: null,
                cancellationToken: cancellationToken,
                excludedCandidateNames: selectedReplacementNames,
                candidateFilter: candidate => IsManaBaseImprovement(card, candidate)).ConfigureAwait(false);
            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
                selectedReplacementNames.Add(suggestion.WithCard);
            }
        }

        ManaBaseAnalysis manaBase = AnalyzeManaBase(workspace);
        if (manaBase.LandCount < 36 && plan.Operations.Count + (suggestions.Count * 2) < Math.Clamp(limit, 1, 25))
        {
            HashSet<string> replacementNames = suggestions
                .Select(suggestion => suggestion.WithCard)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            CardInfo? land = await FindAddCandidateAsync(
                    workspace,
                    DeckRoles.Lands,
                    maxPrice,
                    cancellationToken,
                    replacementNames,
                    candidateFilter: IsUntappedLandCandidate)
                .ConfigureAwait(false);
            if (land is not null)
            {
                plan.Operations.Add(CreateAddOperation(land, DeckRoles.Lands, "Add an additional land to improve mana consistency."));
            }
        }

        AddReplacementOperations(plan, workspace, suggestions);
        await SavePlanWithWarningsAsync(plan, suggestions.Count + plan.Operations.Count, cancellationToken).ConfigureAwait(false);
        return new RecommendationPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Finds consistency improvements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindConsistencyImprovementsAsync(
        string workspaceId,
        string focus,
        decimal maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckConsistencyAnalysis consistency = AnalyzeDeckConsistency(workspace);
        DeckEditPlan plan = CreatePlan(workspace, "Consistency improvement plan", "consistency-improvements");
        plan.Rationale = $"Improves {NormalizeFocus(focus)} consistency by filling ramp, draw, tutor, or card-selection gaps.";
        HashSet<string> selectedAddNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (string role in ConsistencyRolesToImprove(consistency, focus).Take(Math.Clamp(limit, 1, 25)))
        {
            CardInfo? candidate = await FindAddCandidateAsync(
                    workspace,
                    role,
                    maxPrice,
                    cancellationToken,
                    selectedAddNames)
                .ConfigureAwait(false);
            if (candidate is null)
            {
                continue;
            }

            plan.Operations.Add(CreateAddOperation(candidate, role, $"Add {candidate.Name} to improve {role} density."));
            selectedAddNames.Add(candidate.Name);
        }

        await SavePlanWithWarningsAsync(plan, plan.Operations.Count, cancellationToken).ConfigureAwait(false);
        return new RecommendationPlanResult { Plan = plan, Suggestions = [] };
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
        DeckIntent? intent,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedCandidateNames = null,
        Func<CardInfo, bool>? candidateFilter = null)
    {
        CardRoleAssignment currentRole = DeckRoleClassifier.Classify(currentCard);
        if (currentRole.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        CardSearchRequest searchRequest = DeckRoleClassifier.SearchRequestForRole(
            currentRole.PrimaryRole,
            workspace.Format,
            maxPrice);
        IReadOnlyList<CardSearchResult> searchResults = await CardCatalog
            .SearchCardsAsync(searchRequest, limit: 12, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> candidatesByName = await CardCatalog
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
                || (excludedCandidateNames is not null && excludedCandidateNames.Contains(candidate.Name))
                || candidate.Name.Equals(currentCard.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsLegalInFormat(candidate, workspace.Format)
                || !IsInDeckColorIdentity(candidate, colorIdentityKnown, deckColorIdentity)
                || IsAvoidedCandidate(candidate, intent))
            {
                continue;
            }

            if (candidateFilter is not null && !candidateFilter(candidate))
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
        DeckIntent? intent,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = CreatePlan(workspace, name, kind);
        plan.Rationale = $"Weighted replacement plan using role={weights.Role:0.##}, power={weights.Power:0.##}, price={weights.Price:0.##}.";
        if (intent is not null)
        {
            plan.Warnings.Add("This plan used the deck intent stored in the workspace description.");
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Score);
        if (suggestions.Count == 0)
        {
            plan.Warnings.Add("No replacements met the current filters.");
        }

        AddReplacementOperations(plan, workspace, suggestions);

        return await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the plan and adds a no-op warning when empty.
    /// </summary>
    private async Task SavePlanWithWarningsAsync(
        DeckEditPlan plan,
        int candidateCount,
        CancellationToken cancellationToken)
    {
        if (candidateCount == 0 || plan.Operations.Count == 0)
        {
            plan.Warnings.Add("No candidates met the current filters.");
        }

        plan.Confidence = plan.Operations.Count == 0 ? 0 : 0.65;
        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds replacement operations to a plan.
    /// </summary>
    private static void AddReplacementOperations(
        DeckEditPlan plan,
        DeckWorkspace workspace,
        IReadOnlyList<ReplacementSuggestion> suggestions)
    {
        foreach (ReplacementSuggestion suggestion in suggestions)
        {
            DeckCard? currentCard = workspace.Cards.FirstOrDefault(card =>
                card.Name.Equals(suggestion.ReplaceCard, StringComparison.OrdinalIgnoreCase));
            int quantity = currentCard?.Quantity ?? 1;
            string category = currentCard is null
                ? DeckDefaults.Mainboard
                : DeckCategoryOrdering.PrimaryCategory(currentCard);

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
    }

    /// <summary>
    /// Finds a card to add for a role.
    /// </summary>
    private async Task<CardInfo?> FindAddCandidateAsync(
        DeckWorkspace workspace,
        string role,
        decimal maxPrice,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedNames = null,
        Func<CardInfo, bool>? candidateFilter = null)
    {
        CardSearchRequest searchRequest = DeckRoleClassifier.SearchRequestForRole(role, workspace.Format, maxPrice);
        IReadOnlyList<CardSearchResult> results = await CardCatalog
            .SearchCardsAsync(searchRequest, limit: 12, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cardsByName = await CardCatalog
            .GetCardsByNamesAsync(results.Select(result => result.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        HashSet<string> existingNames = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        (bool colorIdentityKnown, HashSet<string> deckColorIdentity) = GetDeckColorIdentity(workspace);

        return cardsByName.Values
            .Where(card => !existingNames.Contains(card.Name))
            .Where(card => IsLegalInFormat(card, workspace.Format))
            .Where(card => IsInDeckColorIdentity(card, colorIdentityKnown, deckColorIdentity))
            .Where(card => excludedNames is null || !excludedNames.Contains(card.Name))
            .Where(card => !ReadUsdPrice(card).HasValue || ReadUsdPrice(card) <= maxPrice)
            .Where(card => CandidateMatchesRole(card, role))
            .Where(card => candidateFilter is null || candidateFilter(card))
            .OrderBy(card => card.EdhrecRank ?? int.MaxValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks whether an add candidate actually fills the requested role or tag.
    /// </summary>
    private static bool CandidateMatchesRole(CardInfo card, string role)
    {
        return DeckRoleClassifier.MatchesTarget(CreateCandidateCard(card), role);
    }

    /// <summary>
    /// Checks whether a land replacement improves tapped-land pressure or fixing.
    /// </summary>
    private static bool IsManaBaseImprovement(DeckCard currentCard, CardInfo candidate)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
        if (!candidateRole.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CardSnapshot currentSnapshot = GetSnapshot(currentCard);
        CardSnapshot candidateSnapshot = GetSnapshot(candidateCard);
        if (LooksTapped(candidateSnapshot))
        {
            return false;
        }

        int currentColors = ReadProducedMana(currentCard).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int candidateColors = ReadProducedMana(candidateCard).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        bool preservesSources = candidateColors >= currentColors || currentColors == 0;
        return LooksTapped(currentSnapshot)
            && (preservesSources || candidateRole.Tags.Contains(DeckTags.ManaFixing));
    }

    /// <summary>
    /// Checks whether a land add candidate avoids tapped-land pressure.
    /// </summary>
    private static bool IsUntappedLandCandidate(CardInfo candidate)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        return DeckRoleClassifier.Classify(candidateCard).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && !LooksTapped(GetSnapshot(candidateCard));
    }

    /// <summary>
    /// Builds a plan operation that adds a recommended role card.
    /// </summary>
    private static DeckEditOperation CreateAddOperation(CardInfo card, string role, string rationale)
    {
        return new DeckEditOperation
        {
            Operation = DeckEditOperations.AddCard,
            CardName = card.Name,
            Quantity = 1,
            Category = role,
            Rationale = rationale
        };
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

        CardSnapshot snapshot = GetSnapshot(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = $"{card.Name} {snapshot.TypeLine} {snapshot.OracleText}";
        bool casualTarget = NormalizeFocus(targetPower) is "casual" or "low" or "precon";
        return IsFastMana(card)
            || role.Tags.Contains(DeckTags.Stax)
            || role.Tags.Contains(DeckTags.ComboPiece)
            || ContainsAny(text, "extra turn", "destroy all lands")
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
    /// Checks whether an existing card is protected by intent.
    /// </summary>
    private static bool IsProtectedCard(DeckCard card, DeckIntent? intent)
    {
        if (intent is null)
        {
            return false;
        }

        return intent.Protect.Any(value =>
            value.Equals("commander", StringComparison.OrdinalIgnoreCase) && IsCommanderCard(card)
            || card.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
            || card.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
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
