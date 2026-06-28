namespace MtgMcp.Core;

/// <summary>
/// Provides replacement, upgrade, and consistency improvement planning.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Loads local workspaces for replacement planning.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Resolves search candidates and card metadata.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Persists generated replacement plans when plan tools are enabled.
    /// </summary>
    private readonly IDeckPlanRepository? planRepository;

    /// <summary>
    /// Supplies reusable analysis metrics used by reduction and consistency plans.
    /// </summary>
    private readonly DeckAnalysisMetrics analysisMetrics;

    /// <summary>
    /// Creates a replacement collaborator with explicit storage, catalog, and analysis dependencies.
    /// </summary>
    public DeckReplacementService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        DeckAnalysisMetrics analysisMetrics,
        IDeckPlanRepository? planRepository = null)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.planRepository = planRepository;
        this.analysisMetrics = analysisMetrics;
    }

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

        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !DeckIntentProtection.IsProtectedCard(card, intent))
            .Where(card => ReadUsdPrice(DeckServiceHelpers.GetSnapshot(card)) >= effectiveMaxPrice + minSavings)
            .OrderByDescending(card => ReadUsdPrice(DeckServiceHelpers.GetSnapshot(card)) ?? 0)
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

        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace)
            .Where(ShouldConsiderUpgrade)
            .Where(card => !DeckIntentProtection.IsProtectedCard(card, intent))
            .OrderBy(card => DeckRoleClassifier.Classify(card).Confidence)
            .ThenByDescending(card => DeckServiceHelpers.GetSnapshot(card).EdhrecRank ?? int.MaxValue)
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
        IReadOnlySet<string> gameChangers = await analysisMetrics.FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        CommanderBracketEstimate estimate = analysisMetrics.EstimateCommanderBracket(workspace, gameChangers);
        ReplacementWeights weights = NormalizeWeights(new ReplacementWeights { Role = 0.55, Power = 0.15, Price = 0.30 });
        List<ReplacementSuggestion> suggestions = [];
        HashSet<string> selectedReplacementNames = new(StringComparer.OrdinalIgnoreCase);
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, "Commander bracket reduction plan", "bracket-reduction");
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

            plan.Operations.Add(DeckEditOperation.RemoveCard(
                card.Name,
                card.Quantity,
                DeckCategoryOrdering.PrimaryCategory(card),
                $"Remove {card.Name} to reduce bracket pressure."));
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

        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace)
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
        await DeckServiceHelpers.RequirePlanRepository(planRepository).SaveAsync(plan, cancellationToken).ConfigureAwait(false);
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
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, "Mana base improvement plan", "mana-base-improvements");
        plan.Rationale = "Improves land count, fixing, and tapped-land pressure while preserving color identity.";

        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace)
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            .Where(card => DeckAnalysisMetrics.LooksTapped(DeckServiceHelpers.GetSnapshot(card)))
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

        ManaBaseAnalysis manaBase = analysisMetrics.AnalyzeManaBase(workspace);
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
        DeckConsistencyAnalysis consistency = analysisMetrics.AnalyzeDeckConsistency(workspace);
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, "Consistency improvement plan", "consistency-improvements");
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
}
