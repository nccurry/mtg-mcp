using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the saved-deck, installed-card-data, and pure on-curve composition path.
/// </summary>
public sealed class DeckOnCurveEstimateCoordinatorTests
{
    /// <summary>
    /// Verifies one complete cached estimate returns source facts, caller coverage, and replay details.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithInstalledFacts_ReturnsSampledEvidenceWithoutHttp()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store, includeKnownEmptyLand: true);
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        int requestCount = handler.RequestCount;

        DeckOnCurveEstimateResult result = RequireSuccess(await coordinator.EstimateAsync(
            Request(deck),
            TestContext.Current.CancellationToken));
        DeckOnCurveEstimateResult generatedSeedResult = RequireSuccess(await coordinator.EstimateAsync(
            Request(deck) with { Seed = null },
            TestContext.Current.CancellationToken));

        Assert.Equal("sampled-on-curve-estimate", result.Kind);
        Assert.Equal(deck.DeckId, result.DeckId);
        Assert.Equal(deck.Revision, result.DeckRevision);
        Assert.Equal("Fixture Growth", result.Target.Name);
        Assert.Equal("{1}{G}", result.Target.ManaCost);
        Assert.Equal("installed-scryfall-card-data", result.SourceEvidence.Source);
        Assert.NotEqual(Guid.Empty, result.SourceEvidence.InstalledCardDataGenerationId);
        Assert.Matches("^[0-9a-f]{64}$", result.SourceEvidence.EvidenceChecksum);
        Assert.Equal(4, result.SourceFacts.Count);
        Assert.Equal("missing", Assert.Single(
            result.SourceFacts,
            fact => fact.PrintingId == DeckOnCurveSourceFixture.TargetPrintingId).ProducedMana.Kind);
        DeckOnCurveCandidateLand forest = Assert.Single(result.LandRuleCoverage.CandidateLands);
        Assert.Equal(DeckOnCurveSourceFixture.GreenLandPrintingId, forest.PrintingId);
        Assert.Equal("one-mana-same-turn", forest.Rule);
        Assert.Equal("values", forest.ProducedMana.Kind);
        Assert.Equal(["G"], forest.ProducedMana.Colors);
        Assert.Equal(DeckOnCurveSourceFixture.KnownEmptyLandPrintingId, Assert.Single(
            result.LandRuleCoverage.KnownEmptyLands).PrintingId);
        Assert.Equal(DeckOnCurveSourceFixture.MultiFacePrintingId, Assert.Single(
            result.LandRuleCoverage.MultiFaceCards).PrintingId);
        Assert.Equal(100, result.SampleCount);
        Assert.Equal(100, result.CompletedTrialCount);
        Assert.Equal("0123456789abcdef", result.Seed);
        Assert.Matches("^[0-9a-f]{16}$", generatedSeedResult.Seed);
        Assert.Equal("wilson-95", result.Interval.Method);
        Assert.Equal(5, result.FailureCounts.Count);
        Assert.InRange(result.Traces.Count, 1, 2);
        Assert.Contains(result.Warnings, warning => warning.Contains("sampled estimate", StringComparison.Ordinal));
        Assert.Equal(requestCount, handler.RequestCount);
        Assert.Equal(deck.Revision, RequireSuccess(await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken)).Revision);
    }

    /// <summary>
    /// Verifies a changed deck revision stops before source lookup or calculation.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithChangedRevision_ReturnsConflictBeforeSourceRead()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store, includeKnownEmptyLand: false);
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck) with { ExpectedRevision = deck.Revision + 1 },
            TestContext.Current.CancellationToken);

        OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
        Assert.Equal("deck-revision-conflict", conflict.ReasonCode);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Verifies a missing installed card fact returns unavailable without provider fallback.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithNoInstalledCardData_ReturnsUnavailableWithoutHttp()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store, includeKnownEmptyLand: false);
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck),
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("on-curve-card-fact-unavailable", unavailable.ReasonCode);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Verifies a single-faced land with null produced-mana stays unavailable rather than becoming empty.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithNullLandProducedMana_ReturnsUnavailable()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Null Land Fixture",
                Entries:
                [
                    new DeckEntryDraft(1, "Fixture Growth", PrintingId: DeckOnCurveSourceFixture.TargetPrintingId, Zone: "main"),
                    new DeckEntryDraft(7, "Fixture Forest", PrintingId: DeckOnCurveSourceFixture.GreenLandPrintingId, Zone: "main"),
                    new DeckEntryDraft(1, "Fixture Null Land", PrintingId: DeckOnCurveSourceFixture.NullLandPrintingId, Zone: "main"),
                ]),
            TestContext.Current.CancellationToken));
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        int requestCount = handler.RequestCount;

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck),
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("on-curve-land-produced-mana-unavailable", unavailable.ReasonCode);
        Assert.Equal(requestCount, handler.RequestCount);
    }

    /// <summary>
    /// Verifies a single-faced land with a missing produced-mana field stays unavailable rather than becoming empty.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithMissingLandProducedMana_ReturnsUnavailable()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Missing Land Fixture",
                Entries:
                [
                    new DeckEntryDraft(1, "Fixture Growth", PrintingId: DeckOnCurveSourceFixture.TargetPrintingId, Zone: "main"),
                    new DeckEntryDraft(7, "Fixture Forest", PrintingId: DeckOnCurveSourceFixture.GreenLandPrintingId, Zone: "main"),
                    new DeckEntryDraft(1, "Fixture Missing Land", PrintingId: DeckOnCurveSourceFixture.MissingLandPrintingId, Zone: "main"),
                ]),
            TestContext.Current.CancellationToken));
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        int requestCount = handler.RequestCount;

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck),
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("on-curve-land-produced-mana-unavailable", unavailable.ReasonCode);
        Assert.Equal(requestCount, handler.RequestCount);
    }

    /// <summary>
    /// Verifies a missing caller rule names the eligible entry that still needs a rule.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithMissingLandRule_NamesTheEligibleEntry()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store, includeKnownEmptyLand: false);
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        DeckEntry forest = Assert.Single(
            deck.Entries,
            entry => entry.PrintingId == DeckOnCurveSourceFixture.GreenLandPrintingId);

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck) with { LandRules = [] },
            TestContext.Current.CancellationToken);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-on-curve-request", invalid.ReasonCode);
        Assert.Contains(forest.EntryId.ToString("D"), invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a known-empty land cannot receive a caller rule.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithKnownEmptyLandRule_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store, includeKnownEmptyLand: true);
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        DeckEntry forest = Assert.Single(deck.Entries, entry => entry.PrintingId == DeckOnCurveSourceFixture.GreenLandPrintingId);
        DeckEntry emptyLand = Assert.Single(
            deck.Entries,
            entry => entry.PrintingId == DeckOnCurveSourceFixture.KnownEmptyLandPrintingId);

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            Request(deck) with
            {
                LandRules =
                [
                    new DeckOnCurveLandRuleToolInput(forest.EntryId, "one-mana-same-turn"),
                    new DeckOnCurveLandRuleToolInput(emptyLand.EntryId, "not-modeled"),
                ],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("invalid-on-curve-request", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a multi-face target is outside the version-one target model.
    /// </summary>
    [Fact]
    public async Task EstimateAsync_WithMultiFaceTarget_ReturnsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        DeckOnCurveSourceFixture.DeckOnCurveSourceHandler handler = DeckOnCurveSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckOnCurveSourceFixture.CreateService(temporary.Path, handler);
        RequireSuccess(await scryfall.SyncCorpusAsync(cancellationToken: TestContext.Current.CancellationToken));
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Multi Face Fixture",
                Entries:
                [
                    new DeckEntryDraft(7, "Fixture Front // Fixture Back", PrintingId: DeckOnCurveSourceFixture.MultiFacePrintingId, Zone: "main"),
                ]),
            TestContext.Current.CancellationToken));
        DeckOnCurveEstimateCoordinator coordinator = new(store, scryfall);
        DeckEntry target = Assert.Single(deck.Entries);

        OperationResult<DeckOnCurveEstimateResult> result = await coordinator.EstimateAsync(
            new DeckOnCurveEstimateToolRequest(
                deck.DeckId,
                deck.Revision,
                target.EntryId,
                1,
                [],
                100,
                true,
                "0123456789abcdef"),
            TestContext.Current.CancellationToken);

        Assert.Equal("unsupported-on-curve-multiface-target", Assert.IsType<OperationUnsupported>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Creates one valid version-one request for the saved deck's target and green land.
    /// </summary>
    private static DeckOnCurveEstimateToolRequest Request(DeckDocument deck)
    {
        DeckEntry target = Assert.Single(deck.Entries, entry => entry.PrintingId == DeckOnCurveSourceFixture.TargetPrintingId);
        DeckEntry forest = Assert.Single(deck.Entries, entry => entry.PrintingId == DeckOnCurveSourceFixture.GreenLandPrintingId);
        return new DeckOnCurveEstimateToolRequest(
            deck.DeckId,
            deck.Revision,
            target.EntryId,
            2,
            [new DeckOnCurveLandRuleToolInput(forest.EntryId, "one-mana-same-turn")],
            100,
            true,
            "0123456789abcdef");
    }

    /// <summary>
    /// Creates one saved mainboard with exact printing identities for the source fixture.
    /// </summary>
    private static async Task<DeckDocument> CreateDeckAsync(
        SqliteDeckStore store,
        bool includeKnownEmptyLand)
    {
        List<DeckEntryDraft> entries =
        [
            new DeckEntryDraft(1, "Fixture Growth", PrintingId: DeckOnCurveSourceFixture.TargetPrintingId, Zone: "main"),
            new DeckEntryDraft(7, "Fixture Forest", PrintingId: DeckOnCurveSourceFixture.GreenLandPrintingId, Zone: "main"),
            new DeckEntryDraft(1, "Fixture Front // Fixture Back", PrintingId: DeckOnCurveSourceFixture.MultiFacePrintingId, Zone: "main"),
        ];
        if (includeKnownEmptyLand)
        {
            entries.Add(new DeckEntryDraft(
                1,
                "Fixture Empty Land",
                PrintingId: DeckOnCurveSourceFixture.KnownEmptyLandPrintingId,
                Zone: "main"));
        }

        return RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("On Curve Fixture", Entries: entries),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Extracts one successful result or fails the current assertion.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        Assert.True(result.Value is OperationSuccess<T>, result.Value.ToString());
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
