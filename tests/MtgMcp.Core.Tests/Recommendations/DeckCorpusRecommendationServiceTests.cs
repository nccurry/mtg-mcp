using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains corpus-backed recommendation and cache tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Verifies that analysis depth aliases map to expected corpus budgets.
    /// </summary>
    [Fact]
    public void RecommendationAnalysisBudget_FromDepth_NormalizesProfiles()
    {
        RecommendationAnalysisBudget minimal = RecommendationAnalysisBudget.FromDepth("minimize");
        RecommendationAnalysisBudget balanced = RecommendationAnalysisBudget.FromDepth(null);
        RecommendationAnalysisBudget best = RecommendationAnalysisBudget.FromDepth("best-analysis");

        minimal.AnalysisDepth.Should().Be(AnalysisDepths.Minimal);
        minimal.MaxSources.Should().Be(2);
        minimal.SourceTimeoutSeconds.Should().Be(12);
        minimal.IncludeSourceUrls.Should().BeFalse();
        minimal.IncludeComboDetails.Should().BeFalse();
        balanced.AnalysisDepth.Should().Be(AnalysisDepths.Balanced);
        balanced.MaxSources.Should().Be(4);
        balanced.SourceTimeoutSeconds.Should().Be(20);
        best.AnalysisDepth.Should().Be(AnalysisDepths.Best);
        best.MaxSources.Should().Be(10);
        best.MaxEvidencePerRecommendation.Should().Be(6);
        best.SourceTimeoutSeconds.Should().Be(25);
    }

    /// <summary>
    /// Verifies that commander aggregate rows preserve source grouping and counts.
    /// </summary>
    [Fact]
    public async Task GetCommanderAggregateCardsAsync_ReturnsSourceBackedRowsWithoutMergingSources()
    {
        FakeCorpusSignalProvider provider = new();
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        CommanderAggregateCardsResult result = await service.GetCommanderAggregateCardsAsync(
            "Tinybones, Trinket Thief",
            theme: "discard",
            source: null,
            limit: 5,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.CommanderName.Should().Be("Tinybones, Trinket Thief");
        result.Cards.Should().Contain(row => row.CardName == "Arcane Signet"
            && row.Source == "Fake corpus"
            && row.Section == "top-cards"
            && row.DeckCount == 300
            && row.EligibleDeckCount == 600
            && row.ScryfallUri!.EndsWith(Uri.EscapeDataString("Arcane Signet"), StringComparison.Ordinal));
        result.Notes.Should().Contain(note => note.Contains("grouped by source", StringComparison.OrdinalIgnoreCase));
        provider.LastQuery?.Commander.Should().Be("Tinybones, Trinket Thief");
        provider.LastQuery?.Theme.Should().Be("discard");
    }

    /// <summary>
    /// Verifies that sources without deterministic theme support are skipped for theme lookups.
    /// </summary>
    [Fact]
    public async Task GetCommanderAggregateCardsAsync_SkipsSourcesWithoutThemeSupport()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders:
            [
                new FakeCorpusSignalProvider(
                    key: "decklist-sample",
                    sourceName: "Decklist Sample",
                    primaryCard: "Thought Vessel",
                    kind: "decklist-api")
            ]);

        CommanderAggregateCardsResult result = await service.GetCommanderAggregateCardsAsync(
            "Tinybones, Trinket Thief",
            theme: "discard",
            source: "decklist-sample",
            limit: 5,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.Cards.Should().BeEmpty();
        result.Notes.Should().Contain(note => note.Contains("unsupported-theme", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that noisy natural-language theme text retries an obvious commander theme slug.
    /// </summary>
    [Fact]
    public async Task GetCommanderAggregateCardsAsync_ResolvesNoisyThemeTextToKnownCommanderTheme()
    {
        ThemeAwareCommanderCorpusProvider provider = new();
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        CommanderAggregateCardsResult result = await service.GetCommanderAggregateCardsAsync(
            "Vihaan, Goldwaker",
            theme: "Vihaan treasure outlaws draw removal lands",
            source: "edhrec",
            limit: 5,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.Theme.Should().Be("treasure");
        result.Cards.Should().Contain(row => row.CardName == "Prosperous Bandit");
        result.Notes.Should().Contain(note => note.Contains("theme-resolved", StringComparison.OrdinalIgnoreCase));
        provider.Queries.Should().ContainSingle(query => query.Theme == "treasure");
        provider.Queries.Should().NotContain(query => query.Theme == "vihaan treasure outlaws draw removal lands");
    }

    /// <summary>
    /// Verifies that unsupported commander themes include actionable alternatives.
    /// </summary>
    [Fact]
    public async Task GetCommanderAggregateCardsAsync_SuggestsAlternativesForUnsupportedTheme()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [new ThemeAwareCommanderCorpusProvider()]);

        CommanderAggregateCardsResult result = await service.GetCommanderAggregateCardsAsync(
            "Vihaan, Goldwaker",
            theme: "engines",
            source: "edhrec",
            limit: 5,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.Cards.Should().BeEmpty();
        result.Notes.Should().Contain(note =>
            note.Contains("unsupported-theme", StringComparison.OrdinalIgnoreCase)
            && note.Contains("Suggested alternatives", StringComparison.OrdinalIgnoreCase)
            && note.Contains("treasure", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that commander tags are derived from source-backed sections.
    /// </summary>
    [Fact]
    public async Task GetCommanderTagsAsync_ReturnsSourceBackedSections()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CommanderTagsResult result = await service.GetCommanderTagsAsync(
            "Tinybones, Trinket Thief",
            source: "fake-corpus",
            limit: 5,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.Tags.Should().Contain(row => row.TagName == "top-cards"
            && row.ThemeSlug == "top-cards"
            && row.Source == "Fake corpus");
    }

    /// <summary>
    /// Verifies that commander candidate discovery applies source-call bounds and reports partial failures.
    /// </summary>
    [Fact]
    public async Task SearchCommanderCandidates_BoundsEdhrecFetchesAndReportsFailures()
    {
        FakeCardCatalog catalog = new();
        CommanderCandidateCorpusProvider provider = new(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alesha, Who Smiles at Death"] = 2_200,
                ["Tatyova, Benthic Druid"] = 4_500,
                ["Roon of the Hidden Realm"] = 2_800,
            },
            failingCommanders: ["Glissa Sunslayer"]);
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            catalog,
            corpusSignalProviders: [provider]);

        CommanderCandidateSearchResult result = await service.SearchCommanderCandidatesAsync(
            colorIdentity: "brw",
            exactColorIdentity: true,
            minEligibleDecks: 1_500,
            maxEligibleDecks: 3_500,
            limit: 5,
            scryfallCandidateCap: 2000,
            edhrecFetchCap: 3,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.ColorIdentity.Should().Be("WBR");
        result.ScryfallCandidateCap.Should().Be(200);
        result.ScryfallCandidatesInspected.Should().Be(4);
        result.EdhrecFetchCap.Should().Be(3);
        result.EdhrecFetchesAttempted.Should().Be(3);
        result.Commanders.Should().ContainSingle().Which.CommanderName.Should().Be("Alesha, Who Smiles at Death");
        result.Notes.Should().Contain(note => note.Contains("EDHREC failed", StringComparison.OrdinalIgnoreCase));
        result.Notes.Should().Contain(note => note.Contains("Glissa Sunslayer", StringComparison.OrdinalIgnoreCase)
            && note.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        result.Notes.Should().Contain(note => note.Contains("fetch cap", StringComparison.OrdinalIgnoreCase));
        catalog.SearchQueries.Should().Contain("CommanderCandidates:WBR:True");
        provider.Queries.Select(query => query.Commander).Should().Equal(
            "Alesha, Who Smiles at Death",
            "Tatyova, Benthic Druid",
            "Glissa Sunslayer");
    }

    /// <summary>
    /// Verifies that the win-condition bundle honors multiple requested aggregate sources separately.
    /// </summary>
    [Fact]
    public async Task GetCommanderWinConditionEvidenceAsync_RestrictsRequestedSourcesWithoutMerging()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders:
            [
                new FakeCorpusSignalProvider(),
                new FakeCorpusSignalProvider("other-source", "Other Source", "Thought Vessel")
            ]);

        CommanderWinConditionEvidenceResult result = await service.GetCommanderWinConditionEvidenceAsync(
            "Tinybones, Trinket Thief",
            theme: null,
            strictColorIdentity: true,
            sources: ["fake-corpus", "other-source"],
            limit: 10,
            refresh: false,
            TestContext.Current.CancellationToken);

        result.AggregateCards.Cards.Should().Contain(row => row.Source == "Fake corpus");
        result.AggregateCards.Cards.Should().Contain(row => row.Source == "Other Source");
        result.AggregateCards.Notes.Should().Contain(note =>
            note.Contains("not merged", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that payoff finder labels candidates as Scryfall-query-derived evidence.
    /// </summary>
    [Fact]
    public async Task FindWinconPayoffsAsync_ReturnsScryfallQueryDerivedCandidates()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog());

        WinconPayoffSearchResult result = await service.FindWinconPayoffsAsync(
            WinRouteLabels.Aristocrats,
            "B",
            "commander",
            maxPrice: 5,
            limit: 2,
            TestContext.Current.CancellationToken);

        result.Candidates.Should().NotBeEmpty();
        result.Candidates.Should().OnlyContain(candidate => candidate.Metadata.SourceKind == "payoff-candidate-search");
        result.Notes.Should().Contain(note => note.Contains("not popularity evidence", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that cache duration parsing supports compact configured values.
    /// </summary>
    [Fact]
    public void CorpusCacheFactory_ParseDuration_HandlesCompactValues()
    {
        CorpusCacheFactory.ParseDuration("6h", TimeSpan.Zero).Should().Be(TimeSpan.FromHours(6));
        CorpusCacheFactory.ParseDuration("7d", TimeSpan.Zero).Should().Be(TimeSpan.FromDays(7));
        CorpusCacheFactory.ParseDuration("00:15:00", TimeSpan.Zero).Should().Be(TimeSpan.FromMinutes(15));
        CorpusCacheFactory.ParseDuration("bogus", TimeSpan.FromMinutes(3)).Should().Be(TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// Verifies that memory corpus cache respects freshness and key versioning.
    /// </summary>
    [Fact]
    public async Task MemoryCorpusCache_RespectsExpiryAndAdapterVersion()
    {
        MemoryCorpusCache cache = new(new MtgMcpCorpusCacheOptions { MaxEntries = 10 });
        CorpusCacheKey key = new()
        {
            Source = "source",
            Endpoint = "endpoint",
            Query = "query",
            AdapterVersion = "1",
            Budget = "balanced"
        };
        await cache.SetAsync(key, new CorpusSignalReport { Notes = ["fresh"] }, TestContext.Current.CancellationToken);

        (await cache.GetAsync<CorpusSignalReport>(key, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken))
            .Should()
            .NotBeNull();
        (await cache.GetAsync<CorpusSignalReport>(
                CacheKey("query", adapterVersion: "2"),
                TimeSpan.FromMinutes(5),
                TestContext.Current.CancellationToken))
            .Should()
            .BeNull();
        (await cache.GetAsync<CorpusSignalReport>(key, TimeSpan.Zero, TestContext.Current.CancellationToken))
            .Should()
            .BeNull();
    }

    /// <summary>
    /// Verifies that persisted cache prunes entries when configured limits are exceeded.
    /// </summary>
    [Fact]
    public async Task FileCorpusCache_PrunesByEntriesAndBytes()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"mtg-mcp-cache-{Guid.NewGuid():N}");
        try
        {
            FileCorpusCache cache = new(cacheDirectory, new MtgMcpCorpusCacheOptions { MaxEntries = 1, MaxBytes = 512 });
            await cache.SetAsync(CacheKey("one"), new CorpusSignalReport { Notes = [new string('a', 800)] }, TestContext.Current.CancellationToken);
            await cache.SetAsync(CacheKey("two"), new CorpusSignalReport { Notes = ["two"] }, TestContext.Current.CancellationToken);

            Directory.EnumerateFiles(cacheDirectory, "*.json").Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that persisted cache treats expired or unreadable files as misses.
    /// </summary>
    [Fact]
    public async Task FileCorpusCache_TreatsExpiredAndCorruptEntriesAsMisses()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), $"mtg-mcp-cache-{Guid.NewGuid():N}");
        try
        {
            FileCorpusCache cache = new(cacheDirectory, new MtgMcpCorpusCacheOptions { MaxEntries = 10 });
            CorpusCacheKey expiredKey = CacheKey("expired");
            await cache.SetAsync(expiredKey, new CorpusSignalReport { Notes = ["expired"] }, TestContext.Current.CancellationToken);

            (await cache.GetAsync<CorpusSignalReport>(expiredKey, TimeSpan.Zero, TestContext.Current.CancellationToken))
                .Should()
                .BeNull();

            CorpusCacheKey corruptKey = CacheKey("corrupt");
            await cache.SetAsync(corruptKey, new CorpusSignalReport { Notes = ["corrupt"] }, TestContext.Current.CancellationToken);
            string corruptPath = Directory.EnumerateFiles(cacheDirectory, "*.json").Single();
            File.WriteAllText(corruptPath, "{ not-json");

            (await cache.GetAsync<CorpusSignalReport>(corruptKey, TimeSpan.FromHours(1), TestContext.Current.CancellationToken))
                .Should()
                .BeNull();
            Directory.EnumerateFiles(cacheDirectory, "*.json").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that commander trends aggregate enabled corpus providers with the selected budget.
    /// </summary>
    [Fact]
    public async Task AnalyzeCommanderTrends_UsesCorpusProviderAndDepthBudget()
    {
        InMemoryRepository workspaces = new();
        FakeCorpusSignalProvider provider = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        CorpusRecommendationResult result = await service.AnalyzeCommanderTrendsAsync(
            workspace.Id,
            limit: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        provider.LastQuery.Should().NotBeNull();
        provider.LastQuery!.Commander.Should().Be("Tinybones, Trinket Thief");
        provider.LastBudget.Should().NotBeNull();
        provider.LastBudget!.AnalysisDepth.Should().Be(AnalysisDepths.Minimal);
        result.AnalysisDepth.Should().Be(AnalysisDepths.Minimal);
        result.Sources.Should().Contain(source => source.Key == "fake-corpus" && source.Enabled);
        result.Recommendations.Should().Contain(recommendation => recommendation.CardName == "Illness in the Ranks");
        CorpusRecommendation illness = result.Recommendations
            .Single(recommendation => recommendation.CardName == "Illness in the Ranks");
        illness.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Illness in the Ranks"));
        CorpusEvidence evidence = illness.Evidence.Should().ContainSingle().Subject;
        evidence.Source.Should().Be("Fake corpus");
        evidence.SignalType.Should().Be(CorpusSignalTypes.Novelty);
        evidence.Uri.Should().BeNull();
    }

    /// <summary>
    /// Verifies that corpus tools do not synthesize built-in staple recommendations without providers.
    /// </summary>
    [Fact]
    public async Task AnalyzeCommanderTrends_WithoutCorpusProvidersDoesNotUseHeuristicFallback()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(workspaces, new FakeCardCatalog());

        CorpusRecommendationResult result = await service.AnalyzeCommanderTrendsAsync(
            workspace.Id,
            limit: 10,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Recommendations.Should().BeEmpty();
        result.Sources.Should().BeEmpty();
        result.Notes.Should().Contain(note => note.Contains("No API-backed recommendation sources", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that failing corpus providers are reported without aborting the recommendation flow.
    /// </summary>
    [Fact]
    public async Task AnalyzeCommanderTrends_ReportsProviderFailureAndContinues()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new FailingCorpusSignalProvider(), new FakeCorpusSignalProvider()]);

        CorpusRecommendationResult result = await service.AnalyzeCommanderTrendsAsync(
            workspace.Id,
            limit: 5,
            analysisDepth: "balanced",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Sources.Should().Contain(source =>
            source.Key == "failing-corpus"
            && source.Status == CorpusSourceStatuses.Failed);
        result.Recommendations.Should().Contain(recommendation => recommendation.CardName == "Illness in the Ranks");
        result.Notes.Should().Contain(note => note.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that provider timeouts are isolated from other corpus sources.
    /// </summary>
    [Fact]
    public async Task AnalyzeCommanderTrends_ReportsProviderTimeoutAndContinues()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new TimeoutCorpusSignalProvider(), new FakeCorpusSignalProvider()]);

        CorpusRecommendationResult result = await service.AnalyzeCommanderTrendsAsync(
            workspace.Id,
            limit: 5,
            analysisDepth: "balanced",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Sources.Should().Contain(source =>
            source.Key == "timeout-corpus"
            && source.Status == CorpusSourceStatuses.Failed
            && source.Notes.Any(note => note.Contains("Timed out", StringComparison.OrdinalIgnoreCase)));
        result.Recommendations.Should().Contain(recommendation => recommendation.CardName == "Illness in the Ranks");
    }

    /// <summary>
    /// Verifies that source-specific searches return raw grouped evidence without querying other providers.
    /// </summary>
    [Fact]
    public async Task SearchCorpusEvidence_ReturnsSourceFilteredEvidenceTable()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        FakeCorpusSignalProvider provider = new();
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new FailingCorpusSignalProvider(), provider]);

        CorpusEvidenceSearchResult result = await service.SearchCorpusEvidenceAsync(
            workspace.Id,
            sourceKey: "fake-corpus",
            goal: "token hate",
            limit: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        provider.LastQuery.Should().NotBeNull();
        result.SourceKey.Should().Be("fake-corpus");
        result.Sources.Should().ContainSingle(source => source.Key == "fake-corpus");
        result.CardEvidence.Should().Contain(row =>
            row.CardName == "Illness in the Ranks"
            && row.Source == "Fake corpus"
            && row.SignalType == CorpusSignalTypes.Novelty
            && row.EvidenceCount == 12
            && row.ScryfallUri!.EndsWith(Uri.EscapeDataString("Illness in the Ranks"), StringComparison.Ordinal)
            && !row.AlreadyInDeck);
        result.Notes.Should().NotContain(note => note.Contains("Failing corpus", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that source evidence searches prefer obvious commander themes from the goal over stale local categories.
    /// </summary>
    [Fact]
    public async Task SearchCorpusEvidence_ResolvesGoalToKnownCommanderTheme()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(VihaanWorkspace(), TestContext.Current.CancellationToken);
        ThemeAwareCommanderCorpusProvider provider = new();
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        CorpusEvidenceSearchResult result = await service.SearchCorpusEvidenceAsync(
            workspace.Id,
            sourceKey: "edhrec",
            goal: "Vihaan treasure outlaws draw removal lands",
            limit: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.CardEvidence.Should().Contain(row => row.CardName == "Prosperous Bandit");
        result.Notes.Should().Contain(note => note.Contains("theme-resolved", StringComparison.OrdinalIgnoreCase));
        provider.Queries.Should().ContainSingle(query => query.Theme == "treasure");
        provider.Queries.Should().NotContain(query => query.Theme == "engines");
    }

    /// <summary>
    /// Verifies that removed roadmap-only sources no longer appear as disabled providers.
    /// </summary>
    [Fact]
    public async Task SearchCorpusEvidence_UnconfiguredRoadmapSourceReportsNoMatch()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusEvidenceSearchResult result = await service.SearchCorpusEvidenceAsync(
            workspace.Id,
            sourceKey: "edhrec-commander",
            goal: "discard",
            limit: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Sources.Should().BeEmpty();
        result.CardEvidence.Should().BeEmpty();
        result.ExemplarDecks.Should().BeEmpty();
        result.Discussions.Should().BeEmpty();
        result.Notes.Should().Contain("No configured recommendation source matched 'edhrec-commander'.");
    }

    /// <summary>
    /// Verifies that lesser-known recommendations filter out popular corpus signals.
    /// </summary>
    [Fact]
    public async Task FindLesserKnownCards_FiltersPopularSignals()
    {
        InMemoryRepository workspaces = new();
        FakeCorpusSignalProvider provider = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        CorpusRecommendationResult result = await service.FindLesserKnownCardsAsync(
            workspace.Id,
            goal: "token hate",
            limit: 5,
            maxPrice: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Recommendations.Should().ContainSingle(recommendation => recommendation.CardName == "Illness in the Ranks");
        result.Recommendations.Should().NotContain(recommendation => recommendation.CardName == "Lightning Greaves");
        result.Recommendations.Should().OnlyContain(recommendation =>
            !recommendation.EdhrecRank.HasValue || recommendation.EdhrecRank > 5_000);
    }

    /// <summary>
    /// Verifies that lesser-known discovery favors explicit plan fit over off-plan combo-only evidence.
    /// </summary>
    [Fact]
    public async Task FindLesserKnownCards_PrioritizesPlanFitOverOffPlanComboSignals()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new PlanFitCorpusSignalProvider()]);

        CorpusRecommendationResult result = await service.FindLesserKnownCardsAsync(
            workspace.Id,
            goal: "tokens",
            limit: 2,
            maxPrice: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Recommendations.Should().HaveCount(2);
        result.Recommendations[0].CardName.Should().Be("Hidden Token Maker");
        result.Recommendations[1].CardName.Should().Be("Obscure Combo Engine");
        result.Recommendations[0].Score.Should().BeGreaterThan(result.Recommendations[1].Score);
    }

    /// <summary>
    /// Verifies that top exemplar lookups return high-signal decks from enabled providers.
    /// </summary>
    [Fact]
    public async Task FindTopExemplarDecks_ReturnsProviderExemplars()
    {
        InMemoryRepository workspaces = new();
        FakeCorpusSignalProvider provider = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [provider]);

        TopExemplarDecksResult result = await service.FindTopExemplarDecksAsync(
            workspace.Id,
            limit: 1,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        provider.LastBudget.Should().NotBeNull();
        provider.LastBudget!.IncludeExemplarDecks.Should().BeTrue();
        result.ExemplarDecks.Should().ContainSingle().Which.Name.Should().Be("High Vote Tinybones");
    }

    /// <summary>
    /// Verifies that source listing exposes only configured corpus providers.
    /// </summary>
    [Fact]
    public void ListCorpusSources_OnlyIncludesConfiguredProviders()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusSourceStatusResult result = service.ListCorpusSources();

        CorpusSourceStatus source = result.Sources.Should().ContainSingle().Subject;
        source.Key.Should().Be("fake-corpus");
        source.Enabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that corpus budget replacements save a plan and attach provider evidence.
    /// </summary>
    [Fact]
    public async Task FindCorpusBudgetReplacements_CreatesPlanWithCorpusEvidence()
    {
        InMemoryRepository workspaces = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Corpus Budget",
            Cards = [ExpensiveRamp()]
        }, TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            archidektGateway: null,
            planRepository: plans,
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusBudgetReplacementResult result = await service.FindCorpusBudgetReplacementsAsync(
            workspace.Id,
            maxPrice: 5,
            minSavings: 1,
            limit: 5,
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.RemoveCard && operation.CardName == "Mana Crypt");
        result.Plan.Operations.Should().Contain(operation =>
            operation.Operation == DeckEditOperations.AddCard && operation.CardName == "Arcane Signet");
        workspaces.Workspaces[workspace.Id].Cards.Should().ContainSingle().Which.Name.Should().Be("Mana Crypt");
        (await plans.GetAsync(result.Plan.PlanId, TestContext.Current.CancellationToken)).Should().NotBeNull();

        CorpusRecommendation recommendation = result.Recommendations.Should()
            .ContainSingle()
            .Which;
        recommendation.CardName.Should().Be("Arcane Signet");
        recommendation.ReplaceCard.Should().Be("Mana Crypt");
        recommendation.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Arcane Signet"));
        recommendation.ReplaceCardScryfallUri.Should().EndWith(Uri.EscapeDataString("Mana Crypt"));
        recommendation.Evidence.Should().ContainSingle(evidence =>
            evidence.Source == "Fake corpus" && evidence.SignalType == CorpusSignalTypes.Budget);
    }

    /// <summary>
    /// Verifies that explain card returns direct evidence from enabled corpus providers.
    /// </summary>
    [Fact]
    public async Task ExplainCardCorpusSignal_ReturnsProviderEvidence()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusRecommendationResult result = await service.ExplainCardCorpusSignalAsync(
            workspace.Id,
            "Illness in the Ranks",
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        CorpusRecommendation recommendation = result.Recommendations.Should().ContainSingle().Which;
        recommendation.CardName.Should().Be("Illness in the Ranks");
        recommendation.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Illness in the Ranks"));
        recommendation.Evidence.Should().ContainSingle(evidence =>
            evidence.Source == "Fake corpus" && evidence.SignalType == CorpusSignalTypes.Novelty);
        result.Notes.Should().NotContain(note => note.Contains("No matching source evidence", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that explain card falls back to local metadata when providers have no direct evidence.
    /// </summary>
    [Fact]
    public async Task ExplainCardCorpusSignal_FallsBackToCardMetadataWhenNoEvidenceMatches()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CorpusWorkspace(), TestContext.Current.CancellationToken);
        DeckRecommendationService service = CreateRecommendationService(
            workspaces,
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusRecommendationResult result = await service.ExplainCardCorpusSignalAsync(
            workspace.Id,
            "Hero's Downfall",
            analysisDepth: "minimal",
            refresh: false,
            cancellationToken: TestContext.Current.CancellationToken);

        CorpusRecommendation recommendation = result.Recommendations.Should().ContainSingle().Which;
        recommendation.CardName.Should().Be("Hero's Downfall");
        recommendation.ScryfallUri.Should().EndWith(Uri.EscapeDataString("Hero's Downfall"));
        recommendation.Evidence.Should().BeEmpty();
        recommendation.Rationale.Should().Contain("local card metadata");
        result.Notes.Should().Contain(note => note.Contains("No matching source evidence", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a Commander workspace for corpus recommendation tests.
    /// </summary>
    private static DeckWorkspace CorpusWorkspace()
    {
        return new DeckWorkspace
        {
            Name = "Corpus",
            Format = "commander",
            Description =
                """
                MTG MCP Deck Intent
                Version: 1
                Commander: Tinybones, Trinket Thief
                Archetype: discard-control
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard
                {
                    Name = "Tinybones, Trinket Thief",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        OracleText = "Whenever an opponent discards a card, you draw a card.",
                        ColorIdentity = ["B"]
                    }
                },
                new DeckCard
                {
                    Name = "Swamp",
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land",
                        OracleText = "{T}: Add {B}.",
                        ProducedMana = ["B"]
                    }
                }
            ]
        };
    }

    /// <summary>
    /// Creates a Vihaan workspace whose local archetype is intentionally less specific than the user goal.
    /// </summary>
    private static DeckWorkspace VihaanWorkspace()
    {
        return new DeckWorkspace
        {
            Name = "Vihaan",
            Format = "commander",
            Description =
                """
                MTG MCP Deck Intent
                Version: 1
                Commander: Vihaan, Goldwaker
                Archetype: engines
                End MTG MCP Deck Intent
                """,
            Cards =
            [
                new DeckCard
                {
                    Name = "Vihaan, Goldwaker",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature",
                        OracleText = "Treasures you control are creatures.",
                        ColorIdentity = ["R", "W", "B"]
                    }
                }
            ]
        };
    }

    /// <summary>
    /// Creates a cache key for corpus cache tests.
    /// </summary>
    private static CorpusCacheKey CacheKey(string query, string adapterVersion = "1")
    {
        return new CorpusCacheKey
        {
            Source = "source",
            Endpoint = "endpoint",
            Query = query,
            AdapterVersion = adapterVersion,
            Budget = "balanced"
        };
    }

    /// <summary>
    /// Provides deterministic corpus source behavior for recommendation tests.
    /// </summary>
    private sealed class FakeCorpusSignalProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Stores the fake source key.
        /// </summary>
        private readonly string key;

        /// <summary>
        /// Stores the fake source display name.
        /// </summary>
        private readonly string sourceName;

        /// <summary>
        /// Stores the primary fake card returned by this source.
        /// </summary>
        private readonly string primaryCard;

        /// <summary>
        /// Stores the fake source kind.
        /// </summary>
        private readonly string kind;

        /// <summary>
        /// Creates a fake corpus provider.
        /// </summary>
        public FakeCorpusSignalProvider(
            string key = "fake-corpus",
            string sourceName = "Fake corpus",
            string primaryCard = "Arcane Signet",
            string kind = "commander-aggregate")
        {
            this.key = key;
            this.sourceName = sourceName;
            this.primaryCard = primaryCard;
            this.kind = kind;
        }

        /// <summary>
        /// Gets the last query received by the fake provider.
        /// </summary>
        public CorpusSignalQuery? LastQuery { get; private set; }

        /// <summary>
        /// Gets the last budget received by the fake provider.
        /// </summary>
        public RecommendationAnalysisBudget? LastBudget { get; private set; }

        /// <summary>
        /// Gets fake source status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = key,
                Name = sourceName,
                Kind = kind,
                Enabled = true,
                StableApi = true,
                Status = CorpusSourceStatuses.Available,
                Uri = $"https://example.test/{key}"
            };
        }

        /// <summary>
        /// Gets fake corpus signals.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastBudget = budget;
            CorpusSignalReport report = new()
            {
                Sources = [GetStatus()],
                Signals =
                [
                    new CardCorpusSignal
                    {
                        CardName = primaryCard,
                        Source = sourceName,
                        SignalType = CorpusSignalTypes.Budget,
                        Section = "top-cards",
                        Score = 0.88,
                        InclusionRate = 0.50,
                        DeckCount = 300,
                        EligibleDeckCount = 600,
                        Price = 1.00m,
                        EdhrecRank = 5,
                        Uri = $"https://example.test/cards/{primaryCard.Replace(' ', '-').ToLowerInvariant()}",
                        Rationale = "Cheap ramp replacement appears in budget Tinybones lists."
                    },
                    new CardCorpusSignal
                    {
                        CardName = "Illness in the Ranks",
                        Source = sourceName,
                        SignalType = CorpusSignalTypes.Novelty,
                        Section = "spicy-tech",
                        Score = 0.92,
                        InclusionRate = 0.03,
                        DeckCount = 12,
                        Price = 1.00m,
                        EdhrecRank = 8_000,
                        Uri = "https://example.test/cards/illness",
                        Rationale = "Low-play token hate appears in high-vote Tinybones lists."
                    },
                    new CardCorpusSignal
                    {
                        CardName = "Lightning Greaves",
                        Source = sourceName,
                        SignalType = CorpusSignalTypes.Inclusion,
                        Section = "top-cards",
                        Score = 0.74,
                        InclusionRate = 0.42,
                        DeckCount = 200,
                        Price = 6.00m,
                        EdhrecRank = 40,
                        Uri = "https://example.test/cards/greaves",
                        Rationale = "Popular protection piece appears frequently."
                    }
                ],
                Notes = ["Fake corpus queried."]
            };

            if (budget.IncludeExemplarDecks)
            {
                report.ExemplarDecks =
                [
                    new DeckExemplarSignal
                    {
                        Name = "High Vote Tinybones",
                        Source = sourceName,
                        Commander = query.Commander,
                        PopularityMetric = "votes",
                        PopularityValue = 42,
                        Weight = 0.95,
                        Uri = "https://example.test/decks/high-vote"
                    },
                    new DeckExemplarSignal
                    {
                        Name = "Budget Tinybones",
                        Source = sourceName,
                        Commander = query.Commander,
                        PopularityMetric = "views",
                        PopularityValue = 20,
                        Weight = 0.60,
                        Uri = "https://example.test/decks/budget"
                    }
                ];
            }

            return Task.FromResult(report);
        }
    }

    /// <summary>
    /// Provides commander aggregate rows only for the treasure theme.
    /// </summary>
    private sealed class ThemeAwareCommanderCorpusProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Gets the queries observed by the fake provider.
        /// </summary>
        public List<CorpusSignalQuery> Queries { get; } = [];

        /// <summary>
        /// Gets fake EDHREC status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = "edhrec",
                Name = "EDHREC",
                Kind = "commander-aggregate",
                Enabled = true,
                StableApi = false,
                Status = CorpusSourceStatuses.Available,
                Uri = "https://edhrec.test/"
            };
        }

        /// <summary>
        /// Gets theme-sensitive commander aggregate evidence.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            Queries.Add(new CorpusSignalQuery
            {
                WorkspaceId = query.WorkspaceId,
                Format = query.Format,
                Commander = query.Commander,
                Theme = query.Theme,
                Goal = query.Goal,
                ExistingCards = [.. query.ExistingCards],
                MaxPrice = query.MaxPrice,
                Refresh = query.Refresh
            });

            CorpusSignalReport report = new() { Sources = [GetStatus()] };
            if (string.IsNullOrWhiteSpace(query.Theme))
            {
                report.Signals.Add(new CardCorpusSignal
                {
                    CardName = "Prosperous Bandit",
                    Source = "EDHREC",
                    SignalType = CorpusSignalTypes.Inclusion,
                    Section = "treasure",
                    Score = 0.80,
                    DeckCount = 200
                });
                return Task.FromResult(report);
            }

            if (!query.Theme.Equals("treasure", StringComparison.OrdinalIgnoreCase))
            {
                report.Notes.Add($"unsupported-theme: EDHREC did not expose theme slug '{query.Theme}' for this commander.");
                return Task.FromResult(report);
            }

            report.Signals.Add(new CardCorpusSignal
            {
                CardName = "Prosperous Bandit",
                Source = "EDHREC",
                SignalType = CorpusSignalTypes.Inclusion,
                Section = "treasure",
                Score = 0.90,
                InclusionRate = 0.30,
                DeckCount = 300,
                EligibleDeckCount = 1_000,
                Uri = "https://edhrec.test/commanders/vihaan-goldwaker/treasure",
                Rationale = "Appears in treasure-focused Vihaan aggregate rows."
            });
            return Task.FromResult(report);
        }
    }

    /// <summary>
    /// Provides one on-plan lower-known card and one stronger off-plan combo signal.
    /// </summary>
    private sealed class PlanFitCorpusSignalProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Gets fake source status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = "plan-fit",
                Name = "Plan Fit",
                Enabled = true,
                StableApi = true,
                Status = CorpusSourceStatuses.Available,
            };
        }

        /// <summary>
        /// Gets deterministic plan-fit and combo evidence rows.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CorpusSignalReport
            {
                Sources = [GetStatus()],
                Signals =
                [
                    new CardCorpusSignal
                    {
                        CardName = "Hidden Token Maker",
                        Source = "Plan Fit",
                        SignalType = CorpusSignalTypes.Novelty,
                        Score = 0.70,
                        Price = 0.75m,
                        EdhrecRank = 9_000,
                        Rationale = "Low-known token maker supports the requested token plan."
                    },
                    new CardCorpusSignal
                    {
                        CardName = "Obscure Combo Engine",
                        Source = "Plan Fit",
                        SignalType = CorpusSignalTypes.Combo,
                        Score = 1.00,
                        Price = 0.50m,
                        EdhrecRank = 9_500,
                        Rationale = "High signal combo piece is unrelated to the requested token plan."
                    }
                ],
            });
        }
    }

    /// <summary>
    /// Provides commander aggregate counts for commander candidate discovery tests.
    /// </summary>
    private sealed class CommanderCandidateCorpusProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Stores eligible deck counts by commander name.
        /// </summary>
        private readonly IReadOnlyDictionary<string, int> counts;

        /// <summary>
        /// Stores commander names that should fail lookup.
        /// </summary>
        private readonly HashSet<string> failingCommanders;

        /// <summary>
        /// Creates a candidate corpus provider.
        /// </summary>
        public CommanderCandidateCorpusProvider(
            IReadOnlyDictionary<string, int> counts,
            IReadOnlyList<string> failingCommanders)
        {
            this.counts = counts;
            this.failingCommanders = failingCommanders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets queries observed by the fake provider.
        /// </summary>
        public List<CorpusSignalQuery> Queries { get; } = [];

        /// <summary>
        /// Gets fake EDHREC source status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = "edhrec",
                Name = "EDHREC",
                Kind = "commander-aggregate",
                Enabled = true,
                StableApi = false,
                Status = CorpusSourceStatuses.Available,
                Uri = "https://edhrec.com/"
            };
        }

        /// <summary>
        /// Gets fake commander aggregate evidence.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            string commander = query.Commander ?? "";
            if (failingCommanders.Contains(commander))
            {
                throw new InvalidOperationException("EDHREC shape changed.");
            }

            if (!counts.TryGetValue(commander, out int count))
            {
                return Task.FromResult(new CorpusSignalReport { Sources = [GetStatus()] });
            }

            return Task.FromResult(new CorpusSignalReport
            {
                Sources = [GetStatus()],
                Signals =
                [
                    new CardCorpusSignal
                    {
                        CardName = "Sol Ring",
                        Source = "EDHREC",
                        SignalType = CorpusSignalTypes.Inclusion,
                        Section = "top-cards",
                        Score = 1,
                        DeckCount = Math.Max(1, count / 2),
                        EligibleDeckCount = count,
                        Uri = $"https://edhrec.test/commanders/{Uri.EscapeDataString(commander)}",
                    }
                ]
            });
        }
    }

    /// <summary>
    /// Provides a corpus source that fails during lookup.
    /// </summary>
    private sealed class FailingCorpusSignalProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Gets fake source status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = "failing-corpus",
                Name = "Failing corpus",
                Kind = "test",
                Enabled = true,
                StableApi = true,
                Status = CorpusSourceStatuses.Available
            };
        }

        /// <summary>
        /// Fails the lookup to exercise source isolation.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("changed JSON shape");
        }
    }

    /// <summary>
    /// Provides a corpus source that simulates exhausting its source budget.
    /// </summary>
    private sealed class TimeoutCorpusSignalProvider : ICorpusSignalProvider
    {
        /// <summary>
        /// Gets fake timeout source status.
        /// </summary>
        public CorpusSourceStatus GetStatus()
        {
            return new CorpusSourceStatus
            {
                Key = "timeout-corpus",
                Name = "Timeout corpus",
                Kind = "test",
                Enabled = true,
                StableApi = true,
                Status = CorpusSourceStatuses.Available
            };
        }

        /// <summary>
        /// Throws cancellation to exercise source timeout isolation.
        /// </summary>
        public Task<CorpusSignalReport> GetSignalsAsync(
            CorpusSignalQuery query,
            RecommendationAnalysisBudget budget,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }
}
