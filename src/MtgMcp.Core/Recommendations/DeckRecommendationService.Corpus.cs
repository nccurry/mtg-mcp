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
        Dictionary<string, List<CardCorpusSignal>> signalsByName = GroupSignalsByCard(report.Signals);
        List<CorpusRecommendation> recommendations = [];

        foreach (ReplacementSuggestion suggestion in planResult.Suggestions.Take(budget.MaxRecommendations))
        {
            cards.TryGetValue(suggestion.WithCard, out CardInfo? card);
            List<CardCorpusSignal> signals = signalsByName.TryGetValue(suggestion.WithCard, out List<CardCorpusSignal>? values)
                ? values
                : [];
            List<CorpusEvidence> evidence = BuildEvidence(signals, budget);
            recommendations.Add(new CorpusRecommendation
            {
                CardName = suggestion.WithCard,
                ReplaceCard = suggestion.ReplaceCard,
                RecommendationKind = "budget-replacement",
                Role = suggestion.Role,
                Tags = card is null ? [] : DeckRoleClassifier.Classify(CreateCandidateCard(card)).Tags,
                Score = Math.Clamp((suggestion.Score * 0.70) + (AverageSignalScore(signals) * 0.30), 0, 1),
                Confidence = Math.Clamp(0.45 + (evidence.Count * 0.10), 0, 0.90),
                Price = suggestion.CandidatePrice,
                EdhrecRank = card?.EdhrecRank,
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
            Sources = MergeSourceStatuses(report.Sources),
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
            Sources = MergeSourceStatuses(report.Sources)
        };
        result.Notes.AddRange(report.Notes);
        if (result.ExemplarDecks.Count == 0)
        {
            result.Notes.Add("No exemplar-deck provider is enabled yet; enable a stable or permissioned deck corpus source to populate this result.");
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
                CorpusRecommendation recommendation = BuildRecommendation(
                    card,
                    [],
                    recommendationKind: "explain-card",
                    goal: cardName,
                    budget,
                    replaceCard: null);
                recommendation.Rationale = "No enabled corpus source returned direct evidence for this card; showing local card metadata only.";
                result.Recommendations.Add(recommendation);
            }

            result.Notes.Add("No matching corpus evidence was found for the requested card.");
        }

        return result;
    }

    /// <summary>
    /// Lists configured and planned corpus sources.
    /// </summary>
    public CorpusSourceStatusResult ListCorpusSources()
    {
        return new CorpusSourceStatusResult
        {
            Sources = MergeSourceStatuses(CorpusSignalProviders.Select(provider => provider.GetStatus()).Concat(KnownCorpusSources()))
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
        Dictionary<string, List<CardCorpusSignal>> signalsByName = GroupSignalsByCard(report.Signals);
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
                || (lesserKnownOnly && !IsLesserKnown(card)))
            {
                continue;
            }

            List<CardCorpusSignal> signals = signalsByName.TryGetValue(card.Name, out List<CardCorpusSignal>? values)
                ? values
                : [];
            CorpusRecommendation recommendation = BuildRecommendation(card, signals, recommendationKind, goal ?? intent?.Archetype, budget, replaceCard: null);
            recommendations.Add(recommendation);
        }

        CorpusRecommendationResult result = new()
        {
            WorkspaceId = workspace.Id,
            Commander = intent?.Commander ?? FindCommanderName(workspace),
            Theme = intent?.Archetype ?? DominantTheme(workspace),
            AnalysisDepth = budget.AnalysisDepth,
            Budget = budget,
            Recommendations = recommendations
                .OrderByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.EdhrecRank ?? int.MaxValue)
                .Take(budget.MaxRecommendations)
                .ToList(),
            Sources = MergeSourceStatuses(report.Sources),
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
        if (CorpusSignalProviders.Count == 0)
        {
            result.Notes.Add("No API-backed corpus providers are configured, so no corpus recommendations were generated.");
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
        CancellationToken cancellationToken)
    {
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CorpusSignalQuery query = new()
        {
            WorkspaceId = workspace.Id,
            Format = workspace.Format,
            Commander = intent?.Commander ?? FindCommanderName(workspace),
            Theme = intent?.Archetype ?? DominantTheme(workspace),
            Goal = goal,
            ExistingCards = workspace.Cards.Select(card => card.Name).ToList(),
            MaxPrice = maxPrice,
            Refresh = refresh
        };
        CorpusSignalReport combined = new();
        if (budget.AnalysisDepth.Equals(AnalysisDepths.Best, StringComparison.OrdinalIgnoreCase))
        {
            combined.Sources.AddRange(KnownCorpusSources());
        }

        int queriedSources = 0;
        foreach (ICorpusSignalProvider provider in CorpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            combined.Sources.Add(status);
            if (!status.Enabled || queriedSources >= budget.MaxSources)
            {
                continue;
            }

            queriedSources++;
            try
            {
                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, cancellationToken).ConfigureAwait(false);
                combined.Signals.AddRange(report.Signals);
                combined.ExemplarDecks.AddRange(report.ExemplarDecks);
                combined.Discussions.AddRange(report.Discussions);
                combined.Sources.AddRange(report.Sources);
                combined.Notes.AddRange(report.Notes);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                status.Status = CorpusSourceStatuses.Failed;
                status.Notes.Add($"{exception.GetType().Name}: {exception.Message}");
                combined.Notes.Add($"{status.Name} failed; continuing with remaining corpus sources.");
            }
        }

        combined.Signals = DeduplicateSignals(combined.Signals)
            .OrderByDescending(signal => signal.Score)
            .Take(budget.MaxCandidates * Math.Max(1, budget.MaxSources))
            .ToList();
        combined.Discussions = DeduplicateDiscussions(combined.Discussions)
            .OrderByDescending(discussion => discussion.Score ?? 0)
            .ThenByDescending(discussion => discussion.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Clamp(budget.MaxDecksPerSource * budget.MaxEvidencePerRecommendation, 5, 100))
            .ToList();
        combined.Sources = MergeSourceStatuses(combined.Sources);
        return combined;
    }

    /// <summary>
    /// Builds a recommendation from card data and corpus signals.
    /// </summary>
    private static CorpusRecommendation BuildRecommendation(
        CardInfo card,
        IReadOnlyList<CardCorpusSignal> signals,
        string recommendationKind,
        string? goal,
        RecommendationAnalysisBudget budget,
        string? replaceCard)
    {
        DeckCard candidate = CreateCandidateCard(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        double signalScore = AverageSignalScore(signals);
        double sourceAgreement = signals.Select(signal => signal.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() / (double)Math.Max(1, budget.MaxSources);
        double roleScore = ScoreRoleFit(role, goal);
        double noveltyScore = IsLesserKnown(card) ? 0.75 : 0.35;
        double priceScore = ReadUsdPrice(card) is null ? 0.45 : 0.65;
        double score = Math.Clamp((signalScore * 0.45) + (roleScore * 0.25) + (sourceAgreement * 0.15) + (noveltyScore * 0.10) + (priceScore * 0.05), 0, 1);
        List<CorpusEvidence> evidence = BuildEvidence(signals, budget);
        return new CorpusRecommendation
        {
            CardName = card.Name,
            ReplaceCard = replaceCard,
            RecommendationKind = recommendationKind,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            Score = score,
            Confidence = Math.Clamp(0.35 + (evidence.Count * 0.10) + (sourceAgreement * 0.20), 0, 0.95),
            Price = ReadUsdPrice(card),
            EdhrecRank = card.EdhrecRank,
            Rationale = BuildCorpusRationale(card.Name, role.PrimaryRole, recommendationKind, evidence.Count, goal),
            Evidence = evidence
        };
    }

    /// <summary>
    /// Groups signals by case-insensitive card name.
    /// </summary>
    private static Dictionary<string, List<CardCorpusSignal>> GroupSignalsByCard(IEnumerable<CardCorpusSignal> signals)
    {
        return signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.CardName))
            .GroupBy(signal => signal.CardName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds compact evidence rows from card signals.
    /// </summary>
    private static List<CorpusEvidence> BuildEvidence(
        IReadOnlyList<CardCorpusSignal> signals,
        RecommendationAnalysisBudget budget)
    {
        return signals
            .OrderByDescending(signal => signal.Score)
            .ThenBy(signal => signal.Source)
            .Take(budget.MaxEvidencePerRecommendation)
            .Select(signal => new CorpusEvidence
            {
                Source = signal.Source,
                SignalType = signal.SignalType,
                Score = signal.Score,
                Summary = string.IsNullOrWhiteSpace(signal.Rationale)
                    ? $"{signal.SignalType} signal from {signal.Source}."
                    : signal.Rationale,
                Uri = budget.IncludeSourceUrls ? signal.Uri : null
            })
            .ToList();
    }

    /// <summary>
    /// Removes duplicate source/type/card signal rows.
    /// </summary>
    private static List<CardCorpusSignal> DeduplicateSignals(IEnumerable<CardCorpusSignal> signals)
    {
        return signals
            .GroupBy(signal => $"{signal.CardName}|{signal.Source}|{signal.SignalType}|{signal.Uri}|{signal.Rationale}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(signal => signal.Score).First())
            .ToList();
    }

    /// <summary>
    /// Removes duplicate discussion rows by source URL and body.
    /// </summary>
    private static List<DiscussionEvidence> DeduplicateDiscussions(IEnumerable<DiscussionEvidence> discussions)
    {
        return discussions
            .Where(discussion => !string.IsNullOrWhiteSpace(discussion.Uri) || !string.IsNullOrWhiteSpace(discussion.Body))
            .GroupBy(
                discussion => $"{discussion.Source}|{discussion.Uri}|{discussion.Body}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(discussion => discussion.Score ?? 0).First())
            .ToList();
    }

    /// <summary>
    /// Merges duplicate source rows by key.
    /// </summary>
    private static List<CorpusSourceStatus> MergeSourceStatuses(IEnumerable<CorpusSourceStatus> sources)
    {
        return sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Key) || !string.IsNullOrWhiteSpace(source.Name))
            .GroupBy(source => string.IsNullOrWhiteSpace(source.Key) ? source.Name : source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(source => source.Enabled ? 0 : 1)
            .ThenBy(source => source.Name)
            .ToList();
    }

    /// <summary>
    /// Lists corpus sources that need a structured API adapter or explicit permission before use.
    /// </summary>
    private static List<CorpusSourceStatus> KnownCorpusSources()
    {
        return
        [
            SourceStatus("edhrec-commander", "EDHREC commander aggregates", "unofficial-api", "https://edhrec.com/", stableApi: false, attributionRequired: true, apiType: CorpusSourceApiTypes.UnofficialApi, status: CorpusSourceStatuses.Disabled, permissionSensitive: true),
            SourceStatus("archidekt-exemplars", "Archidekt structured public endpoints", "unofficial-api", "https://archidekt.com/", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.UnofficialApi, status: CorpusSourceStatuses.Disabled, permissionSensitive: true),
            SourceStatus("moxfield-exemplars", "Moxfield structured public endpoints", "unofficial-api", "https://www.moxfield.com/", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.UnofficialApi, status: CorpusSourceStatuses.Disabled, permissionSensitive: true),
            SourceStatus("mtggoldfish", "MTGGoldfish metagame", "unsupported", "https://www.mtggoldfish.com/metagame/commander", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.Unsupported, status: CorpusSourceStatuses.Unsupported, permissionSensitive: true),
            SourceStatus("mtgdecks", "MTGDecks.net", "unsupported", "https://mtgdecks.net/", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.Unsupported, status: CorpusSourceStatuses.Unsupported, permissionSensitive: true),
            SourceStatus("magicgg", "Magic.gg decklists", "unsupported", "https://magic.gg/decklists", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.Unsupported, status: CorpusSourceStatuses.Unsupported, permissionSensitive: false),
            SourceStatus("mtgstocks", "MTGStocks market movers", "unsupported", "https://www.mtgstocks.com/", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.Unsupported, status: CorpusSourceStatuses.Unsupported, permissionSensitive: true),
            SourceStatus("aetherhub", "AetherHub DeckHub", "unsupported", "https://aetherhub.com/Docs/DeckHub", stableApi: false, attributionRequired: false, apiType: CorpusSourceApiTypes.Unsupported, status: CorpusSourceStatuses.Unsupported, permissionSensitive: true)
        ];
    }

    /// <summary>
    /// Creates one source status row.
    /// </summary>
    private static CorpusSourceStatus SourceStatus(
        string key,
        string name,
        string kind,
        string uri,
        bool stableApi,
        bool attributionRequired,
        string apiType,
        string status,
        bool permissionSensitive)
    {
        return new CorpusSourceStatus
        {
            Key = key,
            Name = name,
            Kind = kind,
            Enabled = false,
            StableApi = stableApi,
            ApiType = apiType,
            UnofficialApi = apiType.Equals(CorpusSourceApiTypes.UnofficialApi, StringComparison.OrdinalIgnoreCase),
            PermissionSensitive = permissionSensitive,
            AttributionRequired = attributionRequired,
            Status = status,
            Uri = uri,
            Notes = [apiType == CorpusSourceApiTypes.Unsupported
                ? "No supported structured API/feed is configured; HTML scraping is out of scope."
                : "Structured API adapter is not enabled in this build or by configuration."]
        };
    }

    /// <summary>
    /// Scores role fit against a user goal or theme.
    /// </summary>
    private static double ScoreRoleFit(CardRoleAssignment role, string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0.35 : 0.65;
        }

        if (goal.Contains(role.PrimaryRole, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Any(tag => goal.Contains(tag, StringComparison.OrdinalIgnoreCase)))
        {
            return 0.95;
        }

        return 0.55;
    }

    /// <summary>
    /// Computes the average source signal score.
    /// </summary>
    private static double AverageSignalScore(IReadOnlyList<CardCorpusSignal> signals)
    {
        return signals.Count == 0 ? 0.30 : signals.Average(signal => signal.Score);
    }

    /// <summary>
    /// Checks whether a card is lower-known for Commander recommendation purposes.
    /// </summary>
    private static bool IsLesserKnown(CardInfo card)
    {
        return !card.EdhrecRank.HasValue || card.EdhrecRank.Value > 5_000;
    }

    /// <summary>
    /// Builds a compact recommendation rationale.
    /// </summary>
    private static string BuildCorpusRationale(string cardName, string role, string kind, int evidenceCount, string? goal)
    {
        string goalText = string.IsNullOrWhiteSpace(goal) ? "the deck context" : goal;
        return evidenceCount == 0
            ? $"{cardName} is a {role} candidate for {goalText}."
            : $"{cardName} is a {role} candidate for {goalText} with {evidenceCount} corpus signal(s) supporting the {kind} recommendation.";
    }
}
