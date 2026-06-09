using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains tests for deck performance analysis.
/// </summary>
public sealed class DeckPerformanceTests
{
    /// <summary>
    /// Verifies that performance analysis returns scenario and confidence data.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_ReturnsScenarioAndConfidenceData()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 18, islands: 18, utility: 29);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 500,
            maxTurn: 6,
            seed: 2026,
            includeMulligans: true,
            CancellationToken.None);

        analysis.Simulations.Should().Be(500);
        analysis.ModelLabel.Should().Be("strict-sequencing-model");
        analysis.OpeningHands.SevenCardKeepRate.Should().BeGreaterThan(0.50);
        analysis.TurnProbabilities.Should().Contain(row =>
            row.Name == "land-drop-by-turn"
            && row.Turn == 3
            && row.SampleSize == 500
            && row.HighConfidenceInterval >= row.LowConfidenceInterval);
        analysis.Castability.ColorSourceReliability.Should().Contain(row => row.Name == "source-W-by-turn");
        analysis.Commander.CastByTurn.Single(row => row.Turn == 4).Probability.Should().BeGreaterThan(0);
        analysis.Scenarios.Should().Contain(row => row.Name == "commander-by-turn-4");
        analysis.Scenarios.Should().Contain(row => row.Name == "stranded-high-mana-risk-by-max-turn");
        analysis.Assumptions.Should().Contain(note => note.Contains("not simulated", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that performance analysis warns when a Commander active deck excludes sideboard cards.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_WarnsWhenCommanderActiveDeckIsPartial()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 18, islands: 18, utility: 18);
        deck.Categories.Add(new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false });
        deck.Cards.Add(Card("Sideboard Spell", 11, DeckDefaults.Sideboard, "Sorcery", "{1}", 1, "Scry 1.", []));
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 100,
            maxTurn: 3,
            seed: 77,
            includeMulligans: true,
            CancellationToken.None);

        analysis.DeckSize.Should().Be(89);
        analysis.Warnings.Should().Contain(warning =>
            warning.Contains("89 included cards", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("Sideboard", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("not sampled", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Inga and Esika commander-specific assumptions are surfaced by both simulator models.
    /// </summary>
    [Fact]
    public async Task SimulationOutputs_SurfaceIngaAndEsikaAssumptions()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateIngaAndEsikaPerformanceDeck();
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis performance = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            SimulationProfileIds.Neutral,
            simulations: 100,
            maxTurn: 5,
            seed: 101,
            includeMulligans: true,
            CancellationToken.None);
        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            deck.Id,
            targetTurn: 5,
            simulations: 100,
            seed: 101,
            mulligan: true,
            CancellationToken.None);

        performance.Assumptions.Should().Contain(note =>
            note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase)
            && note.Contains("creature mana", StringComparison.OrdinalIgnoreCase));
        goldfish.Notes.Should().Contain(note =>
            note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase)
            && note.Contains("creature spells", StringComparison.OrdinalIgnoreCase));
        goldfish.WinEstimate.Notes.Should().Contain(note =>
            note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase));
        goldfish.TurnSummaries.SelectMany(summary => summary.Notes).Should().Contain(note =>
            note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that plan performance comparison applies preview edits in memory.
    /// </summary>
    [Fact]
    public async Task ComparePlanPerformance_UsesPreviewOperationsAndReturnsDeltas()
    {
        InMemoryRepository repository = new();
        InMemoryPlanRepository plans = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 10, islands: 10, utility: 45);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckEditPlan plan = new()
        {
            WorkspaceId = deck.Id,
            Name = "Raise land count",
            Kind = "performance",
            Operations =
            [
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.SetCardQuantity,
                    CardName = "Plains",
                    Category = DeckDefaults.Mainboard,
                    Quantity = 18,
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.SetCardQuantity,
                    CardName = "Island",
                    Category = DeckDefaults.Mainboard,
                    Quantity = 18,
                },
                new DeckEditOperation
                {
                    Operation = DeckEditOperations.SetCardQuantity,
                    CardName = "Utility Spell",
                    Category = DeckDefaults.Mainboard,
                    Quantity = 29,
                },
            ],
        };
        await plans.SaveAsync(plan, CancellationToken.None);
        DeckSimulationService service = new(
            repository,
            new EmptyCardCatalog(),
            planRepository: plans);

        DeckPerformanceComparison comparison = await service.ComparePlanPerformanceAsync(
            plan.PlanId,
            "commander-default",
            simulations: 500,
            maxTurn: 5,
            seed: 9001,
            CancellationToken.None);

        comparison.PlanId.Should().Be(plan.PlanId);
        comparison.Before.DeckSize.Should().Be(100);
        comparison.After.DeckSize.Should().Be(100);
        comparison.Deltas.Should().Contain(delta =>
            delta.Metric == "seven-card-keep-rate"
            && delta.After > delta.Before
            && delta.Delta > 0);
        comparison.Deltas.Single(delta => delta.Metric == "commander-by-target-turn")
            .BeforeLowConfidenceInterval.Should().NotBeNull();
        comparison.Deltas.Single(delta => delta.Metric == "commander-by-target-turn")
            .ConfidenceIntervalsOverlap.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the service delegates to the pure analyzer without changing deterministic output.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_MatchesPureAnalyzerForFixedSeed()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 18, islands: 18, utility: 29);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis viaService = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 200,
            maxTurn: 5,
            seed: 42,
            includeMulligans: true,
            CancellationToken.None);
        DeckPerformanceAnalysis direct = DeckPerformanceAnalyzer.Analyze(
            deck,
            "commander-default",
            simulations: 200,
            maxTurn: 5,
            seed: 42,
            includeMulligans: true,
            CancellationToken.None);

        viaService.OpeningHands.SevenCardKeepRate.Should().Be(direct.OpeningHands.SevenCardKeepRate);
        viaService.Commander.CastByTurn.Single(row => row.Turn == 4).Probability
            .Should().Be(direct.Commander.CastByTurn.Single(row => row.Turn == 4).Probability);
        viaService.Scenarios.Single(row => row.Name == "all-colors-by-turn-3").SuccessRate
            .Should().Be(direct.Scenarios.Single(row => row.Name == "all-colors-by-turn-3").SuccessRate);
    }

    /// <summary>
    /// Verifies that repeated colored pips require repeated colored sources.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_RequiresRepeatedColoredSourcesForRepeatedPips()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateDoubleBlueDeck();
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 1_000,
            maxTurn: 2,
            seed: 7,
            includeMulligans: false,
            CancellationToken.None);

        analysis.TurnProbabilities.Single(row =>
                row.Name == "interaction-held-up-by-turn" && row.Turn == 2)
            .Probability.Should()
            .Be(0);
    }

    /// <summary>
    /// Verifies that repeated tapped lands are modeled as distinct permanents.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_TreatsRepeatedTappedLandsAsDistinctPermanents()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateTappedIslandDeck();
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 100,
            maxTurn: 2,
            seed: 11,
            includeMulligans: false,
            CancellationToken.None);

        analysis.Castability.ColorSourceReliability.Single(row =>
                row.Name == "source-U-by-turn" && row.Turn == 2)
            .Probability.Should()
            .Be(1);
    }

    /// <summary>
    /// Verifies that hybrid mana can be satisfied by either available color.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_AllowsHybridCostsFromEitherColor()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateOneColorInteractionDeck(
            landName: "Island",
            producedMana: ["U"],
            spellName: "Hybrid Answer",
            spellCost: "{W/U}",
            spellColorIdentity: ["W", "U"],
            spellText: "Counter target spell.");
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 300,
            maxTurn: 1,
            seed: 33,
            includeMulligans: false,
            CancellationToken.None);

        analysis.TurnProbabilities.Single(row =>
                row.Name == "interaction-held-up-by-turn" && row.Turn == 1)
            .Probability.Should()
            .BeGreaterThan(0.25);
    }

    /// <summary>
    /// Verifies that a single flexible source plus generic mana cannot pay two colored requirements.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_DoesNotReuseFlexibleSourcesAcrossColoredRequirements()
    {
        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            CreateSingleFlexibleSourceDeck(),
            simulations: 1_000,
            maxTurn: 2,
            seed: 36,
            includeMulligans: false);

        analysis.TurnProbabilities.Single(row =>
                row.Name == "interaction-held-up-by-turn" && row.Turn == 2)
            .Probability.Should()
            .Be(0);
    }

    /// <summary>
    /// Verifies that X costs do not add phantom colored requirements.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_IgnoresXCostForColorRequirements()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateOneColorInteractionDeck(
            landName: "Forest",
            producedMana: ["G"],
            spellName: "Flexible Hydra",
            spellCost: "{X}{G}",
            spellColorIdentity: ["G"],
            spellText: "Destroy target artifact.");
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 300,
            maxTurn: 1,
            seed: 34,
            includeMulligans: false,
            CancellationToken.None);

        analysis.TurnProbabilities.Single(row =>
                row.Name == "interaction-held-up-by-turn" && row.Turn == 1)
            .Probability.Should()
            .BeGreaterThan(0.25);
    }

    /// <summary>
    /// Verifies that Phyrexian mana can be modeled as payable without the named color.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_AllowsPhyrexianCostsWithoutColorSource()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreateOneColorInteractionDeck(
            landName: "Wastes",
            producedMana: [],
            spellName: "Phyrexian Shot",
            spellCost: "{R/P}",
            spellColorIdentity: ["R"],
            spellText: "Phyrexian Shot deals damage to target creature.");
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 300,
            maxTurn: 1,
            seed: 35,
            includeMulligans: false,
            CancellationToken.None);

        analysis.TurnProbabilities.Single(row =>
                row.Name == "interaction-held-up-by-turn" && row.Turn == 1)
            .Probability.Should()
            .BeGreaterThan(0.25);
    }

    /// <summary>
    /// Verifies that deck intent can set simulation profile and tighter scenario target defaults.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_UsesDeckIntentForScenarioDefaults()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 18, islands: 18, utility: 29);
        deck.Description = DeckIntentText.UpsertDescription(
            deck.Description,
            """
            MTG MCP Deck Intent
            Version: 2
            Power Target: strong bracket 3
            Simulation Profile: combo
            End MTG MCP Deck Intent
            """);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "auto",
            simulations: 200,
            maxTurn: 6,
            seed: 99,
            includeMulligans: true,
            CancellationToken.None);

        analysis.Profile.Should().Be(SimulationProfileIds.Combo);
        analysis.ProfileResolution.Source.Should().Be("deck-intent");
        analysis.Scenarios.Single(row => row.Name == "commander-by-turn-3").TargetTurn.Should().Be(3);
        analysis.Scenarios.Single(row => row.Name == "all-colors-by-turn-3").TargetTurn.Should().Be(2);
        analysis.Assumptions.Should().Contain(note => note.Contains("Saved deck intent", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that performance analysis honors Background-first command-zone sequencing intent.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_SequencesBackgroundBeforeDelayedCommander()
    {
        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            CreateBackgroundFirstPerformanceDeck(),
            simulations: 300,
            maxTurn: 7,
            seed: 73,
            includeMulligans: false);

        analysis.CommandZone.CommanderNames.Should().ContainSingle("Baeloth Barrityl, Entertainer");
        analysis.CommandZone.BackgroundNames.Should().ContainSingle("Raised by Giants");
        analysis.CommandZone.CommandZoneNames.Should().Equal("Raised by Giants", "Baeloth Barrityl, Entertainer");
        analysis.Commander.CastByTurn.Single(row => row.Turn == 4).Probability.Should().Be(0);
        analysis.CommandZone.BackgroundCastByTurn.Single(row => row.Turn == 7).Probability.Should().BeGreaterThan(0);
        analysis.CommandZone.CommanderWithBackgroundOnlineByTurn.Single(row => row.Turn == 7)
            .Probability.Should().BeGreaterThan(0);
        analysis.CommandZone.AverageBackgroundCastTurn.Should().NotBeNull();
        analysis.CommandZone.AverageCommanderCastTurn.Should().NotBeNull();
        analysis.CommandZone.AverageBackgroundCastTurn.Should()
            .BeLessThan(analysis.CommandZone.AverageCommanderCastTurn!.Value);
        analysis.TurnProbabilities.Should().Contain(row =>
            row.Name == "background-cast-by-turn" && row.Turn == 7);
        analysis.TurnProbabilities.Should().Contain(row =>
            row.Name == "commander-with-background-online-by-turn" && row.Turn == 7);
    }

    /// <summary>
    /// Verifies that scenarios expose observed failure-driver counts.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_ReturnsScenarioFailureDriverCounts()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 4, islands: 4, utility: 61);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());

        DeckPerformanceAnalysis analysis = await service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 200,
            maxTurn: 4,
            seed: 101,
            includeMulligans: true,
            CancellationToken.None);

        ScenarioPerformance scenario = analysis.Scenarios.Single(row => row.Name == "commander-by-turn-4");
        scenario.FailureDriverCounts.Should().NotBeEmpty();
        scenario.FailureDriverCounts.Values.Should().Contain(count => count > 0);
    }

    /// <summary>
    /// Verifies that generic finishers are not counted as combo assembly pieces.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_DoesNotTreatGenericWinconsAsComboPieces()
    {
        DeckWorkspace deck = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Wincons Are Not Combos",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Wincons, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Black Commander", 1, DeckRoles.Commander, "Legendary Creature", "{2}{B}", 3, "", ["B"]),
                Card("Swamp", 70, DeckDefaults.Mainboard, "Basic Land - Swamp", null, 0, "{T}: Add {B}.", ["B"], ["B"]),
                Card("Torment of Hailfire", 15, DeckRoles.Wincons, "Sorcery", "{X}{B}{B}", 2, "Repeat this process X times. Each opponent loses 3 life unless they sacrifice a nonland permanent or discard a card.", ["B"]),
                Card("Gray Merchant of Asphodel", 14, DeckRoles.Wincons, "Creature", "{3}{B}{B}", 5, "When Gray Merchant enters, each opponent loses X life, where X is your devotion to black. You gain life equal to the life lost this way.", ["B"]),
            ],
        };

        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            deck,
            simulations: 200,
            maxTurn: 5,
            seed: 44,
            includeMulligans: false);

        analysis.ComboAssembly.RelevantCards.Should().BeEmpty();
        analysis.ComboAssembly.AssemblyByTurn.Should().OnlyContain(row => row.Probability == 0);
        analysis.ComboAssembly.TutorAssistedAssemblyByTurn.Should().OnlyContain(row => row.Probability == 0);
    }

    /// <summary>
    /// Verifies that opening land rates track exact hypergeometric probabilities.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MatchesHypergeometricOpeningLandRates()
    {
        DeckWorkspace deck = CreateLandSpellDeck(lands: 40, spells: 60);

        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            deck,
            simulations: 5_000,
            maxTurn: 1,
            seed: 1234,
            includeMulligans: false);

        double expectedNoLand = HypergeometricExactly(
            populationSize: 100,
            successCount: 40,
            drawCount: 7,
            observedSuccesses: 0);
        double expectedOneLand = HypergeometricExactly(
            populationSize: 100,
            successCount: 40,
            drawCount: 7,
            observedSuccesses: 1);
        analysis.OpeningHands.NoLandSevenRate.Should().BeApproximately(expectedNoLand, 0.025);
        analysis.OpeningHands.OneLandSevenRate.Should().BeApproximately(expectedOneLand, 0.035);
    }

    /// <summary>
    /// Verifies land-drop probabilities against exact hypergeometric rates for a normal Commander mana base.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MatchesExactLandDropRatesForNormalManaBase()
    {
        DeckWorkspace deck = CreateLandSpellDeck(lands: 37, spells: 63);

        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            deck,
            simulations: 1_500,
            maxTurn: 5,
            seed: 2027,
            includeMulligans: false);

        double expectedTurn3 = HypergeometricAtLeast(
            populationSize: 100,
            successCount: 37,
            drawCount: 10,
            minimumSuccesses: 3);
        double expectedTurn5 = HypergeometricAtLeast(
            populationSize: 100,
            successCount: 37,
            drawCount: 12,
            minimumSuccesses: 5);

        Probability(analysis, "land-drop-by-turn", 3).Should().BeApproximately(expectedTurn3, 0.06);
        Probability(analysis, "land-drop-by-turn", 5).Should().BeApproximately(expectedTurn5, 0.06);
    }

    /// <summary>
    /// Verifies role-seen probabilities against exact rates when no extra draw effects alter cards seen.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MatchesExactRoleSeenRatesForControlledPackages()
    {
        DeckWorkspace deck = CreateRoleSeenOracleDeck();

        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            deck,
            simulations: 1_500,
            maxTurn: 5,
            seed: 2028,
            includeMulligans: false);

        double expectedRampByTurn3 = HypergeometricAtLeast(
            populationSize: 100,
            successCount: 10,
            drawCount: 10,
            minimumSuccesses: 1);
        double expectedInteractionByTurn5 = HypergeometricAtLeast(
            populationSize: 100,
            successCount: 8,
            drawCount: 12,
            minimumSuccesses: 1);

        Probability(analysis, "ramp-seen-by-turn", 3).Should().BeApproximately(expectedRampByTurn3, 0.06);
        Probability(analysis, "interaction-seen-by-turn", 5).Should().BeApproximately(expectedInteractionByTurn5, 0.06);
    }

    /// <summary>
    /// Verifies deterministic fixtures produce precomputed no-mulligan statistics.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MatchesPrecomputedNoMulliganFixtureStatistics()
    {
        DeckPerformanceAnalysis allLands = AnalyzeDirect(
            CreateLandSpellDeck(lands: 100, spells: 0),
            simulations: 100,
            maxTurn: 3,
            seed: 404,
            includeMulligans: false);
        DeckPerformanceAnalysis noLands = AnalyzeDirect(
            CreateLandSpellDeck(lands: 0, spells: 100),
            simulations: 100,
            maxTurn: 3,
            seed: 404,
            includeMulligans: false);

        allLands.DeckSize.Should().Be(100);
        allLands.OpeningHands.SevenCardKeepRate.Should().Be(1);
        allLands.OpeningHands.AverageMulligans.Should().Be(0);
        allLands.OpeningHands.AverageKeptHandSize.Should().Be(7);
        allLands.OpeningHands.AverageKeptLands.Should().Be(7);
        allLands.OpeningHands.FloodedSevenRate.Should().Be(1);
        allLands.OpeningHands.MulliganDistribution.Should().ContainKey(0).WhoseValue.Should().Be(100);
        Probability(allLands, "land-drop-by-turn", 3).Should().Be(1);
        Probability(allLands, "on-curve-untapped-mana-by-turn", 3).Should().Be(1);
        ColorProbability(allLands, "G", 1).Should().Be(1);

        noLands.OpeningHands.SevenCardKeepRate.Should().Be(1);
        noLands.OpeningHands.AverageMulligans.Should().Be(0);
        noLands.OpeningHands.AverageKeptHandSize.Should().Be(7);
        noLands.OpeningHands.AverageKeptLands.Should().Be(0);
        noLands.OpeningHands.NoLandSevenRate.Should().Be(1);
        noLands.OpeningHands.MulliganDistribution.Should().ContainKey(0).WhoseValue.Should().Be(100);
        Probability(noLands, "land-drop-by-turn", 1).Should().Be(0);
        Probability(noLands, "on-curve-untapped-mana-by-turn", 1).Should().Be(0);
    }

    /// <summary>
    /// Verifies deterministic Commander fixtures produce precomputed mulligan statistics.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MatchesPrecomputedCommanderMulliganFixtureStatistics()
    {
        DeckPerformanceAnalysis allLands = AnalyzeDirect(
            CreateCommanderAllLandDeck(),
            simulations: 100,
            maxTurn: 1,
            seed: 405,
            includeMulligans: true);
        DeckPerformanceAnalysis noLands = AnalyzeDirect(
            CreateCommanderNoLandDeck(),
            simulations: 100,
            maxTurn: 1,
            seed: 405,
            includeMulligans: true);

        allLands.OpeningHands.SevenCardKeepRate.Should().Be(0);
        allLands.OpeningHands.AverageMulligans.Should().Be(3);
        allLands.OpeningHands.AverageKeptHandSize.Should().Be(5);
        allLands.OpeningHands.AverageKeptLands.Should().Be(5);
        allLands.OpeningHands.FloodedSevenRate.Should().Be(1);
        allLands.OpeningHands.MulliganDistribution.Should().ContainKey(3).WhoseValue.Should().Be(100);

        noLands.OpeningHands.SevenCardKeepRate.Should().Be(0);
        noLands.OpeningHands.AverageMulligans.Should().Be(3);
        noLands.OpeningHands.AverageKeptHandSize.Should().Be(5);
        noLands.OpeningHands.AverageKeptLands.Should().Be(0);
        noLands.OpeningHands.NoLandSevenRate.Should().Be(1);
        noLands.OpeningHands.MulliganDistribution.Should().ContainKey(3).WhoseValue.Should().Be(100);
    }

    /// <summary>
    /// Verifies that Commander analysis keeps the multiplayer free mulligan as a seven-card hand.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_UsesFreeFirstMulliganForCommander()
    {
        DeckWorkspace commander = CreatePerformanceDeck(plains: 10, islands: 10, utility: 45);
        DeckWorkspace nonFree = CreatePerformanceDeck(plains: 10, islands: 10, utility: 45);
        nonFree.Format = "modern";

        DeckPerformanceAnalysis commanderAnalysis = AnalyzeDirect(
            commander,
            simulations: 2_000,
            maxTurn: 1,
            seed: 202,
            includeMulligans: true);
        DeckPerformanceAnalysis nonFreeAnalysis = AnalyzeDirect(
            nonFree,
            simulations: 2_000,
            maxTurn: 1,
            seed: 202,
            includeMulligans: true);

        commanderAnalysis.OpeningHands.AverageKeptHandSize.Should()
            .BeGreaterThan(nonFreeAnalysis.OpeningHands.AverageKeptHandSize + 0.20);
        commanderAnalysis.Assumptions.Should().Contain(note => note.Contains("first mulligan as free", StringComparison.OrdinalIgnoreCase));
        nonFreeAnalysis.Assumptions.Should().NotContain(note => note.Contains("first mulligan as free", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that mulligans improve access to early ramp in ramp-capable decks.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_MulligansImproveEarlyRampAccess()
    {
        DeckWorkspace deck = CreateCommanderRampDeck(ramp: 16);

        DeckPerformanceAnalysis withoutMulligans = AnalyzeDirect(
            deck,
            simulations: 2_000,
            maxTurn: 1,
            seed: 303,
            includeMulligans: false);
        DeckPerformanceAnalysis withMulligans = AnalyzeDirect(
            deck,
            simulations: 2_000,
            maxTurn: 1,
            seed: 303,
            includeMulligans: true);

        Probability(withMulligans, "ramp-seen-by-turn", 1)
            .Should().BeGreaterThan(Probability(withoutMulligans, "ramp-seen-by-turn", 1) + 0.05);
        withMulligans.OpeningHands.AverageMulligans.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that obvious all-land and no-land decks produce bounded oracle outcomes.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_ReturnsExactLandDropOracleResults()
    {
        DeckPerformanceAnalysis allLands = AnalyzeDirect(
            CreateLandSpellDeck(lands: 100, spells: 0),
            simulations: 200,
            maxTurn: 5,
            seed: 1,
            includeMulligans: false);
        DeckPerformanceAnalysis noLands = AnalyzeDirect(
            CreateLandSpellDeck(lands: 0, spells: 100),
            simulations: 200,
            maxTurn: 5,
            seed: 1,
            includeMulligans: false);

        Probability(allLands, "land-drop-by-turn", 5).Should().Be(1);
        Probability(noLands, "land-drop-by-turn", 1).Should().Be(0);
        allLands.OpeningHands.FloodedSevenRate.Should().Be(1);
        noLands.OpeningHands.NoLandSevenRate.Should().Be(1);
    }

    /// <summary>
    /// Verifies that recommendation axes move the modeled metrics in the expected direction.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_DirectionalSanityChecksFollowDeckbuildingExpectations()
    {
        DeckPerformanceAnalysis lowLand = AnalyzeDirect(CreateLandSpellDeck(lands: 20, spells: 80), seed: 10);
        DeckPerformanceAnalysis highLand = AnalyzeDirect(CreateLandSpellDeck(lands: 40, spells: 60), seed: 10);
        Probability(highLand, "land-drop-by-turn", 3)
            .Should().BeGreaterThan(Probability(lowLand, "land-drop-by-turn", 3) + 0.20);

        DeckPerformanceAnalysis tappedBlue = AnalyzeDirect(CreateMonoBlueLandDeck(tapped: true), maxTurn: 1, seed: 11);
        DeckPerformanceAnalysis untappedBlue = AnalyzeDirect(CreateMonoBlueLandDeck(tapped: false), maxTurn: 1, seed: 11);
        ColorProbability(untappedBlue, "U", 1)
            .Should().BeGreaterThan(ColorProbability(tappedBlue, "U", 1) + 0.90);

        DeckPerformanceAnalysis noRamp = AnalyzeDirect(CreateCommanderRampDeck(ramp: 0), maxTurn: 4, seed: 12);
        DeckPerformanceAnalysis rampHeavy = AnalyzeDirect(CreateCommanderRampDeck(ramp: 20), maxTurn: 4, seed: 12);
        CommanderProbability(rampHeavy, 4)
            .Should().BeGreaterThan(CommanderProbability(noRamp, 4) + 0.20);

        DeckPerformanceAnalysis noInteraction = AnalyzeDirect(CreateInteractionDensityDeck(interaction: 0), maxTurn: 2, seed: 13);
        DeckPerformanceAnalysis interactionDense = AnalyzeDirect(CreateInteractionDensityDeck(interaction: 20), maxTurn: 2, seed: 13);
        Probability(interactionDense, "interaction-held-up-by-turn", 2)
            .Should().BeGreaterThan(Probability(noInteraction, "interaction-held-up-by-turn", 2) + 0.20);

        DeckPerformanceAnalysis poorFixing = AnalyzeDirect(CreateTwoColorFixingDeck(blueSources: 0), maxTurn: 2, seed: 14);
        DeckPerformanceAnalysis goodFixing = AnalyzeDirect(CreateTwoColorFixingDeck(blueSources: 24), maxTurn: 2, seed: 14);
        ColorProbability(goodFixing, "U", 2)
            .Should().BeGreaterThan(ColorProbability(poorFixing, "U", 2) + 0.50);

        DeckPerformanceAnalysis lowCurve = AnalyzeDirect(CreateCurvePressureDeck(highManaCards: 0), maxTurn: 4, seed: 15);
        DeckPerformanceAnalysis highCurve = AnalyzeDirect(CreateCurvePressureDeck(highManaCards: 30), maxTurn: 4, seed: 15);
        ScenarioRate(highCurve, "stranded-high-mana-risk-by-max-turn")
            .Should().BeGreaterThan(ScenarioRate(lowCurve, "stranded-high-mana-risk-by-max-turn") + 0.30);
    }

    /// <summary>
    /// Verifies that conditional tapped lands are not treated as guaranteed early untapped sources.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_TreatsConditionalTappedLandsAsEarlyTappedPressure()
    {
        DeckPerformanceAnalysis conditional = AnalyzeDirect(CreateConditionalBlueLandDeck(), maxTurn: 1, seed: 16);
        DeckPerformanceAnalysis alwaysTapped = AnalyzeDirect(CreateMonoBlueLandDeck(tapped: true), maxTurn: 1, seed: 16);

        ColorProbability(conditional, "U", 1).Should().BeLessThan(0.10);
        ColorProbability(alwaysTapped, "U", 1).Should().BeLessThan(0.10);
    }

    /// <summary>
    /// Verifies that probability rows are bounded and confidence intervals are coherent.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_ProbabilityRowsAreStatisticallyCoherent()
    {
        DeckPerformanceAnalysis analysis = AnalyzeDirect(
            CreatePerformanceDeck(plains: 18, islands: 18, utility: 29),
            simulations: 400,
            maxTurn: 5,
            seed: 16);

        IEnumerable<PerformanceProbability> probabilities = analysis.TurnProbabilities
            .Concat(analysis.Castability.ColorSourceReliability)
            .Concat(analysis.Commander.CastByTurn)
            .Concat(analysis.Commander.ProtectedByTurn)
            .Concat(analysis.ComboAssembly.AssemblyByTurn)
            .Concat(analysis.ComboAssembly.TutorAssistedAssemblyByTurn);
        foreach (PerformanceProbability probability in probabilities)
        {
            probability.Probability.Should().BeInRange(0, 1);
            probability.LowConfidenceInterval.Should().BeInRange(0, probability.Probability);
            probability.HighConfidenceInterval.Should().BeInRange(probability.Probability, 1);
            probability.SampleSize.Should().Be(400);
        }

        foreach (ScenarioPerformance scenario in analysis.Scenarios)
        {
            scenario.SuccessRate.Should().BeInRange(0, 1);
            scenario.LowConfidenceInterval.Should().BeInRange(0, scenario.SuccessRate);
            scenario.HighConfidenceInterval.Should().BeInRange(scenario.SuccessRate, 1);
            scenario.SampleSize.Should().Be(400);
            scenario.FailureDriverCounts.Keys.Should().BeEquivalentTo(scenario.FailureDrivers);
        }
    }

    /// <summary>
    /// Verifies that larger samples narrow Wilson confidence bands.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_LargerSamplesNarrowConfidenceIntervals()
    {
        DeckWorkspace deck = CreateLandSpellDeck(lands: 33, spells: 67);

        PerformanceProbability small = TurnProbability(AnalyzeDirect(
            deck,
            simulations: 300,
            maxTurn: 3,
            seed: 17), "land-drop-by-turn", 3);
        PerformanceProbability large = TurnProbability(AnalyzeDirect(
            deck,
            simulations: 2_000,
            maxTurn: 3,
            seed: 17), "land-drop-by-turn", 3);

        IntervalWidth(large).Should().BeLessThan(IntervalWidth(small));
    }

    /// <summary>
    /// Verifies representative fixture decks stay in plausible metric ranges.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_FixtureArchetypesStayWithinPlausibleRanges()
    {
        DeckPerformanceAnalysis baseline = AnalyzeDirect(
            CreatePerformanceDeck(plains: 18, islands: 18, utility: 29),
            simulations: 400,
            maxTurn: 5,
            seed: 18);
        DeckPerformanceAnalysis taplandHeavy = AnalyzeDirect(
            CreateTappedThreeColorDeck(),
            simulations: 400,
            maxTurn: 3,
            seed: 18);
        DeckPerformanceAnalysis battlecruiser = AnalyzeDirect(
            CreateCurvePressureDeck(highManaCards: 36),
            simulations: 400,
            maxTurn: 4,
            seed: 18);

        baseline.OpeningHands.SevenCardKeepRate.Should().BeInRange(0.55, 0.95);
        ScenarioRate(baseline, "commander-by-turn-4").Should().BeInRange(0.20, 1.00);
        Probability(taplandHeavy, "on-curve-untapped-mana-by-turn", 2).Should().BeLessThan(0.20);
        ScenarioRate(battlecruiser, "stranded-high-mana-risk-by-max-turn").Should().BeGreaterThan(0.50);
    }

    /// <summary>
    /// Verifies static popular-commander fixtures produce plausible profile-specific performance bands.
    /// </summary>
    [Fact]
    public void AnalyzeDeckPerformance_PopularCommanderFixturesStayWithinExpectedBands()
    {
        DeckPerformanceAnalysis krenko = AnalyzeDirect(
            CreateKrenkoTokensFixtureDeck(),
            simulations: 600,
            maxTurn: 5,
            seed: 3001);
        DeckPerformanceAnalysis atraxa = AnalyzeDirect(
            CreateAtraxaCountersFixtureDeck(),
            simulations: 600,
            maxTurn: 5,
            seed: 3002);
        DeckPerformanceAnalysis muldrotha = AnalyzeDirect(
            CreateMuldrothaValueFixtureDeck(),
            simulations: 600,
            maxTurn: 5,
            seed: 3003);

        krenko.DeckSize.Should().Be(100);
        atraxa.DeckSize.Should().Be(100);
        muldrotha.DeckSize.Should().Be(100);

        CommanderProbability(krenko, 4).Should().BeGreaterThan(0.55);
        Probability(krenko, "ramp-seen-by-turn", 3).Should().BeGreaterThan(0.55);
        ScenarioRate(krenko, "stranded-high-mana-risk-by-max-turn").Should().BeLessThan(0.55);

        ScenarioRate(atraxa, "all-colors-by-turn-3").Should().BeInRange(0.35, 0.92);
        CommanderProbability(atraxa, 4).Should().BeInRange(0.30, 0.85);
        ScenarioRate(atraxa, "stranded-high-mana-risk-by-max-turn")
            .Should()
            .BeGreaterThan(ScenarioRate(krenko, "stranded-high-mana-risk-by-max-turn"));

        CommanderProbability(muldrotha, 4).Should().BeLessThan(0.45);
        CommanderProbability(muldrotha, 5).Should().BeGreaterThan(CommanderProbability(muldrotha, 4) + 0.10);
        ScenarioRate(muldrotha, "stranded-high-mana-risk-by-max-turn").Should().BeGreaterThan(0.20);
    }

    /// <summary>
    /// Verifies that long-running performance analysis observes cancellation.
    /// </summary>
    [Fact]
    public async Task AnalyzeDeckPerformance_ObservesCancellation()
    {
        InMemoryRepository repository = new();
        DeckWorkspace deck = CreatePerformanceDeck(plains: 18, islands: 18, utility: 29);
        await repository.SaveAsync(deck, CancellationToken.None);
        DeckSimulationService service = new(repository, new EmptyCardCatalog());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => service.AnalyzeDeckPerformanceAsync(
            deck.Id,
            "commander-default",
            simulations: 100_000,
            maxTurn: 8,
            seed: 1,
            includeMulligans: true,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Runs the pure analyzer with stable defaults for validation fixtures.
    /// </summary>
    private static DeckPerformanceAnalysis AnalyzeDirect(
        DeckWorkspace deck,
        int simulations = 500,
        int maxTurn = 5,
        int seed = 1,
        bool includeMulligans = true)
    {
        return DeckPerformanceAnalyzer.Analyze(
            deck,
            "commander-default",
            simulations,
            maxTurn,
            seed,
            includeMulligans,
            CancellationToken.None);
    }

    /// <summary>
    /// Reads a turn probability point estimate.
    /// </summary>
    private static double Probability(DeckPerformanceAnalysis analysis, string name, int turn)
    {
        return TurnProbability(analysis, name, turn).Probability;
    }

    /// <summary>
    /// Reads a turn probability row.
    /// </summary>
    private static PerformanceProbability TurnProbability(
        DeckPerformanceAnalysis analysis,
        string name,
        int turn)
    {
        return analysis.TurnProbabilities.Single(row => row.Name == name && row.Turn == turn);
    }

    /// <summary>
    /// Reads a color source reliability point estimate.
    /// </summary>
    private static double ColorProbability(DeckPerformanceAnalysis analysis, string color, int turn)
    {
        return analysis.Castability.ColorSourceReliability
            .Single(row => row.Name == $"source-{color}-by-turn" && row.Turn == turn)
            .Probability;
    }

    /// <summary>
    /// Reads commander cast probability by turn.
    /// </summary>
    private static double CommanderProbability(DeckPerformanceAnalysis analysis, int turn)
    {
        return analysis.Commander.CastByTurn.Single(row => row.Turn == turn).Probability;
    }

    /// <summary>
    /// Reads a scenario success or risk rate.
    /// </summary>
    private static double ScenarioRate(DeckPerformanceAnalysis analysis, string name)
    {
        return analysis.Scenarios.Single(row => row.Name == name).SuccessRate;
    }

    /// <summary>
    /// Calculates confidence interval width.
    /// </summary>
    private static double IntervalWidth(PerformanceProbability probability)
    {
        return probability.HighConfidenceInterval - probability.LowConfidenceInterval;
    }

    /// <summary>
    /// Calculates the exact hypergeometric probability of drawing a count of successes.
    /// </summary>
    private static double HypergeometricExactly(
        int populationSize,
        int successCount,
        int drawCount,
        int observedSuccesses)
    {
        double logProbability = LogCombination(successCount, observedSuccesses)
            + LogCombination(populationSize - successCount, drawCount - observedSuccesses)
            - LogCombination(populationSize, drawCount);
        return Math.Exp(logProbability);
    }

    /// <summary>
    /// Calculates the exact hypergeometric probability of drawing at least a count of successes.
    /// </summary>
    private static double HypergeometricAtLeast(
        int populationSize,
        int successCount,
        int drawCount,
        int minimumSuccesses)
    {
        int maximumSuccesses = Math.Min(successCount, drawCount);
        double probability = 0;
        for (int successes = minimumSuccesses; successes <= maximumSuccesses; successes++)
        {
            probability += HypergeometricExactly(populationSize, successCount, drawCount, successes);
        }

        return probability;
    }

    /// <summary>
    /// Calculates log n-choose-k without overflowing intermediate values.
    /// </summary>
    private static double LogCombination(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return double.NegativeInfinity;
        }

        int effectiveK = Math.Min(k, n - k);
        double result = 0;
        for (int index = 1; index <= effectiveK; index++)
        {
            result += Math.Log(n - effectiveK + index) - Math.Log(index);
        }

        return result;
    }

    /// <summary>
    /// Creates a deterministic Commander-style deck for performance tests.
    /// </summary>
    private static DeckWorkspace CreatePerformanceDeck(int plains, int islands, int utility)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Performance Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Maybeboard, IncludedInDeck = false },
            ],
            Cards =
            [
                Card("Test Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{1}{W}{U}", 3, "Whenever you draw your second card each turn, create a token.", ["W", "U"]),
                Card("Plains", plains, DeckDefaults.Mainboard, "Basic Land - Plains", null, 0, "", ["W"], ["W"]),
                Card("Island", islands, DeckDefaults.Mainboard, "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Arcane Signet", 8, DeckDefaults.Mainboard, "Artifact", "{2}", 2, "Add one mana of any color in your commander's color identity.", [], ["W", "U"]),
                Card("Divination", 8, DeckDefaults.Mainboard, "Sorcery", "{2}{U}", 3, "Draw two cards.", ["U"]),
                Card("Counterspell", 8, DeckDefaults.Mainboard, "Instant", "{U}{U}", 2, "Counter target spell.", ["U"]),
                Card("Swiftfoot Boots", 4, DeckDefaults.Mainboard, "Artifact - Equipment", "{2}", 2, "Equipped creature has hexproof and haste. Equip {1}.", []),
                Card("Combo Engine", 3, DeckDefaults.Mainboard, "Artifact", "{3}", 3, "Combo engine. Untap another artifact. Add {U}.", ["U"], ["U"]),
                Card("Table Finisher", 3, DeckDefaults.Mainboard, "Sorcery", "{5}{W}{U}", 7, "Each opponent loses half their life.", ["W", "U"]),
                Card("Utility Spell", utility, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a Baeloth plus Background-style performance fixture with delayed commander intent.
    /// </summary>
    private static DeckWorkspace CreateBackgroundFirstPerformanceDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Baeloth Background Performance",
            Format = "commander",
            Description = DeckIntentText.UpsertDescription(
                null,
                """
                MTG MCP Deck Intent
                Version: 2

                Simulation
                Prefer Commander On Curve: false
                End MTG MCP Deck Intent
                """),
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card(
                    "Baeloth Barrityl, Entertainer",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Elf Shaman",
                    "{4}{R}",
                    5,
                    "Choose a Background. Goaded creatures your opponents control can't block.",
                    ["R", "G"]),
                Card(
                    "Raised by Giants",
                    1,
                    DeckRoles.Commander,
                    "Legendary Enchantment - Background",
                    "{5}{G}",
                    6,
                    "Commander creatures you own have base power and toughness 10/10 and are Giants.",
                    ["G"]),
                Card("Gruul Land Package", 60, DeckDefaults.Mainboard, "Land", null, 0, "{T}: Add {R} or {G}.", [], ["R", "G"]),
                Card("Ramp Stone", 38, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", [], ["R", "G"]),
            ],
        };
    }

    /// <summary>
    /// Creates a deck where Counterspell can never be paid with two blue sources.
    /// </summary>
    private static DeckWorkspace CreateDoubleBlueDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Double Blue Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Blue Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{2}{U}", 3, "", ["U"]),
                Card("Island", 1, DeckDefaults.Mainboard, "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Wastes", 70, DeckDefaults.Mainboard, "Basic Land", null, 0, "", []),
                Card("Counterspell", 28, DeckDefaults.Mainboard, "Instant", "{U}{U}", 2, "Counter target spell.", ["U"]),
            ],
        };
    }

    /// <summary>
    /// Creates a deck where each repeated land enters tapped and produces blue.
    /// </summary>
    private static DeckWorkspace CreateTappedIslandDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Tapped Island Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Blue Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{2}{U}", 3, "", ["U"]),
                Card("Tapped Island", 99, DeckDefaults.Mainboard, "Land - Island", null, 0, "Tapped Island enters tapped.", [], ["U"]),
            ],
        };
    }

    /// <summary>
    /// Creates a controlled deck with only lands and blank spells.
    /// </summary>
    private static DeckWorkspace CreateLandSpellDeck(int lands, int spells)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Land Spell Oracle",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Forest", lands, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Blank Spell", spells, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a controlled package deck where role-seen rates have direct hypergeometric oracles.
    /// </summary>
    private static DeckWorkspace CreateRoleSeenOracleDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Role Seen Oracle",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Forest", 37, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Ramp Stone", 10, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add {G}.", [], ["G"]),
                Card("Nature's Claim", 8, DeckRoles.Interaction, "Instant", "{G}", 1, "Destroy target artifact or enchantment.", ["G"]),
                Card("Blank Spell", 45, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a Krenko, Mob Boss go-wide token fixture inspired by EDHREC Goblin token shells.
    /// </summary>
    private static DeckWorkspace CreateKrenkoTokensFixtureDeck()
    {
        return CommanderFixture(
            "Krenko Tokens Fixture",
            Card("Krenko, Mob Boss", 1, DeckRoles.Commander, "Legendary Creature - Goblin Warrior", "{2}{R}", 4, "{T}: Create X 1/1 red Goblin creature tokens, where X is the number of Goblins you control.", ["R"]),
            [
                Card("Mountain", 34, "Land", "Basic Land - Mountain", null, 0, "", ["R"], ["R"]),
                Card("Red Ramp Package", 10, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", [], ["R"]),
                Card("Goblin Draw Package", 10, DeckRoles.Draw, "Sorcery", "{2}{R}", 3, "Draw two cards, then discard a card.", ["R"]),
                Card("Red Interaction Package", 8, DeckRoles.Interaction, "Instant", "{1}{R}", 2, "Destroy target artifact or deal damage to target creature.", ["R"]),
                Card("Goblin Token Package", 21, DeckRoles.Synergy, "Creature - Goblin", "{2}{R}", 3, "When this creature enters, create two 1/1 red Goblin creature tokens.", ["R"]),
                Card("Goblin Finisher Package", 8, DeckRoles.Wincons, "Creature - Goblin", "{4}{R}", 5, "Creatures you control get +2/+0 and gain haste until end of turn.", ["R"]),
                Card("Goblin Utility Package", 8, DeckRoles.Utility, "Creature - Goblin", "{2}{R}", 3, "Other Goblins you control get +1/+1.", ["R"]),
            ]);
    }

    /// <summary>
    /// Creates an Atraxa, Praetors' Voice counters fixture inspired by EDHREC proliferate and +1/+1 counter shells.
    /// </summary>
    private static DeckWorkspace CreateAtraxaCountersFixtureDeck()
    {
        return CommanderFixture(
            "Atraxa Counters Fixture",
            Card("Atraxa, Praetors' Voice", 1, DeckRoles.Commander, "Legendary Creature - Phyrexian Angel Horror", "{G}{W}{U}{B}", 4, "Flying, vigilance, deathtouch, lifelink. At the beginning of your end step, proliferate.", ["W", "U", "B", "G"]),
            [
                Card("Four-Color Fixing Land Package", 5, "Land", "Land", null, 0, "{T}: Add one mana of any color in your commander's color identity.", [], ["W", "U", "B", "G"]),
                Card("Forest", 10, "Land", "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Island", 8, "Land", "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Plains", 8, "Land", "Basic Land - Plains", null, 0, "", ["W"], ["W"]),
                Card("Swamp", 6, "Land", "Basic Land - Swamp", null, 0, "", ["B"], ["B"]),
                Card("Four-Color Ramp Package", 10, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", [], ["W", "U", "B", "G"]),
                Card("Counter Draw Package", 10, DeckRoles.Draw, "Creature - Wizard", "{2}{U}", 3, "Whenever one or more counters are put on a creature you control, draw a card.", ["U"]),
                Card("Counters Interaction Package", 9, DeckRoles.Interaction, "Instant", "{1}{W}", 2, "Exile target creature.", ["W"]),
                Card("Counters Protection Package", 4, DeckRoles.Protection, "Instant", "{1}{G}", 2, "Creatures you control gain hexproof and indestructible until end of turn.", ["G"]),
                Card("Proliferate Synergy Package", 17, DeckRoles.Synergy, "Creature - Human", "{2}{G}", 3, "When this creature enters, put a +1/+1 counter on each creature you control, then proliferate.", ["G"]),
                Card("Counters Finisher Package", 5, DeckRoles.Wincons, "Creature - Hydra", "{4}{G}{G}", 6, "Double the number of +1/+1 counters on each creature you control.", ["G"]),
                Card("Counters Utility Package", 7, DeckRoles.Utility, "Enchantment", "{3}", 3, "If one or more counters would be put on a permanent you control, put that many plus one instead.", ["G"]),
            ]);
    }

    /// <summary>
    /// Creates a Muldrotha, the Gravetide value fixture inspired by EDHREC graveyard-control shells.
    /// </summary>
    private static DeckWorkspace CreateMuldrothaValueFixtureDeck()
    {
        return CommanderFixture(
            "Muldrotha Value Fixture",
            Card("Muldrotha, the Gravetide", 1, DeckRoles.Commander, "Legendary Creature - Elemental Avatar", "{3}{B}{G}{U}", 6, "During each of your turns, you may play a permanent card of each permanent type from your graveyard.", ["B", "G", "U"]),
            [
                Card("Sultai Fixing Land Package", 4, "Land", "Land", null, 0, "{T}: Add one mana of any color in your commander's color identity.", [], ["B", "G", "U"]),
                Card("Forest", 13, "Land", "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Island", 11, "Land", "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Swamp", 10, "Land", "Basic Land - Swamp", null, 0, "", ["B"], ["B"]),
                Card("Sultai Ramp Package", 11, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", [], ["B", "G", "U"]),
                Card("Graveyard Draw Package", 10, DeckRoles.Draw, "Enchantment", "{2}{U}", 3, "At the beginning of your upkeep, draw a card.", ["U"]),
                Card("Sultai Interaction Package", 10, DeckRoles.Interaction, "Instant", "{1}{B}", 2, "Destroy target creature or planeswalker.", ["B"]),
                Card("Recursion Value Package", 18, DeckRoles.Recursion, "Creature - Shaman", "{3}{G}", 4, "Return target permanent card from your graveyard to your hand.", ["G"]),
                Card("Graveyard Fuel Package", 8, DeckRoles.Synergy, "Creature - Horror", "{2}{B}", 3, "When this creature enters, mill three cards.", ["B"]),
                Card("Muldrotha Finisher Package", 4, DeckRoles.Wincons, "Creature - Avatar", "{5}{G}{G}", 7, "When this creature enters, each opponent loses life equal to the number of permanents you control.", ["G"]),
            ]);
    }

    /// <summary>
    /// Creates a Commander fixture from one commander and already-quantified package cards.
    /// </summary>
    private static DeckWorkspace CommanderFixture(
        string name,
        DeckCard commander,
        List<DeckCard> cards)
    {
        DeckWorkspace workspace = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = "Land", IncludedInDeck = true },
            ],
            Cards = [commander],
        };
        workspace.Cards.AddRange(cards);
        return workspace;
    }

    /// <summary>
    /// Creates a Commander deck whose library is all lands.
    /// </summary>
    private static DeckWorkspace CreateCommanderAllLandDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Commander All Lands",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Green Commander", 1, DeckRoles.Commander, "Creature - Druid", "{2}{G}", 3, "", ["G"]),
                Card("Forest", 99, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
            ],
        };
    }

    /// <summary>
    /// Creates a Commander deck whose library has no lands.
    /// </summary>
    private static DeckWorkspace CreateCommanderNoLandDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Commander No Lands",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Green Commander", 1, DeckRoles.Commander, "Creature - Druid", "{2}{G}", 3, "", ["G"]),
                Card("Blank Spell", 99, DeckDefaults.Mainboard, "Sorcery", "{1}{G}", 2, "Scry 1.", ["G"]),
            ],
        };
    }

    /// <summary>
    /// Creates a mono-blue land deck with either tapped or untapped lands.
    /// </summary>
    private static DeckWorkspace CreateMonoBlueLandDeck(bool tapped)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = tapped ? "Tapped Blue Oracle" : "Untapped Blue Oracle",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Blue Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{2}{U}", 3, "", ["U"]),
                Card(
                    tapped ? "Tapped Island" : "Island",
                    99,
                    DeckDefaults.Mainboard,
                    tapped ? "Land - Island" : "Basic Land - Island",
                    null,
                    0,
                    tapped ? "Tapped Island enters tapped." : "",
                    ["U"],
                    ["U"]),
            ],
        };
    }

    /// <summary>
    /// Creates a mono-blue deck whose lands can enter untapped after paying or meeting a condition.
    /// </summary>
    private static DeckWorkspace CreateConditionalBlueLandDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Conditional Blue Oracle",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Blue Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{2}{U}", 3, "", ["U"]),
                Card(
                    "Watery Grave",
                    99,
                    DeckDefaults.Mainboard,
                    "Land - Island Swamp",
                    null,
                    0,
                    "As Watery Grave enters, you may pay 2 life. If you don't, it enters tapped.",
                    ["U", "B"],
                    ["U", "B"]),
            ],
        };
    }

    /// <summary>
    /// Creates a deck where cheap ramp should improve five-mana commander timing.
    /// </summary>
    private static DeckWorkspace CreateCommanderRampDeck(int ramp)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Ramp Directional",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Ramp Commander", 1, DeckRoles.Commander, "Creature - Druid", "{4}{G}", 5, "", ["G"]),
                Card("Forest", 36, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Ramp Stone", ramp, DeckDefaults.Mainboard, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", [], ["G"]),
                Card("Blank Spell", 63 - ramp, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a deck that varies interaction density while keeping mana stable.
    /// </summary>
    private static DeckWorkspace CreateInteractionDensityDeck(int interaction)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Interaction Directional",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Island", 50, DeckDefaults.Mainboard, "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Counterspell", interaction, DeckDefaults.Mainboard, "Instant", "{U}", 1, "Counter target spell.", ["U"]),
                Card("Blank Spell", 50 - interaction, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a two-color deck that varies access to blue sources.
    /// </summary>
    private static DeckWorkspace CreateTwoColorFixingDeck(int blueSources)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Fixing Directional",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Azorius Commander", 1, DeckRoles.Commander, "Creature - Advisor", "{2}{W}{U}", 4, "", ["W", "U"]),
                Card("Plains", 48 - blueSources, DeckDefaults.Mainboard, "Basic Land - Plains", null, 0, "", ["W"], ["W"]),
                Card("Island", blueSources, DeckDefaults.Mainboard, "Basic Land - Island", null, 0, "", ["U"], ["U"]),
                Card("Blank Spell", 51, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 1.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a deck that varies high-curve stranded-card pressure.
    /// </summary>
    private static DeckWorkspace CreateCurvePressureDeck(int highManaCards)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Curve Pressure",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Forest", 36, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "", ["G"], ["G"]),
                Card("Colossal Threat", highManaCards, DeckDefaults.Mainboard, "Creature - Beast", "{6}{G}", 7, "Trample.", ["G"]),
                Card("Cheap Spell", 64 - highManaCards, DeckDefaults.Mainboard, "Sorcery", "{1}{G}", 2, "Scry 1.", ["G"]),
            ],
        };
    }

    /// <summary>
    /// Creates a three-color deck whose mana base enters tapped.
    /// </summary>
    private static DeckWorkspace CreateTappedThreeColorDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Tapped Three Color",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Esper Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{1}{W}{U}{B}", 4, "", ["W", "U", "B"]),
                Card("Tapped Plains", 33, DeckDefaults.Mainboard, "Land - Plains", null, 0, "Tapped Plains enters tapped.", ["W"], ["W"]),
                Card("Tapped Island", 33, DeckDefaults.Mainboard, "Land - Island", null, 0, "Tapped Island enters tapped.", ["U"], ["U"]),
                Card("Tapped Swamp", 33, DeckDefaults.Mainboard, "Land - Swamp", null, 0, "Tapped Swamp enters tapped.", ["B"], ["B"]),
            ],
        };
    }

    /// <summary>
    /// Creates a deck with one dominant land and one dense interaction spell.
    /// </summary>
    private static DeckWorkspace CreateOneColorInteractionDeck(
        string landName,
        List<string> producedMana,
        string spellName,
        string spellCost,
        List<string> spellColorIdentity,
        string spellText)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Mana Symbol Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Symbol Commander", 1, DeckRoles.Commander, "Creature - Wizard", "{7}", 7, "", []),
                Card(landName, 50, DeckDefaults.Mainboard, "Basic Land", null, 0, "", [], producedMana),
                Card(spellName, 49, DeckDefaults.Mainboard, "Instant", spellCost, 1, spellText, spellColorIdentity),
            ],
        };
    }

    /// <summary>
    /// Creates a deck with one W/U source, many colorless sources, and a WU interaction spell.
    /// </summary>
    private static DeckWorkspace CreateSingleFlexibleSourceDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Single Flexible Source Test",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Colorless Commander", 1, DeckRoles.Commander, "Creature - Construct", "{7}", 7, "", []),
                Card("Command Tower", 1, DeckDefaults.Mainboard, "Land", null, 0, "Add one mana of any color in your commander's color identity.", [], ["W", "U"]),
                Card("Wastes", 70, DeckDefaults.Mainboard, "Basic Land", null, 0, "", [], ["C"]),
                Card("Azorius Answer", 28, DeckDefaults.Mainboard, "Instant", "{W}{U}", 2, "Counter target spell.", ["W", "U"]),
            ],
        };
    }

    /// <summary>
    /// Creates an Inga and Esika fixture for commander-specific simulation assumptions.
    /// </summary>
    private static DeckWorkspace CreateIngaAndEsikaPerformanceDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Inga and Esika Performance",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
            ],
            Cards =
            [
                Card(
                    "Inga and Esika",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Human God",
                    "{2}{G}{U}",
                    4,
                    "Creatures you control have vigilance and \"{T}: Add one mana of any color. Spend this mana only to cast a creature spell.\" Whenever you cast a creature spell, if three or more mana from creatures was spent to cast it, draw a card.",
                    ["G", "U"],
                    ["G", "U"]),
                Card("Forest", 20, DeckDefaults.Mainboard, "Basic Land - Forest", null, 0, "{T}: Add {G}.", [], ["G"]),
                Card("Island", 16, DeckDefaults.Mainboard, "Basic Land - Island", null, 0, "{T}: Add {U}.", [], ["U"]),
                Card("Elvish Mystic", 4, DeckRoles.Ramp, "Creature - Elf Druid", "{G}", 1, "{T}: Add {G}.", ["G"], ["G"]),
                Card("Wood Elves", 10, DeckRoles.Ramp, "Creature - Elf Scout", "{2}{G}", 3, "Search your library for a Forest card.", ["G"]),
                Card("Beast Whisperer", 10, DeckRoles.Draw, "Creature - Elf Druid", "{2}{G}{G}", 4, "Whenever you cast a creature spell, draw a card.", ["G"]),
                Card("Utility Elf", 49, DeckDefaults.Mainboard, "Creature - Elf", "{2}{G}", 3, "A helpful creature.", ["G"]),
            ]
        };
    }

    /// <summary>
    /// Creates a deck card with cached snapshot data.
    /// </summary>
    private static DeckCard Card(
        string name,
        int quantity,
        string category,
        string typeLine,
        string? manaCost,
        double manaValue,
        string oracleText,
        List<string> colorIdentity,
        List<string>? producedMana = null)
    {
        return new DeckCard
        {
            Name = name,
            Quantity = quantity,
            PrimaryCategory = category,
            Categories = [category],
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ManaCost = manaCost,
                ManaValue = manaValue,
                OracleText = oracleText,
                ColorIdentity = colorIdentity,
                ProducedMana = producedMana ?? [],
            },
        };
    }

    /// <summary>
    /// Provides empty card catalog behavior.
    /// </summary>
    private sealed class EmptyCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Returns no search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no semantic search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no card result.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(null);
        }

        /// <summary>
        /// Returns no named card results.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(
                new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns no rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no prints.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Returns no suggestions.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }

    /// <summary>
    /// Provides in-memory workspace storage.
    /// </summary>
    private sealed class InMemoryRepository : IDeckWorkspaceRepository
    {
        /// <summary>
        /// Stores workspaces by id.
        /// </summary>
        private readonly Dictionary<string, DeckWorkspace> workspaces = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a workspace.
        /// </summary>
        public Task<DeckWorkspace> SaveAsync(
            DeckWorkspace workspace,
            CancellationToken cancellationToken)
        {
            workspaces[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Gets a workspace by id.
        /// </summary>
        public Task<DeckWorkspace?> GetAsync(
            string workspaceId,
            CancellationToken cancellationToken)
        {
            workspaces.TryGetValue(workspaceId, out DeckWorkspace? workspace);
            return Task.FromResult(workspace);
        }

        /// <summary>
        /// Lists saved workspaces.
        /// </summary>
        public Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DeckWorkspace>>(workspaces.Values.ToList());
        }
    }

    /// <summary>
    /// Provides in-memory plan storage.
    /// </summary>
    private sealed class InMemoryPlanRepository : IDeckPlanRepository
    {
        /// <summary>
        /// Stores plans by id.
        /// </summary>
        private readonly Dictionary<string, DeckEditPlan> plans = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saves a plan.
        /// </summary>
        public Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
        {
            plans[plan.PlanId] = plan;
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Gets a plan.
        /// </summary>
        public Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
        {
            plans.TryGetValue(planId, out DeckEditPlan? plan);
            return Task.FromResult(plan);
        }

        /// <summary>
        /// Lists plans.
        /// </summary>
        public Task<IReadOnlyList<DeckEditPlan>> ListAsync(
            string? workspaceId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DeckEditPlan> result = plans.Values
                .Where(plan => string.IsNullOrWhiteSpace(workspaceId)
                    || plan.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(result);
        }

        /// <summary>
        /// Deletes a plan.
        /// </summary>
        public Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken)
        {
            return Task.FromResult(plans.Remove(planId));
        }
    }

}
