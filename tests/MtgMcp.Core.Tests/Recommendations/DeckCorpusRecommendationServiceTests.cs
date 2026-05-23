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
        result.Notes.Should().Contain(note => note.Contains("No API-backed corpus providers", StringComparison.OrdinalIgnoreCase));
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
            && !row.AlreadyInDeck);
        result.Notes.Should().NotContain(note => note.Contains("Failing corpus", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that source listing exposes enabled and planned corpus providers.
    /// </summary>
    [Fact]
    public void ListCorpusSources_IncludesEnabledAndPlannedProviders()
    {
        DeckRecommendationService service = CreateRecommendationService(
            new InMemoryRepository(),
            new FakeCardCatalog(),
            corpusSignalProviders: [new FakeCorpusSignalProvider()]);

        CorpusSourceStatusResult result = service.ListCorpusSources();

        result.Sources.Should().Contain(source => source.Key == "fake-corpus" && source.Enabled);
        result.Sources.Should().Contain(source => source.Key == "edhrec-commander" && !source.Enabled);
        result.Sources.Should().Contain(source => source.Key == "archidekt-exemplars" && source.UnofficialApi);
        result.Sources.Should().Contain(source => source.Key == "mtgstocks" && source.ApiType == CorpusSourceApiTypes.Unsupported);
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
        recommendation.Evidence.Should().ContainSingle(evidence =>
            evidence.Source == "Fake corpus" && evidence.SignalType == CorpusSignalTypes.Novelty);
        result.Notes.Should().NotContain(note => note.Contains("No matching corpus evidence", StringComparison.OrdinalIgnoreCase));
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
        recommendation.Evidence.Should().BeEmpty();
        recommendation.Rationale.Should().Contain("local card metadata");
        result.Notes.Should().Contain(note => note.Contains("No matching corpus evidence", StringComparison.OrdinalIgnoreCase));
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
                Key = "fake-corpus",
                Name = "Fake corpus",
                Kind = "test",
                Enabled = true,
                StableApi = true,
                Status = CorpusSourceStatuses.Available,
                Uri = "https://example.test/corpus"
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
                        CardName = "Arcane Signet",
                        Source = "Fake corpus",
                        SignalType = CorpusSignalTypes.Budget,
                        Score = 0.88,
                        InclusionRate = 0.50,
                        DeckCount = 300,
                        Price = 1.00m,
                        EdhrecRank = 5,
                        Uri = "https://example.test/cards/arcane-signet",
                        Rationale = "Cheap ramp replacement appears in budget Tinybones lists."
                    },
                    new CardCorpusSignal
                    {
                        CardName = "Illness in the Ranks",
                        Source = "Fake corpus",
                        SignalType = CorpusSignalTypes.Novelty,
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
                        Source = "Fake corpus",
                        SignalType = CorpusSignalTypes.Inclusion,
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
                        Source = "Fake corpus",
                        Commander = query.Commander,
                        PopularityMetric = "votes",
                        PopularityValue = 42,
                        Weight = 0.95,
                        Uri = "https://example.test/decks/high-vote"
                    },
                    new DeckExemplarSignal
                    {
                        Name = "Budget Tinybones",
                        Source = "Fake corpus",
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
