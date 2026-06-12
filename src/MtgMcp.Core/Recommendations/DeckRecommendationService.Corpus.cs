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
                CorpusRecommendation recommendation = BuildRecommendation(
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
            Commander = intent?.Commander ?? FindCommanderName(workspace),
            Theme = intent?.Archetype ?? DominantTheme(workspace),
            SourceKey = sourceKey,
            AnalysisDepth = budget.AnalysisDepth,
            Budget = budget,
            CardEvidence = BuildCardEvidenceTable(report.Signals, workspace, boundedLimit, scryfallUris),
            Discussions = report.Discussions
                .OrderByDescending(discussion => discussion.Score ?? 0)
                .ThenByDescending(discussion => discussion.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(Math.Clamp(boundedLimit * budget.MaxEvidencePerRecommendation, 1, 100))
                .ToList(),
            ExemplarDecks = report.ExemplarDecks
                .OrderByDescending(deck => deck.Weight)
                .Take(Math.Clamp(boundedLimit, 1, budget.MaxDecksPerSource))
                .ToList(),
            Sources = MergeSourceStatuses(report.Sources)
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
            Sources = MergeSourceStatuses(corpusSignalProviders.Select(provider => provider.GetStatus()))
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
        bool sourceFilterActive = !string.IsNullOrWhiteSpace(sourceKey);
        int queriedSources = 0;
        bool matchedSource = false;
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (sourceFilterActive && !MatchesSourceFilter(status, sourceKey))
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
                status.Status = CorpusSourceStatuses.Failed;
                status.Notes.Add($"Timed out after {budget.SourceTimeoutSeconds} second(s).");
                combined.Notes.Add($"{status.Name} timed out; continuing with remaining recommendation sources.");
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                status.Status = CorpusSourceStatuses.Failed;
                status.Notes.Add($"{exception.GetType().Name}: {exception.Message}");
                combined.Notes.Add($"{status.Name} failed; continuing with remaining recommendation sources.");
            }
        }

        if (sourceFilterActive && !matchedSource)
        {
            combined.Notes.Add($"No configured recommendation source matched '{sourceKey}'.");
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
    /// Builds deterministic card evidence rows without applying recommendation scoring.
    /// </summary>
    private static List<CardEvidenceTableRow> BuildCardEvidenceTable(
        IReadOnlyList<CardCorpusSignal> signals,
        DeckWorkspace workspace,
        int limit,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        HashSet<string> existing = workspace.Cards
            .Select(card => card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.CardName))
            .GroupBy(
                signal => $"{signal.CardName}|{signal.Source}|{signal.SignalType}",
                signal => signal,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                CardCorpusSignal best = group
                    .OrderByDescending(signal => signal.Score)
                    .ThenBy(signal => signal.Source)
                    .First();
                int? deckCount = group.Any(signal => signal.DeckCount.HasValue)
                    ? group.Sum(signal => signal.DeckCount ?? 0)
                    : null;
                List<string> rationales = group
                    .Select(signal => signal.Rationale)
                    .Where(rationale => !string.IsNullOrWhiteSpace(rationale))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                return new CardEvidenceTableRow
                {
                    CardName = best.CardName,
                    Source = best.Source,
                    SignalType = best.SignalType,
                    Score = group.Max(signal => signal.Score),
                    EvidenceCount = deckCount ?? group.Count(),
                    DeckCount = deckCount,
                    InclusionRate = group
                        .Where(signal => signal.InclusionRate.HasValue)
                        .Select(signal => signal.InclusionRate)
                        .DefaultIfEmpty(best.InclusionRate)
                        .Max(),
                    AlreadyInDeck = existing.Contains(best.CardName),
                    Uri = group
                        .OrderByDescending(signal => signal.Score)
                        .Select(signal => signal.Uri)
                        .FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                    ScryfallUri = ResolveScryfallUri(
                        best.CardName,
                        group.Select(signal => signal.ScryfallUri).FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                        scryfallUris),
                    Rationale = rationales.Count == 0
                        ? $"{best.SignalType} evidence from {best.Source}."
                        : string.Join(" ", rationales)
                };
            })
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.EvidenceCount)
            .ThenBy(row => row.CardName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
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
        HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardCorpusSignal signal in signals)
        {
            sources.Add(signal.Source);
        }

        double sourceAgreement = sources.Count / (double)Math.Max(1, budget.MaxSources);
        double roleScore = ScoreRoleFit(role, goal);
        double noveltyScore = IsLesserKnown(card) ? 0.75 : 0.35;
        double priceScore = ReadUsdPrice(card) is null ? 0.45 : 0.65;
        bool lesserKnownRecommendation = recommendationKind.Equals("lesser-known", StringComparison.OrdinalIgnoreCase);
        bool offPlanComboEvidence = lesserKnownRecommendation && IsOffPlanComboEvidence(signals, goal);
        double effectiveSignalScore = offPlanComboEvidence ? Math.Min(signalScore, 0.55) : signalScore;
        double score = lesserKnownRecommendation
            ? Math.Clamp((roleScore * 0.45) + (effectiveSignalScore * 0.25) + (noveltyScore * 0.20) + (sourceAgreement * 0.05) + (priceScore * 0.05), 0, 1)
            : Math.Clamp((signalScore * 0.45) + (roleScore * 0.25) + (sourceAgreement * 0.15) + (noveltyScore * 0.10) + (priceScore * 0.05), 0, 1);
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
            ScryfallUri = card.ScryfallUri,
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
            .Select(group => group
                .OrderBy(source => SourceStatusPriority(source.Status))
                .First())
            .OrderBy(source => source.Enabled ? 0 : 1)
            .ThenBy(source => source.Name)
            .ToList();
    }

    /// <summary>
    /// Ranks source statuses so blocked or failed query statuses are not hidden by an initial available row.
    /// </summary>
    private static int SourceStatusPriority(string status)
    {
        return status switch
        {
            CorpusSourceStatuses.AccessBlocked => 0,
            CorpusSourceStatuses.Failed => 1,
            CorpusSourceStatuses.MissingConfig => 2,
            CorpusSourceStatuses.Disabled => 3,
            _ => 4
        };
    }

    /// <summary>
    /// Checks whether a source row matches a requested source key or display name.
    /// </summary>
    private static bool MatchesSourceFilter(CorpusSourceStatus source, string? sourceKey)
    {
        return string.IsNullOrWhiteSpace(sourceKey)
            || source.Key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase)
            || source.Name.Equals(sourceKey, StringComparison.OrdinalIgnoreCase);
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
    /// Checks whether combo-only evidence is off-plan for a non-combo lesser-known card request.
    /// </summary>
    private static bool IsOffPlanComboEvidence(IReadOnlyList<CardCorpusSignal> signals, string? goal)
    {
        return signals.Count > 0
            && signals.All(signal => signal.SignalType.Equals(CorpusSignalTypes.Combo, StringComparison.OrdinalIgnoreCase))
            && !GoalRequestsCombo(goal);
    }

    /// <summary>
    /// Checks whether the user goal explicitly asks for combo recommendations.
    /// </summary>
    private static bool GoalRequestsCombo(string? goal)
    {
        return !string.IsNullOrWhiteSpace(goal)
            && (goal.Contains("combo", StringComparison.OrdinalIgnoreCase)
                || goal.Contains("infinite", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Chooses the best available Scryfall page for a card row.
    /// </summary>
    private static string? ResolveScryfallUri(
        string cardName,
        string? preferredUri,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        if (!string.IsNullOrWhiteSpace(preferredUri))
        {
            return preferredUri;
        }

        return scryfallUris.TryGetValue(cardName, out string? uri) && !string.IsNullOrWhiteSpace(uri)
            ? uri
            : null;
    }
}
