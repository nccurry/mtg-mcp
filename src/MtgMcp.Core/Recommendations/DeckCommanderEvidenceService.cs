namespace MtgMcp.Core;

/// <summary>
/// Builds source-backed Commander aggregate, tag, and win-condition evidence.
/// </summary>
public sealed class DeckCommanderEvidenceService
{
    /// <summary>
    /// Resolves commander identity and source-only card rows through the catalog.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Supplies combo route classifications for Commander evidence bundles.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Supplies deterministic Commander source signals.
    /// </summary>
    private readonly IReadOnlyList<ICorpusSignalProvider> corpusSignalProviders;

    /// <summary>
    /// Resolves loose Commander theme hints against source-supported tags.
    /// </summary>
    private readonly CommanderThemeResolver commanderThemes;

    /// <summary>
    /// Finds payoff candidates for routes that need an outlet.
    /// </summary>
    private readonly DeckWinconPayoffSearchService payoffSearch;

    /// <summary>
    /// Creates a Commander evidence collaborator with explicit source, analysis, and payoff dependencies.
    /// </summary>
    public DeckCommanderEvidenceService(
        ICardCatalog cardCatalog,
        DeckAnalysisService analysis,
        IEnumerable<ICorpusSignalProvider> corpusSignalProviders,
        CommanderThemeResolver commanderThemes,
        DeckWinconPayoffSearchService payoffSearch)
    {
        this.cardCatalog = cardCatalog;
        this.analysis = analysis;
        this.corpusSignalProviders = corpusSignalProviders.ToList();
        this.commanderThemes = commanderThemes;
        this.payoffSearch = payoffSearch;
    }

