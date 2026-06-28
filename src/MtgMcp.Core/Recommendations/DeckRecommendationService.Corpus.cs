namespace MtgMcp.Core;

/// <summary>
/// Provides corpus-backed recommendation behavior.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Analyzes commander and theme trends using normalized corpus signals.
    /// </summary>
    public async Task<CorpusRecommendationResult> AnalyzeCommanderTrendsAsync(
        string workspaceId,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        budget.MaxRecommendations = Math.Min(budget.MaxRecommendations, Math.Clamp(limit, 1, 50));
        return await BuildCorpusRecommendationsAsync(
            workspaceId,
            goal: null,
            recommendationKind: "commander-trend",
            budget,
            maxPrice: null,
            includeExistingCards: false,
            lesserKnownOnly: false,
            refresh: refresh,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds lower-known cards with useful corpus or local-fit evidence.
    /// </summary>
    public async Task<CorpusRecommendationResult> FindLesserKnownCardsAsync(
        string workspaceId,
        string goal,
        int limit,
        decimal? maxPrice,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        budget.MaxRecommendations = Math.Min(budget.MaxRecommendations, Math.Clamp(limit, 1, 50));
        CorpusRecommendationResult result = await BuildCorpusRecommendationsAsync(
            workspaceId,
            goal,
            recommendationKind: "lesser-known",
            budget,
            maxPrice,
            includeExistingCards: false,
            lesserKnownOnly: true,
            refresh: refresh,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Recommendations.Count == 0)
        {
            result.Notes.Add("No lower-known cards met the current source, color, legality, and budget filters.");
        }

        return result;
    }

    /// <summary>
    /// Creates a budget replacement plan and enriches replacement suggestions with corpus evidence.
    /// </summary>
    public async Task<CorpusBudgetReplacementResult> FindCorpusBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice,
        decimal minSavings,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        RecommendationPlanResult planResult = await FindBudgetReplacementsAsync(
            workspaceId,
            maxPrice,
            minSavings,
            limit,
            weights: null,
            cancellationToken).ConfigureAwait(false);
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        CorpusSignalReport report = await CollectCorpusSignalsAsync(
            workspace,
            goal: "budget replacements",
            maxPrice,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog
            .GetCardsByNamesAsync(planResult.Suggestions.Select(suggestion => suggestion.WithCard).ToList(), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, List<CardCorpusSignal>> signalsByName = CorpusRecommendationBuilder.GroupSignalsByCard(report.Signals);
        List<CorpusRecommendation> recommendations = [];

        foreach (ReplacementSuggestion suggestion in planResult.Suggestions.Take(budget.MaxRecommendations))
        {
            cards.TryGetValue(suggestion.WithCard, out CardInfo? card);
            List<CardCorpusSignal> signals = signalsByName.TryGetValue(suggestion.WithCard, out List<CardCorpusSignal>? values)
                ? values
                : [];
            List<CorpusEvidence> evidence = CorpusRecommendationBuilder.BuildEvidence(signals, budget);
            recommendations.Add(new CorpusRecommendation
            {
                CardName = suggestion.WithCard,
                ReplaceCard = suggestion.ReplaceCard,
                RecommendationKind = "budget-replacement",
                Role = suggestion.Role,
                Tags = card is null ? [] : DeckRoleClassifier.Classify(CreateCandidateCard(card)).Tags,
                Score = Math.Clamp((suggestion.Score * 0.70) + (CorpusRecommendationBuilder.AverageSignalScore(signals) * 0.30), 0, 1),
                Confidence = Math.Clamp(0.45 + (evidence.Count * 0.10), 0, 0.90),
                Price = suggestion.CandidatePrice,
                EdhrecRank = card?.EdhrecRank,
                ScryfallUri = card?.ScryfallUri ?? suggestion.WithCardScryfallUri,
                ReplaceCardScryfallUri = suggestion.ReplaceCardScryfallUri,
                Rationale = evidence.Count == 0
                    ? suggestion.Rationale
                    : $"{suggestion.Rationale} Corpus evidence adds {evidence.Count} supporting signal(s).",
                Evidence = evidence
            });
        }

        return new CorpusBudgetReplacementResult
        {
            Plan = planResult.Plan,
            Recommendations = recommendations,
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources),
            AnalysisDepth = budget.AnalysisDepth,
            Notes = report.Notes
        };
    }

    /// <summary>
    /// Finds top exemplar decks from enabled corpus providers.
    /// </summary>
    public async Task<TopExemplarDecksResult> FindTopExemplarDecksAsync(
        string workspaceId,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        budget.IncludeExemplarDecks = true;
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        CorpusSignalReport report = await CollectCorpusSignalsAsync(
            workspace,
            goal: "top exemplar decks",
            maxPrice: null,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        TopExemplarDecksResult result = new()
        {
            WorkspaceId = workspace.Id,
            AnalysisDepth = budget.AnalysisDepth,
            ExemplarDecks = report.ExemplarDecks
                .OrderByDescending(deck => deck.Weight)
                .Take(Math.Min(Math.Clamp(limit, 1, 50), budget.MaxDecksPerSource))
                .ToList(),
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources)
        };
        result.Notes.AddRange(report.Notes);
        if (result.ExemplarDecks.Count == 0)
        {
            result.Notes.Add("No exemplar-deck provider is enabled yet; enable a stable or permissioned deck recommendation source to populate this result.");
        }

        return result;
    }

    /// <summary>
    /// Explains corpus evidence for a single card in a deck context.
    /// </summary>
    public async Task<CorpusRecommendationResult> ExplainCardCorpusSignalAsync(
        string workspaceId,
        string cardName,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        CorpusRecommendationResult result = await BuildCorpusRecommendationsAsync(
            workspaceId,
            goal: cardName,
            recommendationKind: "explain-card",
            budget,
            maxPrice: null,
            includeExistingCards: true,
            lesserKnownOnly: false,
            refresh: refresh,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        result.Recommendations = result.Recommendations
            .Where(recommendation => recommendation.CardName.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToList();
        if (result.Recommendations.Count == 0)
        {
            CardInfo? card = await CardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
            if (card is not null)
            {
                CorpusRecommendation recommendation = CorpusRecommendationBuilder.BuildRecommendation(
                    card,
                    [],
                    recommendationKind: "explain-card",
                    goal: cardName,
                    budget,
                    replaceCard: null);
                recommendation.Rationale = "No enabled recommendation source returned direct evidence for this card; showing local card metadata only.";
                result.Recommendations.Add(recommendation);
            }

            result.Notes.Add("No matching source evidence was found for the requested card.");
        }

        return result;
    }

    /// <summary>
    /// Searches one corpus source and returns raw evidence rows without synthesizing recommendations.
    /// </summary>
    public async Task<CorpusEvidenceSearchResult> SearchCorpusEvidenceAsync(
        string workspaceId,
        string sourceKey,
        string goal,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            throw new ArgumentException("A recommendation source key or name is required.", nameof(sourceKey));
        }

        int boundedLimit = Math.Clamp(limit, 1, 100);
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(analysisDepth);
        budget.MaxCandidates = Math.Clamp(Math.Max(budget.MaxCandidates, boundedLimit), 1, 200);
        budget.MaxRecommendations = Math.Clamp(boundedLimit, 1, 100);
        budget.IncludeSourceUrls = true;
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CommandZoneContext commandZone = FindCommandZoneContext(workspace);
        string? commander = FindCommanderName(workspace, intent);
        string? theme = FindCorpusTheme(workspace, intent, commandZone, goal);
        CorpusSignalReport report = await CollectCorpusSignalsAsync(
            workspace,
            string.IsNullOrWhiteSpace(goal) ? null : goal,
            maxPrice: null,
            budget,
            refresh,
            cancellationToken,
            sourceKey).ConfigureAwait(false);
        IReadOnlyDictionary<string, string?> scryfallUris = await ResolveScryfallUrisAsync(
            report.Signals.Select(signal => signal.CardName),
            cancellationToken).ConfigureAwait(false);

        CorpusEvidenceSearchResult result = new()
        {
            WorkspaceId = workspace.Id,
            Commander = commander,
            Theme = theme,
            SourceKey = sourceKey,
            AnalysisDepth = budget.AnalysisDepth,
            Budget = budget,
            CardEvidence = CorpusEvidenceTableBuilder.Build(report.Signals, workspace, boundedLimit, scryfallUris),
            Discussions = report.Discussions
                .OrderByDescending(discussion => discussion.Score ?? 0)
                .ThenByDescending(discussion => discussion.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(Math.Clamp(boundedLimit * budget.MaxEvidencePerRecommendation, 1, 100))
                .ToList(),
            ExemplarDecks = report.ExemplarDecks
                .OrderByDescending(deck => deck.Weight)
                .Take(Math.Clamp(boundedLimit, 1, budget.MaxDecksPerSource))
                .ToList(),
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources)
        };
        result.Notes.AddRange(report.Notes);
        if (result.CardEvidence.Count == 0 && result.Discussions.Count == 0 && result.ExemplarDecks.Count == 0)
        {
            result.Notes.Add("The requested recommendation source returned no raw evidence for this deck context.");
        }

        return result;
    }

    /// <summary>
    /// Lists configured corpus sources with real provider implementations.
    /// </summary>
    public CorpusSourceStatusResult ListCorpusSources()
    {
        return new CorpusSourceStatusResult
        {
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(corpusSignalProviders.Select(provider => provider.GetStatus()))
        };
    }

    /// <summary>
    /// Builds corpus recommendations from normalized source signals.
    /// </summary>
    private async Task<CorpusRecommendationResult> BuildCorpusRecommendationsAsync(
        string workspaceId,
        string? goal,
        string recommendationKind,
        RecommendationAnalysisBudget budget,
        decimal? maxPrice,
        bool includeExistingCards,
        bool lesserKnownOnly,
        bool refresh,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CorpusSignalReport report = await CollectCorpusSignalsAsync(workspace, goal, maxPrice, budget, refresh, cancellationToken).ConfigureAwait(false);
        HashSet<string> existing = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<CardCorpusSignal>> signalsByName = CorpusRecommendationBuilder.GroupSignalsByCard(report.Signals);
        List<string> candidateNames = signalsByName.Keys
            .Where(name => includeExistingCards || !existing.Contains(name))
            .Take(budget.MaxCandidates)
            .ToList();
        IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog.GetCardsByNamesAsync(candidateNames, cancellationToken).ConfigureAwait(false);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        List<CorpusRecommendation> recommendations = [];

        foreach (CardInfo card in cards.Values)
        {
            if ((!includeExistingCards && existing.Contains(card.Name))
                || !IsLegalInFormat(card, workspace.Format)
                || !IsInDeckColorIdentity(card, colorKnown, colors)
                || !IsPriceWithinBudget(ReadUsdPrice(card), maxPrice)
                || (lesserKnownOnly && !CorpusRecommendationBuilder.IsLesserKnown(card)))
            {
                continue;
            }

            List<CardCorpusSignal> signals = signalsByName.TryGetValue(card.Name, out List<CardCorpusSignal>? values)
                ? values
                : [];
            CorpusRecommendation recommendation = CorpusRecommendationBuilder.BuildRecommendation(card, signals, recommendationKind, goal ?? intent?.Archetype, budget, replaceCard: null);
            recommendations.Add(recommendation);
        }

        CorpusRecommendationResult result = new()
        {
            WorkspaceId = workspace.Id,
            Commander = FindCommanderName(workspace, intent),
            Theme = FindCorpusTheme(workspace, intent, FindCommandZoneContext(workspace), goal),
            AnalysisDepth = budget.AnalysisDepth,
            Budget = budget,
            Recommendations = recommendations
                .OrderByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.EdhrecRank ?? int.MaxValue)
                .Take(budget.MaxRecommendations)
                .ToList(),
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources),
            ExemplarDecks = budget.IncludeExemplarDecks
                ? report.ExemplarDecks.OrderByDescending(deck => deck.Weight).Take(budget.MaxDecksPerSource).ToList()
                : [],
            Discussions = report.Discussions
                .OrderByDescending(discussion => discussion.Score ?? 0)
                .ThenByDescending(discussion => discussion.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(Math.Clamp(budget.MaxRecommendations * budget.MaxEvidencePerRecommendation, 5, 100))
                .ToList()
        };
        result.Notes.AddRange(report.Notes);
        if (corpusSignalProviders.Count == 0)
        {
            result.Notes.Add("No API-backed recommendation sources are configured, so no source-backed recommendations were generated.");
        }

        return result;
    }

    /// <summary>
    /// Collects corpus signals from configured API-backed providers.
    /// </summary>
    private async Task<CorpusSignalReport> CollectCorpusSignalsAsync(
        DeckWorkspace workspace,
        string? goal,
        decimal? maxPrice,
        RecommendationAnalysisBudget budget,
        bool refresh,
        CancellationToken cancellationToken,
        string? sourceKey = null)
    {
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CommandZoneContext commandZone = FindCommandZoneContext(workspace);
        string? theme = FindCorpusTheme(workspace, intent, commandZone, goal);
        CorpusSignalQuery query = new()
        {
            WorkspaceId = workspace.Id,
            Format = workspace.Format,
            Commander = FindCommanderName(workspace, intent),
            CommanderNames = [.. commandZone.CommanderNames],
            Theme = theme,
            Goal = goal,
            ExistingCards = workspace.Cards.Select(card => card.Name).ToList(),
            MaxPrice = maxPrice,
            Refresh = refresh
        };
        CorpusSignalReport combined = new();
        if (commandZone.HasPartnerPair
            && string.IsNullOrWhiteSpace(goal)
            && !string.IsNullOrWhiteSpace(intent?.Archetype))
        {
            combined.Notes.Add("Partner commander evidence defaults to the broad pair aggregate instead of narrowing by saved archetype.");
        }

        CommanderThemeResolution themeResolution = await ResolveCommanderThemeAsync(
            query.Commander,
            query.Theme,
            goal,
            sourceKey,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        query.Theme = themeResolution.Theme;
        combined.Notes.AddRange(themeResolution.Notes);
        bool sourceFilterActive = !string.IsNullOrWhiteSpace(sourceKey);
        int queriedSources = 0;
        bool matchedSource = false;
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (sourceFilterActive && !CorpusSourceStatusHelpers.MatchesSourceFilter(status, sourceKey))
            {
                continue;
            }

            matchedSource = true;
            combined.Sources.Add(status);
            if (!status.Enabled || queriedSources >= budget.MaxSources)
            {
                continue;
            }

            queriedSources++;
            try
            {
                using CancellationTokenSource providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (budget.SourceTimeoutSeconds > 0)
                {
                    providerCancellation.CancelAfter(TimeSpan.FromSeconds(budget.SourceTimeoutSeconds));
                }

                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, providerCancellation.Token).ConfigureAwait(false);
                combined.Signals.AddRange(report.Signals);
                combined.ExemplarDecks.AddRange(report.ExemplarDecks);
                combined.Discussions.AddRange(report.Discussions);
                combined.Sources.AddRange(report.Sources);
                combined.Notes.AddRange(report.Notes);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status.Status = CorpusSourceStatusKind.Failed;
                status.Notes.Add($"Timed out after {budget.SourceTimeoutSeconds} second(s).");
                combined.Notes.Add($"{status.Name} timed out; continuing with remaining recommendation sources.");
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                status.Status = CorpusSourceStatusKind.Failed;
                status.Notes.Add($"{exception.GetType().Name}: {exception.Message}");
                combined.Notes.Add($"{status.Name} failed; continuing with remaining recommendation sources.");
            }
        }

        if (sourceFilterActive && !matchedSource)
        {
            combined.Notes.Add($"No configured recommendation source matched '{sourceKey}'.");
        }

        combined.Signals = CorpusRecommendationBuilder.DeduplicateSignals(combined.Signals)
            .OrderByDescending(signal => signal.Score)
            .Take(budget.MaxCandidates * Math.Max(1, budget.MaxSources))
            .ToList();
        combined.Discussions = CorpusRecommendationBuilder.DeduplicateDiscussions(combined.Discussions)
            .OrderByDescending(discussion => discussion.Score ?? 0)
            .ThenByDescending(discussion => discussion.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Clamp(budget.MaxDecksPerSource * budget.MaxEvidencePerRecommendation, 5, 100))
            .ToList();
        combined.Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(combined.Sources);
        return combined;
    }

    /// <summary>
    /// Picks the corpus theme while keeping partner commander defaults broad.
    /// </summary>
    private static string? FindCorpusTheme(
        DeckWorkspace workspace,
        DeckIntent? intent,
        CommandZoneContext commandZone,
        string? goal)
    {
        string? theme = intent?.Archetype ?? DominantTheme(workspace);
        return commandZone.HasPartnerPair && string.IsNullOrWhiteSpace(goal)
            ? null
            : theme;
    }

    /// <summary>
    /// Resolves Scryfall pages for source-only card rows without making the source lookup fail on catalog outages.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string?>> ResolveScryfallUrisAsync(
        IEnumerable<string> cardNames,
        CancellationToken cancellationToken)
    {
        List<string> names = cardNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyDictionary<string, CardInfo> cards;
        try
        {
            cards = await CardCatalog.GetCardsByNamesAsync(names, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCancellation(exception))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string?> scryfallUris = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, CardInfo> item in cards)
        {
            if (string.IsNullOrWhiteSpace(item.Value.ScryfallUri))
            {
                continue;
            }

            scryfallUris[item.Key] = item.Value.ScryfallUri;
            scryfallUris[item.Value.Name] = item.Value.ScryfallUri;
        }

        return scryfallUris;
    }

}