    /// <summary>
    /// Gets source-backed aggregate cards for a commander.
    /// </summary>
    public async Task<CommanderAggregateCardsResult> GetCommanderAggregateCardsAsync(
        string commanderName,
        string? theme,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        string normalizedCommander = commanderName.Trim();
        string? normalizedTheme = CommanderThemeResolver.NormalizeTheme(theme);
        int boundedLimit = Math.Clamp(limit, 1, 100);
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(AnalysisDepths.Balanced);
        budget.MaxCandidates = boundedLimit;
        budget.MaxRecommendations = boundedLimit;
        CommanderThemeResolution themeResolution = await commanderThemes.ResolveAsync(
            normalizedCommander,
            normalizedTheme,
            goal: null,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CorpusSignalReport report = await CollectCommanderSignalsAsync(
            normalizedCommander,
            themeResolution.Theme,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        List<CardCorpusSignal> aggregateSignals = report.Signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.CardName))
            .OrderBy(signal => signal.Source, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(signal => signal.Score)
            .ThenByDescending(signal => signal.DeckCount ?? 0)
            .ThenBy(signal => signal.CardName, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToList();
        IReadOnlyDictionary<string, string?> scryfallUris = await CorpusEvidenceTableBuilder.ResolveScryfallUrisAsync(
            cardCatalog,
            aggregateSignals.Select(signal => signal.CardName),
            cancellationToken).ConfigureAwait(false);

        CommanderAggregateCardsResult result = new()
        {
            CommanderName = normalizedCommander,
            Theme = themeResolution.Theme,
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources),
            Cards = aggregateSignals.Select(signal => BuildAggregateRow(signal, scryfallUris)).ToList()
        };
        result.Notes.AddRange(themeResolution.Notes);
        result.Notes.AddRange(report.Notes);
        if (!string.IsNullOrWhiteSpace(themeResolution.Theme) && result.Cards.Count == 0)
        {
            CommanderThemeResolver.AddUnsupportedThemeNote(result.Notes, normalizedTheme, themeResolution.Theme, themeResolution.SuggestedThemes);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            result.Notes.Add("source was omitted; rows are grouped by source and counts are not merged across unlike populations.");
        }

        return result;
    }

    /// <summary>
    /// Gets source-backed tags and theme sections for a commander.
    /// </summary>
    public async Task<CommanderTagsResult> GetCommanderTagsAsync(
        string commanderName,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        int boundedLimit = Math.Clamp(limit, 1, 100);
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth(AnalysisDepths.Balanced);
        budget.MaxCandidates = boundedLimit * 2;
        CorpusSignalReport report = await CollectCommanderSignalsAsync(
            commanderName.Trim(),
            theme: null,
            source,
            budget,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CommanderTagsResult result = new()
        {
            CommanderName = commanderName.Trim(),
            Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(report.Sources),
            Tags = report.Signals
                .Select(signal => new
                {
                    Tag = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section,
                    signal.Source,
                    signal.DeckCount,
                    Uri = signal.Uri
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Tag))
                .GroupBy(row => $"{row.Source}|{row.Tag}", StringComparer.OrdinalIgnoreCase)
                .Select(group => new CommanderTagRow
                {
                    Source = group.First().Source,
                    TagName = group.First().Tag,
                    ThemeSlug = CommanderThemeResolver.SlugifySimple(group.First().Tag),
                    DeckCount = group.Any(row => row.DeckCount.HasValue)
                        ? group.Sum(row => row.DeckCount ?? 0)
                        : null,
                    Metadata = BuildMetadata(
                        group.First().Source,
                        "commander-aggregate-tag",
                        group.Select(row => row.Uri).FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                        confidence: 0.75)
                })
                .OrderBy(row => row.Source, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(row => row.DeckCount ?? 0)
                .ThenBy(row => row.TagName, StringComparer.OrdinalIgnoreCase)
                .Take(boundedLimit)
                .ToList()
        };
        result.Notes.AddRange(report.Notes);
        if (result.Tags.Count == 0)
        {
            result.Notes.Add("No configured source returned deterministic commander tag or theme rows.");
        }

        return result;
    }

    /// <summary>
    /// Bundles structured win-condition evidence for one commander.
    /// </summary>
    public async Task<CommanderWinConditionEvidenceResult> GetCommanderWinConditionEvidenceAsync(
        string commanderName,
        string? theme,
        bool strictColorIdentity,
        IReadOnlyList<string>? sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        List<string> requestedSources = NormalizeSources(sources);
        CommanderAggregateCardsResult aggregate = await GetAggregateForSourcesAsync(
            commanderName,
            theme,
            requestedSources,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        CommanderTagsResult tags = await GetTagsForSourcesAsync(
            commanderName,
            requestedSources,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        ComboEvidenceSearchResult combos = await analysis.SearchCombosByCardAsync(
            commanderName,
            "commander",
            commanderName,
            strictColorIdentity,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
        List<WinRouteClassification> routes = combos.Combos
            .SelectMany(combo => combo.RouteClassifications)
            .ToList();
        HashSet<string> payoffRoutes = routes
            .Where(route => route.NeedsPayoff)
            .SelectMany(route => route.RouteTypes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        CardInfo? commander = await cardCatalog.GetCardAsync(commanderName, cancellationToken).ConfigureAwait(false);
        string commanderColorIdentity = string.Join("", commander?.ColorIdentity ?? []);
        List<WinconPayoffSearchResult> payoffSearches = [];
        List<string> bundleNotes = [];
        if (commander is null && payoffRoutes.Count > 0)
        {
            bundleNotes.Add("Could not determine commander color identity from Scryfall, so payoff searches were not run.");
        }
        else
        {
            foreach (string payoffRoute in payoffRoutes.Take(5))
            {
                payoffSearches.Add(await payoffSearch.FindWinconPayoffsAsync(
                    payoffRoute,
                    commanderColorIdentity,
                    "commander",
                    maxPrice: null,
                    limit: Math.Min(limit, 10),
                    cancellationToken).ConfigureAwait(false));
            }
        }

        CommanderWinConditionEvidenceResult result = new()
        {
            CommanderName = commander?.Name ?? commanderName.Trim(),
            Theme = CommanderThemeResolver.NormalizeTheme(theme),
            AggregateCards = aggregate,
            Tags = tags,
            Combos = combos,
            RouteClassifications = routes,
            PayoffSearches = payoffSearches
        };
        result.Notes.Add("This bundle returns structured evidence only; the LLM should synthesize conclusions separately.");
        result.Notes.AddRange(bundleNotes);
        result.Notes.AddRange(aggregate.Notes);
        result.Notes.AddRange(combos.Notes);
        return result;
    }

    /// <summary>
    /// Collects commander signals directly from source providers without a workspace.
    /// </summary>
    private async Task<CorpusSignalReport> CollectCommanderSignalsAsync(
        string commanderName,
        string? theme,
        string? source,
        RecommendationAnalysisBudget budget,
        bool refresh,
        CancellationToken cancellationToken)
    {
        CorpusSignalQuery query = new()
        {
            Format = "commander",
            Commander = commanderName,
            Theme = theme,
            Refresh = refresh
        };
        CorpusSignalReport combined = new();
        bool sourceFilterActive = !string.IsNullOrWhiteSpace(source);
        bool matchedSource = false;
        int queriedSources = 0;
        foreach (ICorpusSignalProvider provider in corpusSignalProviders)
        {
            CorpusSourceStatus status = provider.GetStatus();
            if (sourceFilterActive && !CorpusSourceStatusHelpers.MatchesSourceFilter(status, source))
            {
                continue;
            }

            matchedSource = true;
            combined.Sources.Add(status);
            if (!status.Enabled || queriedSources >= budget.MaxSources)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(theme) && !CommanderThemeResolver.SupportsCommanderTheme(status))
            {
                combined.Notes.Add(
                    $"unsupported-theme: {status.Name} does not expose deterministic commander theme slugs; skipped theme lookup.");
                continue;
            }

            queriedSources++;
            try
            {
                CorpusSignalReport report = await provider.GetSignalsAsync(query, budget, cancellationToken)
                    .ConfigureAwait(false);
                combined.Signals.AddRange(report.Signals);
                combined.Sources.AddRange(report.Sources);
                combined.Notes.AddRange(report.Notes);
            }
            catch (Exception exception) when (!DeckServiceHelpers.IsCancellation(exception))
            {
                status.Status = CorpusSourceStatusKind.Failed;
                status.Notes.Add($"{exception.GetType().Name}: {exception.Message}");
                combined.Notes.Add($"{status.Name} failed; continuing with remaining recommendation sources.");
            }
        }

        if (sourceFilterActive && !matchedSource)
        {
            combined.Notes.Add($"No configured recommendation source matched '{source}'.");
        }

        combined.Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(combined.Sources);
        return combined;
    }

    /// <summary>
    /// Gets aggregate rows from the requested source set without merging source populations.
    /// </summary>
    private async Task<CommanderAggregateCardsResult> GetAggregateForSourcesAsync(
        string commanderName,
        string? theme,
        IReadOnlyList<string> sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                source: null,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        if (sources.Count == 1)
        {
            return await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                sources[0],
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        int boundedLimit = Math.Clamp(limit, 1, 100);
        CommanderAggregateCardsResult combined = new()
        {
            CommanderName = commanderName.Trim(),
            Theme = CommanderThemeResolver.NormalizeTheme(theme)
        };
        foreach (string source in sources)
        {
            CommanderAggregateCardsResult sourceResult = await GetCommanderAggregateCardsAsync(
                commanderName,
                theme,
                source,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
            combined.Cards.AddRange(sourceResult.Cards);
            combined.Sources.AddRange(sourceResult.Sources);
            combined.Notes.AddRange(sourceResult.Notes);
        }

        combined.Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(combined.Sources);
        combined.Cards.Sort(CompareAggregateRows);
        if (combined.Cards.Count > boundedLimit)
        {
            combined.Cards.RemoveRange(boundedLimit, combined.Cards.Count - boundedLimit);
        }

        combined.Notes.Add("sources were queried separately; counts are not merged across unlike populations.");
        return combined;
    }

    /// <summary>
    /// Gets tag rows from the requested source set without merging unlike populations.
    /// </summary>
    private async Task<CommanderTagsResult> GetTagsForSourcesAsync(
        string commanderName,
        IReadOnlyList<string> sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return await GetCommanderTagsAsync(
                commanderName,
                source: null,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        if (sources.Count == 1)
        {
            return await GetCommanderTagsAsync(
                commanderName,
                sources[0],
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
        }

        int boundedLimit = Math.Clamp(limit, 1, 100);
        CommanderTagsResult combined = new()
        {
            CommanderName = commanderName.Trim()
        };
        foreach (string source in sources)
        {
            CommanderTagsResult sourceResult = await GetCommanderTagsAsync(
                commanderName,
                source,
                limit,
                refresh,
                cancellationToken).ConfigureAwait(false);
            combined.Tags.AddRange(sourceResult.Tags);
            combined.Sources.AddRange(sourceResult.Sources);
            combined.Notes.AddRange(sourceResult.Notes);
        }

        combined.Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(combined.Sources);
        combined.Tags.Sort(CompareTagRows);
        if (combined.Tags.Count > boundedLimit)
        {
            combined.Tags.RemoveRange(boundedLimit, combined.Tags.Count - boundedLimit);
        }

        combined.Notes.Add("sources were queried separately; tag counts are not merged across unlike populations.");
        return combined;
    }

    /// <summary>
    /// Converts one corpus signal into a public commander aggregate row.
    /// </summary>
    private static CommanderAggregateCardRow BuildAggregateRow(
        CardCorpusSignal signal,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        return new CommanderAggregateCardRow
        {
            CardName = signal.CardName,
            Source = signal.Source,
            Section = string.IsNullOrWhiteSpace(signal.Section) ? signal.SignalType : signal.Section,
            DeckCount = signal.DeckCount,
            EligibleDeckCount = signal.EligibleDeckCount,
            InclusionRate = signal.InclusionRate,
            SynergyScore = signal.SynergyScore,
            Score = signal.Score,
            ScryfallUri = CorpusEvidenceTableBuilder.ResolveScryfallUri(signal.CardName, signal.ScryfallUri, scryfallUris),
            Metadata = BuildMetadata(signal.Source, "commander-aggregate-card", signal.Uri, signal.Score)
        };
    }

    /// <summary>
    /// Normalizes optional source filters for bundle tools.
    /// </summary>
    private static List<string> NormalizeSources(IReadOnlyList<string>? sources)
    {
        if (sources is null)
        {
            return [];
        }

        List<string> normalizedSources = [];
        foreach (string source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)
                || normalizedSources.Contains(source.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedSources.Add(source.Trim());
        }

        return normalizedSources;
    }

    /// <summary>
    /// Sorts aggregate rows by source population and source-provided scores.
    /// </summary>
    private static int CompareAggregateRows(CommanderAggregateCardRow left, CommanderAggregateCardRow right)
    {
        int source = string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0)
        {
            return source;
        }

        int score = right.Score.CompareTo(left.Score);
        if (score != 0)
        {
            return score;
        }

        int deckCount = (right.DeckCount ?? 0).CompareTo(left.DeckCount ?? 0);
        return deckCount != 0
            ? deckCount
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts tag rows by source and source-provided deck counts.
    /// </summary>
    private static int CompareTagRows(CommanderTagRow left, CommanderTagRow right)
    {
        int source = string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0)
        {
            return source;
        }

        int deckCount = (right.DeckCount ?? 0).CompareTo(left.DeckCount ?? 0);
        return deckCount != 0
            ? deckCount
            : string.Compare(left.TagName, right.TagName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds source metadata for deterministic evidence rows.
    /// </summary>
    private static SourceEvidenceMetadata BuildMetadata(
        string source,
        string sourceKind,
        string? sourceUri,
        double confidence)
    {
        return new SourceEvidenceMetadata
        {
            Source = source,
            SourceKind = sourceKind,
            SourceUri = sourceUri,
            CacheStatus = "live-or-cache",
            Confidence = Math.Clamp(confidence, 0, 1),
            Deterministic = true
        };
    }
}
