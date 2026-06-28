using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck simulation and projection tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Serializes MCP-facing payloads with web-style property names.
    /// </summary>
    private static readonly JsonSerializerOptions WebJsonSerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies that combo and goldfish projections return explainable estimates.
    /// </summary>
    [Fact]
    public async Task GoldfishAndComboTools_ReturnHeuristicEstimates()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Goldfish",
            Description = DeckIntentText.UpsertDescription(null, "Simulation Profile: combo"),
            Cards =
            [
                new DeckCard { Name = "Forest", Quantity = 40, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 12, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "{T}: Add {G}." } },
                new DeckCard { Name = "Token Maker", Quantity = 16, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Creature", ManaValue = 3, OracleText = "When this enters, create two 1/1 creature tokens." } },
                new DeckCard { Name = "Craterhoof Behemoth", Quantity = 3, PrimaryCategory = DeckRoles.Wincons, Categories = [DeckRoles.Wincons], Snapshot = new CardSnapshot { TypeLine = "Creature", ManaValue = 8, OracleText = "Creatures you control get +X/+X and gain trample until end of turn." } },
                new DeckCard { Name = "Combo A", Quantity = 1, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "Untap target permanent. Copy target activated ability." } },
                new DeckCard { Name = "Combo B", Quantity = 1, PrimaryCategory = DeckRoles.Synergy, Categories = [DeckRoles.Synergy], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "Whenever an ability is copied, untap target permanent." } }
            ]
        }, TestContext.Current.CancellationToken);
        FakeCardCatalog catalog = new();
        DeckSimulationService simulation = CreateSimulationService(workspaces, catalog);
        DeckAnalysisService analysis = CreateAnalysisService(workspaces, catalog);

        GoldfishSimulationResult goldfish = await simulation.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 200,
            seed: 9,
            mulligan: true,
            TestContext.Current.CancellationToken);
        ComboPressureEstimate pressure = await analysis.EstimateComboPressureAsync(
            workspace.Id,
            TestContext.Current.CancellationToken);

        goldfish.TurnSummaries.Should().HaveCount(5);
        goldfish.TurnSummaries.Last().MedianManaSources.Should().BeGreaterThan(0);
        goldfish.WinEstimate.Routes.Should().NotBeEmpty();
        pressure.Level.Should().NotBe("low");
    }

    /// <summary>
    /// Verifies that deterministic goldfish fixtures produce stable board and win estimates.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_ProducesStableExactProjectionForDeterministicComboDeck()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Deterministic Combo Goldfish",
            Description = DeckIntentText.UpsertDescription(null, "Simulation Profile: combo"),
            Cards =
            [
                new DeckCard
                {
                    Name = "Combo A",
                    Quantity = 40,
                    PrimaryCategory = DeckRoles.Synergy,
                    Categories = [DeckRoles.Synergy],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ManaValue = 0,
                        OracleText = "Combo. Untap target permanent. Copy target activated ability."
                    }
                },
                new DeckCard
                {
                    Name = "Combo B",
                    Quantity = 40,
                    PrimaryCategory = DeckRoles.Synergy,
                    Categories = [DeckRoles.Synergy],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ManaValue = 0,
                        OracleText = "Combo. Whenever an ability is copied, untap target permanent."
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 25,
            seed: 123,
            mulligan: true,
            TestContext.Current.CancellationToken);
        GoldfishSimulationResult replay = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 25,
            seed: 123,
            mulligan: true,
            TestContext.Current.CancellationToken);
        ProjectedTurnState projected = await service.ProjectBoardStateAsync(
            workspace.Id,
            turn: 3,
            simulations: 25,
            seed: 123,
            TestContext.Current.CancellationToken);
        WinTurnEstimate winTurn = await service.EstimateWinTurnAsync(
            workspace.Id,
            maxTurn: 5,
            simulations: 25,
            seed: 123,
            TestContext.Current.CancellationToken);

        goldfish.Simulations.Should().Be(100);
        JsonSerializer.Serialize(replay, WebJsonSerializerOptions)
            .Should()
            .Be(JsonSerializer.Serialize(goldfish, WebJsonSerializerOptions));
        goldfish.RngKind.Should().Be(DeterministicSimulationRandom.Kind);
        goldfish.WinEstimate.RngKind.Should().Be(DeterministicSimulationRandom.Kind);
        goldfish.Mulligans.Should().Be(100);
        goldfish.TurnSummaries.Should().HaveCount(5);
        goldfish.TurnSummaries.Should().OnlyContain(summary => summary.RngKind == DeterministicSimulationRandom.Kind);
        goldfish.TurnSummaries.Select(summary => summary.MedianNonlandPermanents)
            .Should()
            .Equal(6, 7, 8, 9, 10);
        goldfish.TurnSummaries.Should().OnlyContain(summary =>
            summary.MedianLands == 0
            && summary.MedianManaSources == 0
            && summary.MedianCardsInHand == 0
            && summary.MedianPower == 0
            && summary.MedianTokens == 0
            && summary.Confidence == 0.50);
        goldfish.WinEstimate.ObservedWins.Should().Be(100);
        goldfish.WinEstimate.ObservedWinRate.Should().Be(1);
        goldfish.WinEstimate.MedianObservedWinTurn.Should().Be(5);
        goldfish.WinEstimate.P25ObservedWinTurn.Should().Be(5);
        goldfish.WinEstimate.P75ObservedWinTurn.Should().Be(5);
        goldfish.WinEstimate.MedianWinTurn.Should().Be(5);
        goldfish.LethalConfidence.Should().BeGreaterThan(0);
        goldfish.WinEstimate.LethalConfidence.Should().Be(goldfish.LethalConfidence);
        goldfish.WinEstimate.PressureOnlyProgress.Should().Be(goldfish.PressureOnlyProgress);
        string winEstimateJson = JsonSerializer.Serialize(goldfish.WinEstimate, WebJsonSerializerOptions);
        winEstimateJson.Should().Contain("rngKind");
        winEstimateJson.Should().Contain("medianObservedWinTurn");
        winEstimateJson.Should().Contain("lethalConfidence");
        winEstimateJson.Should().Contain("pressureOnlyProgress");
        winEstimateJson.Should().NotContain("medianWinTurn");
        goldfish.WinEstimate.WinByTurnRates.Should().Contain([
            new KeyValuePair<int, double>(1, 0),
            new KeyValuePair<int, double>(2, 0),
            new KeyValuePair<int, double>(3, 0),
            new KeyValuePair<int, double>(4, 0),
            new KeyValuePair<int, double>(5, 1)
        ]);
        WinRoute route = goldfish.WinEstimate.Routes.Should().ContainSingle().Subject;
        route.Name.Should().Be("combo");
        route.EarliestTurn.Should().Be(5);
        route.Probability.Should().Be(1);
        route.Cards.Should().BeEquivalentTo(["Combo A", "Combo B"]);

        projected.Turn.Should().Be(3);
        projected.RngKind.Should().Be(DeterministicSimulationRandom.Kind);
        projected.MedianNonlandPermanents.Should().Be(8);
        projected.LikelyBoard.Should().Be("0 lands, 0 mana sources, 8 nonland permanents, about 0 pressure, 0 cards in hand.");
        winTurn.RngKind.Should().Be(DeterministicSimulationRandom.Kind);
        winTurn.Routes.Should().ContainSingle(route => route.Kind == "combo" && route.Probability == 1);
    }

    /// <summary>
    /// Verifies that bounded effective-cost heuristics cover convoke, Blasphemous Act-style costs, and X token spells.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_UsesBoundedEffectiveCostsForDynamicSpells()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Dynamic Cost Goldfish",
            Cards =
            [
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 20,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Forest", ManaValue = 0, OracleText = "{T}: Add {G}." }
                },
                new DeckCard
                {
                    Name = "Free Soldier",
                    Quantity = 35,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Creature - Soldier", ManaValue = 0 }
                },
                new DeckCard
                {
                    Name = "Blasphemous Act",
                    Quantity = 15,
                    PrimaryCategory = DeckRoles.BoardWipes,
                    Categories = [DeckRoles.BoardWipes],
                    Snapshot = new CardSnapshot
                    {
                        ManaCost = "{8}{R}",
                        ManaValue = 9,
                        TypeLine = "Sorcery",
                        OracleText = "This spell costs {1} less to cast for each creature on the battlefield. Blasphemous Act deals 13 damage to each creature."
                    }
                },
                new DeckCard
                {
                    Name = "Hour of Reckoning",
                    Quantity = 10,
                    PrimaryCategory = DeckRoles.BoardWipes,
                    Categories = [DeckRoles.BoardWipes],
                    Snapshot = new CardSnapshot
                    {
                        ManaCost = "{4}{W}{W}{W}",
                        ManaValue = 7,
                        TypeLine = "Sorcery",
                        OracleText = "Convoke. Destroy all nontoken creatures."
                    }
                },
                new DeckCard
                {
                    Name = "Secure the Wastes",
                    Quantity = 20,
                    PrimaryCategory = DeckRoles.Synergy,
                    Categories = [DeckRoles.Synergy],
                    Snapshot = new CardSnapshot
                    {
                        ManaCost = "{X}{W}",
                        ManaValue = 1,
                        TypeLine = "Instant",
                        OracleText = "Create X 1/1 white Warrior creature tokens."
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 3,
            simulations: 100,
            seed: 77,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.RepresentativeLines.Should().Contain(line =>
            line.Contains("Blasphemous Act", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Hour of Reckoning", StringComparison.OrdinalIgnoreCase));
        goldfish.TurnSummaries.Single(summary => summary.Turn == 3).MedianTokens.Should().BeGreaterThan(2);
        goldfish.PressureOnlyProgress.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that affinity and commander-gated discounts affect goldfish cast timing.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_UsesAffinityAndCommanderDiscounts()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Affinity Discount Goldfish",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                GoldfishCard("Test Commander", 1, DeckRoles.Commander, "Legendary Creature", "{1}", 1, "", []),
                GoldfishCard("Forest", 30, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Free Bauble", 24, DeckRoles.Synergy, "Artifact", "{0}", 0, "An artifact.", []),
                GoldfishCard("Thoughtcast", 35, DeckRoles.Draw, "Sorcery", "{4}{U}", 5, "Affinity for artifacts. Draw two cards.", ["U"]),
                GoldfishCard(
                    "Commander's Call",
                    10,
                    DeckRoles.Synergy,
                    "Sorcery",
                    "{3}{G}",
                    4,
                    "This spell costs {1} less to cast if you control your commander. Create two Food tokens.",
                    ["G"]),
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());
        DeckCard thoughtcast = GoldfishCard("Thoughtcast", 1, DeckRoles.Draw, "Sorcery", "{4}{U}", 5, "Affinity for artifacts. Draw two cards.", ["U"]);
        DeckCard bauble = GoldfishCard("Free Bauble", 1, DeckRoles.Synergy, "Artifact", "{0}", 0, "An artifact.", []);
        DeckCard commandersCall = GoldfishCard(
            "Commander's Call",
            1,
            DeckRoles.Synergy,
            "Sorcery",
            "{3}{G}",
            4,
            "This spell costs {1} less to cast if you control your commander. Create two Food tokens.",
            ["G"]);

        EstimateGoldfishTotalManaSpent(thoughtcast, [bauble, bauble, bauble, bauble], availableMana: 5, commanderOnline: false)
            .Should()
            .Be(1);
        EstimateGoldfishTotalManaSpent(commandersCall, [], availableMana: 4, commanderOnline: false)
            .Should()
            .Be(4);
        EstimateGoldfishTotalManaSpent(commandersCall, [], availableMana: 4, commanderOnline: true)
            .Should()
            .Be(3);

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 3,
            simulations: 100,
            seed: 78,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.RepresentativeLines.Should().Contain(line => line.Contains("Commander's Call", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that banked Food plus lifegain/drain payoff evidence produces the Food drain route.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DetectsFoodLifegainDrainRoute()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Food Drain Goldfish",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                GoldfishCard(
                    "Sam, Loyal Attendant",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Halfling Peasant",
                    "{1}{G}",
                    2,
                    "Activated abilities of Foods you control cost {1} less to activate.",
                    ["G"]),
                GoldfishCard("Forest", 37, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard(
                    "Second Breakfast",
                    30,
                    DeckRoles.Synergy,
                    "Enchantment",
                    "{1}",
                    1,
                    "At the beginning of your end step, create two Food tokens.",
                    []),
                GoldfishCard(
                    "Sanguine Steward",
                    32,
                    DeckRoles.Payoffs,
                    "Creature",
                    "{1}",
                    1,
                    "Whenever you gain life, each opponent loses 1 life.",
                    []),
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 6,
            simulations: 100,
            seed: 79,
            mulligan: true,
            TestContext.Current.CancellationToken);

        WinRoute route = goldfish.WinEstimate.Routes.Should()
            .ContainSingle(candidate => candidate.Kind == "food-lifegain-drain")
            .Subject;
        SimulationRouteEvidence evidence = route.Evidence.Should()
            .ContainSingle(candidate => candidate.Name == "Food Lifegain Drain Burst")
            .Subject;
        evidence.Evidence.Should().Contain(line => line.Contains("food bank", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("lifegain available", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("drain payoff", StringComparison.OrdinalIgnoreCase));
        goldfish.Notes.Should().Contain(note => note.Contains("Sam, Loyal Attendant", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that partial Commander goldfish runs warn and leave sideboard cards out of the sampled deck.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_WarnsWhenCommanderActiveDeckIsPartial()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Partial Commander Goldfish",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Sideboard, IncludedInDeck = false },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Test Commander",
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot { TypeLine = "Legendary Creature", ManaValue = 3 }
                },
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 88,
                    PrimaryCategory = DeckDefaults.Mainboard,
                    Categories = [DeckDefaults.Mainboard],
                    Snapshot = new CardSnapshot { TypeLine = "Basic Land - Forest", ManaValue = 0 }
                },
                new DeckCard
                {
                    Name = "Sideboard Bomb",
                    Quantity = 11,
                    PrimaryCategory = DeckDefaults.Sideboard,
                    Categories = [DeckDefaults.Sideboard],
                    Snapshot = new CardSnapshot { TypeLine = "Sorcery", ManaValue = 1 }
                },
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 3,
            simulations: 100,
            seed: 21,
            mulligan: true,
            TestContext.Current.CancellationToken);
        WinTurnEstimate estimate = await service.EstimateWinTurnAsync(
            workspace.Id,
            maxTurn: 3,
            simulations: 100,
            seed: 21,
            TestContext.Current.CancellationToken);

        goldfish.Warnings.Should().Contain(warning =>
            warning.Contains("89 included cards", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("Sideboard", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("not sampled", StringComparison.OrdinalIgnoreCase));
        goldfish.WinEstimate.Notes.Should().Contain(note =>
            note.Contains("partial active deck", StringComparison.OrdinalIgnoreCase));
        estimate.Notes.Should().Contain(note =>
            note.Contains("89 included cards", StringComparison.OrdinalIgnoreCase));
        goldfish.RepresentativeLines.Should().NotContain(line =>
            line.Contains("Sideboard Bomb", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Background-first intent delays the creature commander and reports separate command-zone timings.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_SequencesBackgroundBeforeDelayedCommander()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateBackgroundFirstGoldfishDeck(
                """
                Prefer Commander On Curve: false
                Preferred Commander Turn: 6
                Preferred Background Turn: 4
                Command Zone Order: Raised by Giants, Baeloth Barrityl, Entertainer
                """),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 7,
            simulations: 300,
            seed: 31,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.CommandZone.CommanderNames.Should().ContainSingle("Baeloth Barrityl, Entertainer");
        goldfish.CommandZone.BackgroundNames.Should().ContainSingle("Raised by Giants");
        goldfish.CommandZone.CommandZoneNames.Should().Equal("Raised by Giants", "Baeloth Barrityl, Entertainer");
        goldfish.CommandZone.CommanderCastByTurn.Single(row => row.Turn == 4)
            .Probability.Should().Be(0);
        goldfish.CommandZone.AverageBackgroundCastTurn.Should().NotBeNull();
        goldfish.CommandZone.AverageCommanderCastTurn.Should().NotBeNull();
        goldfish.CommandZone.AverageBackgroundCastTurn.Should().BeLessThan(goldfish.CommandZone.AverageCommanderCastTurn!.Value);
        goldfish.CommandZone.CommanderWithBackgroundOnlineByTurn.Single(row => row.Turn == 7)
            .Probability.Should().BeGreaterThan(0);

        int backgroundLine = goldfish.RepresentativeLines.FindIndex(line =>
            line.Contains("cast background Raised by Giants", StringComparison.OrdinalIgnoreCase));
        int commanderLine = goldfish.RepresentativeLines.FindIndex(line =>
            line.Contains("cast commander Baeloth Barrityl, Entertainer", StringComparison.OrdinalIgnoreCase));
        backgroundLine.Should().BeGreaterThanOrEqualTo(0);
        commanderLine.Should().BeGreaterThan(backgroundLine);
    }

    /// <summary>
    /// Verifies that Background decks default to Background-first sequencing when commander-on-curve is disabled.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DefaultsBackgroundBeforeCommanderWhenOnCurveDisabled()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateBackgroundFirstGoldfishDeck(
                """
                Prefer Commander On Curve: false
                """),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 7,
            simulations: 300,
            seed: 31,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.CommandZone.CommandZoneNames.Should().Equal("Raised by Giants", "Baeloth Barrityl, Entertainer");
        goldfish.CommandZone.CommanderCastByTurn.Single(row => row.Turn == 4)
            .Probability.Should().Be(0);
        goldfish.CommandZone.AverageBackgroundCastTurn.Should().NotBeNull();
        goldfish.CommandZone.AverageCommanderCastTurn.Should().NotBeNull();
        goldfish.CommandZone.AverageBackgroundCastTurn.Should().BeLessThan(goldfish.CommandZone.AverageCommanderCastTurn!.Value);
    }

    /// <summary>
    /// Verifies that multiple command-zone cards are not sampled into the library and can both be deployed.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DeploysMultipleCommandZoneCardsFromCommandZone()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(CreatePartnerGoldfishDeck(), TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 3,
            simulations: 100,
            seed: 41,
            mulligan: false,
            TestContext.Current.CancellationToken);

        goldfish.CommandZone.CommanderNames.Should().Equal("Partner One", "Partner Two");
        goldfish.CommandZone.CommanderCastByTurn.Single(row => row.Turn == 2).Probability.Should().Be(1);
        goldfish.TurnSummaries.Single(row => row.Turn == 3).MedianLands.Should().Be(3);
        goldfish.RepresentativeLines.Should().Contain(line =>
            line.Contains("cast commander Partner One", StringComparison.OrdinalIgnoreCase));
        goldfish.RepresentativeLines.Should().Contain(line =>
            line.Contains("cast commander Partner Two", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that Archidekt goldfish comparison imports reference decks read-only and returns raw deltas.
    /// </summary>
    [Fact]
    public async Task CompareArchidektGoldfish_ImportsReferencesReadOnlyAndReturnsDeltas()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace active = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Active Goldfish", archidektDeckId: null),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new();
        string firstUrl = "https://archidekt.com/decks/111/reference_one";
        string secondUrl = "https://archidekt.com/decks/222/reference_two";
        string thirdUrl = "https://archidekt.com/decks/333/reference_three";
        archidekt.ImportedDecksByInput[firstUrl] = CreateGoldfishFixtureDeck("Reference One", "111", wincons: 5);
        archidekt.ImportedDecksByInput[secondUrl] = CreateGoldfishFixtureDeck("Reference Two", "222", ramp: 16);
        archidekt.ImportedDecksByInput[thirdUrl] = CreateGoldfishFixtureDeck("Reference Three", "333", tokens: 20);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog(), archidekt);

        ArchidektGoldfishComparisonResult comparison = await service.CompareArchidektGoldfishAsync(
            active.Id,
            firstUrl,
            secondUrl,
            thirdUrl,
            targetTurn: 5,
            simulations: 100,
            seed: 44,
            mulligan: true,
            TestContext.Current.CancellationToken);

        comparison.WorkspaceId.Should().Be(active.Id);
        comparison.ActiveDeck.Label.Should().Be("active");
        comparison.ActiveDeck.Source.Should().Be("workspace");
        comparison.ActiveDeck.Goldfish.Simulations.Should().Be(100);
        comparison.ReferenceDecks.Select(deck => deck.Label).Should().Equal("reference-1", "reference-2", "reference-3");
        comparison.ReferenceDecks.Select(deck => deck.Input).Should().Equal(firstUrl, secondUrl, thirdUrl);
        comparison.ReferenceDecks.Should().OnlyContain(deck =>
            deck.Source == "archidekt"
            && deck.Goldfish.TargetTurn == 5
            && deck.DeltaFromActive != null);
        comparison.ReferenceDecks.Should().OnlyContain(deck =>
            deck.Goldfish.WinEstimate.ObservedWins >= 0
            && deck.Goldfish.WinEstimate.ObservedWinRate >= 0
            && deck.Goldfish.WinEstimate.ObservedWinRate <= 1);
        comparison.ReferenceDecks.Should().OnlyContain(deck =>
            deck.DeltaFromActive!.MedianObservedWinTurnDelta == deck.DeltaFromActive.MedianWinTurnDelta);
        string deltaJson = JsonSerializer.Serialize(
            comparison.ReferenceDecks.First().DeltaFromActive,
            WebJsonSerializerOptions);
        deltaJson.Should().Contain("medianObservedWinTurnDelta");
        deltaJson.Should().NotContain("medianWinTurnDelta");
        comparison.ReferenceDecks.Select(deck => deck.ArchidektDeckId).Should().Equal("111", "222", "333");
        comparison.ReferenceFailures.Should().BeEmpty();
        archidekt.ImportRequests.Select(request => request.DeckIdOrUrl).Should().Equal(firstUrl, secondUrl, thirdUrl);
        archidekt.ImportRequests.Should().OnlyContain(request => !request.WriteBack);
        comparison.Warnings.Should().BeEmpty();
        comparison.Notes.Should().Contain(note => note.Contains("writeBack=false", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that non-Archidekt references are rejected without blocking valid Archidekt comparisons.
    /// </summary>
    [Fact]
    public async Task CompareArchidektGoldfish_ReportsNonArchidektReferencesWithoutImportingThem()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace active = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Active Goldfish", archidektDeckId: null),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new();
        string archidektUrl = "https://archidekt.com/decks/111/reference_one";
        string nonArchidektUrl = "https://example.com/decks/reference-two";
        archidekt.ImportedDecksByInput[archidektUrl] = CreateGoldfishFixtureDeck("Reference One", "111", wincons: 5);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog(), archidekt);

        ArchidektGoldfishComparisonResult comparison = await service.CompareArchidektGoldfishAsync(
            active.Id,
            archidektUrl,
            nonArchidektUrl,
            archidektDeckUrl3: null,
            targetTurn: 5,
            simulations: 100,
            seed: 44,
            mulligan: true,
            TestContext.Current.CancellationToken);

        comparison.ReferenceDecks.Should().ContainSingle(deck => deck.Input == archidektUrl);
        GoldfishReferenceImportFailure failure = comparison.ReferenceFailures.Should().ContainSingle().Subject;
        failure.Label.Should().Be("reference-2");
        failure.Input.Should().Be(nonArchidektUrl);
        failure.Source.Should().Be("example.com");
        failure.Reason.Should().Contain("Only Archidekt");
        archidekt.ImportRequests.Select(request => request.DeckIdOrUrl).Should().Equal(archidektUrl);
        comparison.Warnings.Should().Contain(warning => warning.Contains("reference-2", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that generalized goldfish comparison handles local workspaces, Archidekt imports, and partial failures.
    /// </summary>
    [Fact]
    public async Task CompareGoldfish_ComparesLocalAndArchidektDecksWithPartialFailures()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace active = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Active Goldfish", archidektDeckId: null),
            TestContext.Current.CancellationToken);
        DeckWorkspace second = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Second Goldfish", archidektDeckId: null, ramp: 16),
            TestContext.Current.CancellationToken);
        FakeArchidektGateway archidekt = new();
        string archidektUrl = "https://archidekt.com/decks/222/reference_two";
        string nonArchidektUrl = "https://example.com/decks/reference-three";
        archidekt.ImportedDecksByInput[archidektUrl] = CreateGoldfishFixtureDeck("Reference Two", "222", tokens: 20);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog(), archidekt);

        DeckGoldfishComparisonResult comparison = await service.CompareGoldfishAsync(
            [active.Id, second.Id],
            [archidektUrl, nonArchidektUrl],
            targetTurn: 5,
            simulations: 100,
            seed: 44,
            mulligan: true,
            TestContext.Current.CancellationToken);

        comparison.WorkspaceId.Should().Be(active.Id);
        comparison.BaselineDeck.Label.Should().Be("active");
        comparison.ComparedDecks.Select(deck => deck.Label).Should().Equal("workspace-2", "reference-1");
        comparison.ComparedDecks.Should().OnlyContain(deck => deck.DeltaFromActive != null);
        comparison.ComparedDecks.Single(deck => deck.Label == "workspace-2").Source.Should().Be("workspace");
        comparison.ComparedDecks.Single(deck => deck.Label == "reference-1").Source.Should().Be("archidekt");
        GoldfishReferenceImportFailure failure = comparison.Failures.Should().ContainSingle().Subject;
        failure.Label.Should().Be("reference-2");
        failure.Source.Should().Be("example.com");
        comparison.Warnings.Should().Contain(warning => warning.Contains("reference-2", StringComparison.OrdinalIgnoreCase));
        archidekt.ImportRequests.Should().ContainSingle(request =>
            request.DeckIdOrUrl == archidektUrl
            && !request.WriteBack);
    }

    /// <summary>
    /// Verifies that missing Archidekt configuration is reported as a reference failure.
    /// </summary>
    [Fact]
    public async Task CompareGoldfish_ReturnsFailureWhenArchidektGatewayIsUnavailable()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace active = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Active Goldfish", archidektDeckId: null),
            TestContext.Current.CancellationToken);
        DeckWorkspace second = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Second Goldfish", archidektDeckId: null, ramp: 16),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        DeckGoldfishComparisonResult comparison = await service.CompareGoldfishAsync(
            [active.Id, second.Id],
            ["https://archidekt.com/decks/222/reference_two"],
            targetTurn: 5,
            simulations: 100,
            seed: 44,
            mulligan: true,
            TestContext.Current.CancellationToken);

        comparison.ComparedDecks.Should().ContainSingle(deck => deck.Label == "workspace-2");
        GoldfishReferenceImportFailure failure = comparison.Failures.Should().ContainSingle().Subject;
        failure.Label.Should().Be("reference-1");
        failure.Source.Should().Be("archidekt");
        failure.Reason.Should().Contain("Archidekt support is not configured");
    }

    /// <summary>
    /// Verifies the opt-in rules-backed race model reuses deck_compare_goldfish inputs.
    /// </summary>
    [Fact]
    public async Task CompareGoldfish_RulesBackedRaceModelReturnsConservativeRace()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace fast = await workspaces.SaveAsync(
            CreateRulesBackedRaceFixtureDeck("Fast Race", "fast", power: 20),
            TestContext.Current.CancellationToken);
        DeckWorkspace slow = await workspaces.SaveAsync(
            CreateRulesBackedRaceFixtureDeck("Slow Race", "slow", power: 1),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        object comparison = await service.CompareGoldfishAsync(
            [fast.Id, slow.Id],
            archidektDeckIdsOrUrls: null,
            simulationProfile: SimulationProfileIds.Auto,
            targetTurn: 5,
            simulations: 3,
            seed: 8,
            mulligan: false,
            model: RulesGoldfishRaceConstants.ModelName,
            cancellationToken: TestContext.Current.CancellationToken);

        RulesGoldfishRaceResult result = comparison.Should().BeOfType<RulesGoldfishRaceResult>().Subject;
        result.ModelName.Should().Be(RulesGoldfishRaceConstants.ModelName);
        result.RandomKind.Should().Be(DeterministicSimulationRandom.Kind);
        result.StartingLife.Should().Be(40);
        result.TiePolicy.Should().Contain("Same-turn lethal");
        result.Notes.Should().Contain(note => note.Contains("not a full Magic rules engine", StringComparison.OrdinalIgnoreCase));
        result.Decks.Single(deck => deck.Label == "active").Wins.Should().Be(3);
        result.Decks.Single(deck => deck.Label == "workspace-2").Losses.Should().Be(3);
    }

    /// <summary>
    /// Verifies that weak decks report no likely goldfish win route.
    /// </summary>
    [Fact]
    public async Task EstimateWinTurn_ReturnsNoRouteForDeckWithoutWinCondition()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "No Wincon",
            Cards =
            [
                new DeckCard { Name = "Forest", Quantity = 42, PrimaryCategory = DeckRoles.Lands, Categories = [DeckRoles.Lands] },
                new DeckCard { Name = "Ramp", Quantity = 10, PrimaryCategory = DeckRoles.Ramp, Categories = [DeckRoles.Ramp], Snapshot = new CardSnapshot { TypeLine = "Artifact", ManaValue = 2, OracleText = "{T}: Add {G}." } }
            ]
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        WinTurnEstimate estimate = await service.EstimateWinTurnAsync(
            workspace.Id,
            maxTurn: 7,
            simulations: 100,
            seed: 17,
            TestContext.Current.CancellationToken);

        estimate.ObservedWins.Should().Be(0);
        estimate.ObservedWinRate.Should().Be(0);
        estimate.MedianObservedWinTurn.Should().BeNull();
        estimate.MedianWinTurn.Should().BeNull();
        estimate.Routes.Should().BeEmpty();
        estimate.Notes.Should().Contain(note => note.Contains("No likely win", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that reanimator-control routes can use graveyard target and held-interaction evidence.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DetectsReanimatorControlRoutePredicates()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateReanimatorControlGoldfishDeck(),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 100,
            seed: 21,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.WinEstimate.ObservedWins.Should().BeGreaterThan(0);
        WinRoute route = goldfish.WinEstimate.Routes.Should()
            .ContainSingle(candidate => candidate.Kind == "reanimator-control")
            .Subject;
        route.Evidence.Should().ContainSingle(candidate => candidate.Name == "Reanimator Control");
        SimulationRouteEvidence evidence = route.Evidence.Single();
        evidence.Evidence.Should().Contain(line => line.Contains("graveyard count", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("reanimation target", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("interaction held", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that aristocrats routes can use sacrifice, drain, recursion, and token evidence.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DetectsAristocratsRoutePredicates()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateAristocratsGoldfishDeck(),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 4,
            simulations: 100,
            seed: 22,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.WinEstimate.ObservedWins.Should().BeGreaterThan(0);
        WinRoute route = goldfish.WinEstimate.Routes.Should()
            .ContainSingle(candidate => candidate.Kind == "aristocrats")
            .Subject;
        route.Evidence.Should().ContainSingle(candidate => candidate.Name == "Aristocrats Loop");
        SimulationRouteEvidence evidence = route.Evidence.Single();
        evidence.Evidence.Should().Contain(line => line.Contains("sacrifice outlet", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("drain payoff", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("recursive creature", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("tokens", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that profile arguments select explicit profiles and unknown profiles fall back through auto with a warning.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_AcceptsExplicitProfileAndFallsBackUnknownProfileToAuto()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Profile Control Goldfish",
            Description = DeckIntentText.UpsertDescription(
                null,
                """
                Goal: Fast combo loop
                """),
            Cards =
            [
                GoldfishCard("Forest", 50, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Combo A", 25, DeckRoles.Synergy, "Artifact", "{1}", 1, "Combo. Untap target permanent and copy an ability.", []),
                GoldfishCard("Combo B", 25, DeckRoles.Synergy, "Artifact", "{1}", 1, "Combo. Whenever an ability is copied, untap target permanent.", []),
            ],
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult explicitProfile = await service.SimulateGoldfishAsync(
            workspace.Id,
            SimulationProfileIds.Control,
            targetTurn: 4,
            simulations: 100,
            seed: 33,
            mulligan: true,
            TestContext.Current.CancellationToken);
        GoldfishSimulationResult unknownProfile = await service.SimulateGoldfishAsync(
            workspace.Id,
            "not-real",
            targetTurn: 4,
            simulations: 100,
            seed: 33,
            mulligan: true,
            TestContext.Current.CancellationToken);

        explicitProfile.ProfileResolution.Source.Should().Be("explicit");
        explicitProfile.ProfileResolution.Profile.Id.Should().Be(SimulationProfileIds.Control);
        unknownProfile.ProfileResolution.Source.Should().Be("auto");
        unknownProfile.ProfileResolution.Profile.Id.Should().Be(SimulationProfileIds.Combo);
        unknownProfile.Warnings.Should().Contain(warning => warning.Contains("not-real", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that goldfish summary metrics stay on the documented 0-100 scales.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_ReturnsBoundedSummaryMetrics()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateGoldfishFixtureDeck("Metric Goldfish", archidektDeckId: null),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 5,
            simulations: 100,
            seed: 44,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.BoardDevelopmentScore.Should().BeInRange(0, 100);
        goldfish.ThreatPressure.Should().BeInRange(0, 100);
        goldfish.EngineOnlineRate.Should().BeInRange(0, 100);
        goldfish.WinDetectionConfidence.Should().BeInRange(0, 100);
        goldfish.Notes.Should().Contain(note => note.Contains("0-100", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that fallback route rationales are not labeled deterministic.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_LabelsFallbackRouteRationaleAsHeuristicPressure()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Fallback Pressure",
            Cards =
            [
                GoldfishCard("Forest", 50, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Aerial Closer", 50, DeckRoles.Wincons, "Creature - Beast", "{2}{G}", 3, "Flying. Trample.", ["G"]),
            ],
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            targetTurn: 7,
            simulations: 100,
            seed: 52,
            mulligan: true,
            TestContext.Current.CancellationToken);

        WinRoute route = goldfish.WinEstimate.Routes.Should().NotBeEmpty().And.Subject.First();
        route.Evidence.Should().OnlyContain(evidence => evidence.Source == "fallback");
        route.Rationale.Should().Contain("fallback heuristic pressure");
        route.Rationale.Should().NotContain("deterministic route evidence");
    }

    /// <summary>
    /// Verifies that activated commander engines create pressure evidence without claiming deterministic wins.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_ReportsKenessosEnginePressureWithoutDetectedWins()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Kenessos Pressure",
            Cards =
            [
                GoldfishCard(
                    "Kenessos, Priest of Thassa",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Merfolk Cleric",
                    "{1}{G/U}",
                    2,
                    "{5}{G/U}: Scry 1, then reveal the top card of your library. If it is a Kraken, Leviathan, Octopus, or Serpent creature card, put it onto the battlefield.",
                    ["G", "U"]),
                GoldfishCard("Forest", 48, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Topdeck Setup", 20, DeckRoles.Tutors, "Sorcery", "{G}", 1, "Scry 3, then put a card from your hand on top of your library.", ["G"]),
                GoldfishCard("Stormtide Leviathan", 31, DeckRoles.Wincons, "Creature - Leviathan", "{5}{U}{U}{U}", 8, "Islandwalk. Creatures without flying or islandwalk can't attack.", ["U"]),
            ],
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            SimulationProfileIds.Neutral,
            targetTurn: 7,
            simulations: 100,
            seed: 71,
            mulligan: true,
            TestContext.Current.CancellationToken);

        goldfish.EnginePressure.LibraryRevealCheat.Should().BeTrue();
        goldfish.EnginePressure.HighCmcHitDensity.Should().BeGreaterThan(0.5);
        goldfish.EnginePressure.Pressure.Should().BeGreaterThan(0);
        goldfish.EnginePressure.Evidence.Should().Contain(evidence =>
            evidence.Contains("activated library/topdeck cheat", StringComparison.OrdinalIgnoreCase));
        goldfish.WinEstimate.ObservedWins.Should().Be(0);
    }

    /// <summary>
    /// Verifies that sorcery finisher pressure requires a meaningful board.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_SorceryFinisherPressureRequiresBoard()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace emptyBoard = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Stampede Empty Board",
            Cards =
            [
                GoldfishCard("Forest", 60, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Overwhelming Stampede", 40, DeckRoles.Wincons, "Sorcery", "{3}{G}{G}", 5, "Until end of turn, creatures you control get +X/+X and gain trample, where X is the greatest power among creatures you control.", ["G"]),
            ],
        }, TestContext.Current.CancellationToken);
        DeckWorkspace boardDeck = await workspaces.SaveAsync(new DeckWorkspace
        {
            Name = "Stampede Board",
            Cards =
            [
                GoldfishCard("Forest", 40, DeckRoles.Lands, "Basic Land - Forest", null, 0, "{T}: Add {G}.", ["G"], ["G"]),
                GoldfishCard("Token Maker", 30, DeckRoles.Synergy, "Creature - Elf", "{1}{G}", 2, "When this creature enters, create two 1/1 creature tokens.", ["G"]),
                GoldfishCard("Overwhelming Stampede", 30, DeckRoles.Wincons, "Sorcery", "{3}{G}{G}", 5, "Until end of turn, creatures you control get +X/+X and gain trample, where X is the greatest power among creatures you control.", ["G"]),
            ],
        }, TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult empty = await service.SimulateGoldfishAsync(
            emptyBoard.Id,
            SimulationProfileIds.Neutral,
            targetTurn: 6,
            simulations: 100,
            seed: 72,
            mulligan: true,
            TestContext.Current.CancellationToken);
        GoldfishSimulationResult withBoard = await service.SimulateGoldfishAsync(
            boardDeck.Id,
            SimulationProfileIds.Neutral,
            targetTurn: 6,
            simulations: 100,
            seed: 72,
            mulligan: true,
            TestContext.Current.CancellationToken);

        empty.SorceryFinisherPressure.SorceryFinisherHeld.Should().BeTrue();
        empty.SorceryFinisherPressure.Pressure.Should().Be(0);
        withBoard.SorceryFinisherPressure.SorceryFinisherHeld.Should().BeTrue();
        withBoard.SorceryFinisherPressure.BoardPowerBeforeFinisher.Should().BeGreaterThanOrEqualTo(6);
        withBoard.SorceryFinisherPressure.Pressure.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that Ghen-style enchantment recursion can be detected through profile route templates.
    /// </summary>
    [Fact]
    public async Task GoldfishSimulation_DetectsEnchantmentRecursionEngineRoute()
    {
        InMemoryRepository workspaces = new();
        DeckWorkspace workspace = await workspaces.SaveAsync(
            CreateEnchantmentRecursionGoldfishDeck(),
            TestContext.Current.CancellationToken);
        DeckSimulationService service = CreateSimulationService(workspaces, new FakeCardCatalog());

        GoldfishSimulationResult goldfish = await service.SimulateGoldfishAsync(
            workspace.Id,
            SimulationProfileIds.Value,
            targetTurn: 6,
            simulations: 100,
            seed: 61,
            mulligan: true,
            TestContext.Current.CancellationToken);

        WinRoute route = goldfish.WinEstimate.Routes.Should()
            .ContainSingle(candidate => candidate.Kind == "engine-inevitability")
            .Subject;
        SimulationRouteEvidence evidence = route.Evidence.Should()
            .ContainSingle(candidate => candidate.Name == "Enchantment Recursion Engine")
            .Subject;
        evidence.Evidence.Should().Contain(line => line.Contains("enchantment recursion", StringComparison.OrdinalIgnoreCase));
        evidence.Evidence.Should().Contain(line => line.Contains("engine payoff", StringComparison.OrdinalIgnoreCase));
        evidence.MissingRequirements.Should().BeEmpty();
    }

    /// <summary>
    /// Creates a graveyard-control fixture with an explicit deck-intent route.
    /// </summary>
    private static DeckWorkspace CreateReanimatorControlGoldfishDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Reanimator Control Goldfish",
            Format = "commander",
            Description = DeckIntentText.UpsertDescription(
                null,
                """
                MTG MCP Deck Intent
                Version: 2
                Simulation Profile: control

                Simulation
                Hold Interaction From Turn: 2
                Minimum Interaction Held: 1

                Win Routes
                Reanimator Control: requires graveyard>=2, reanimation-target, interaction-held>=1; earliest turn 3; kind reanimator-control
                End MTG MCP Deck Intent
                """),
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                GoldfishCard("Graveyard Commander", 1, DeckRoles.Commander, "Legendary Creature", "{2}{B}", 3, "", ["B"]),
                GoldfishCard("Swamp", 39, DeckRoles.Lands, "Basic Land - Swamp", null, 0, "{T}: Add {B}.", ["B"], ["B"]),
                GoldfishCard("Entomb Setup", 20, DeckRoles.Tutors, "Instant", "{B}", 1, "Search your library for a creature card and put that card into your graveyard.", ["B"]),
                GoldfishCard("Counterspell", 20, DeckRoles.Interaction, "Instant", "{1}{U}", 2, "Counter target spell.", ["U"]),
                GoldfishCard("Archon of Cruelty", 20, DeckRoles.Wincons, "Creature - Archon", "{6}{B}{B}", 8, "Flying. When this creature enters, each opponent sacrifices a creature or planeswalker.", ["B"]),
            ],
        };
    }

    /// <summary>
    /// Creates an aristocrats fixture with an explicit deck-intent route.
    /// </summary>
    private static DeckWorkspace CreateAristocratsGoldfishDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Aristocrats Goldfish",
            Format = "commander",
            Description = DeckIntentText.UpsertDescription(
                null,
                """
                MTG MCP Deck Intent
                Version: 2
                Simulation Profile: value

                Win Routes
                Aristocrats Loop: requires sac-outlet, drain-payoff, recursive-creature, tokens>=2; earliest turn 3; kind aristocrats
                End MTG MCP Deck Intent
                """),
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                GoldfishCard("Aristocrats Commander", 1, DeckRoles.Commander, "Legendary Creature", "{2}{B}", 3, "", ["B"]),
                GoldfishCard("Swamp", 39, DeckRoles.Lands, "Basic Land - Swamp", null, 0, "{T}: Add {B}.", ["B"], ["B"]),
                GoldfishCard("Carrion Feeder", 15, DeckRoles.Synergy, "Creature - Zombie", "{B}", 1, "Sacrifice a creature: Put a +1/+1 counter on Carrion Feeder.", ["B"]),
                GoldfishCard("Blood Artist", 15, DeckRoles.Wincons, "Creature - Vampire", "{1}{B}", 2, "Whenever Blood Artist or another creature dies, target player loses 1 life and you gain 1 life.", ["B"]),
                GoldfishCard("Reassembling Skeleton", 15, DeckRoles.Synergy, "Creature - Skeleton", "{1}{B}", 2, "Return Reassembling Skeleton from your graveyard to the battlefield tapped.", ["B"]),
                GoldfishCard("Token Maker", 15, DeckRoles.Synergy, "Creature - Warlock", "{1}{B}", 2, "When this creature enters, create two 1/1 creature tokens.", ["B"]),
            ],
        };
    }

    /// <summary>
    /// Creates a Ghen-style enchantment recursion fixture without an explicit deck-intent route.
    /// </summary>
    private static DeckWorkspace CreateEnchantmentRecursionGoldfishDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Enchantment Recursion Goldfish",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                GoldfishCard(
                    "Ghen, Arcanum Weaver",
                    1,
                    DeckRoles.Commander,
                    "Legendary Creature - Human Wizard",
                    "{R}{W}{B}",
                    3,
                    "{R}{W}{B}, {T}, Sacrifice an enchantment: Return target enchantment card from your graveyard to the battlefield.",
                    ["R", "W", "B"]),
                GoldfishCard("Swamp", 40, DeckRoles.Lands, "Basic Land - Swamp", null, 0, "{T}: Add {B}.", ["B"], ["B"]),
                GoldfishCard("Graveyard Setup", 20, DeckRoles.Tutors, "Sorcery", "{B}", 1, "Search your library for an enchantment card and put that card into your graveyard.", ["B"]),
                GoldfishCard("Bleeding Pact", 39, DeckRoles.Wincons, "Enchantment", "{1}{B}", 2, "At the beginning of your end step, each opponent loses 1 life.", ["B"]),
            ],
        };
    }

    /// <summary>
    /// Creates a goldfish fixture card with cached snapshot data.
    /// </summary>
    private static DeckCard GoldfishCard(
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
    /// Reads the private goldfish cast-cost estimate without expanding the production API.
    /// </summary>
    private static int EstimateGoldfishTotalManaSpent(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens = 0,
        int artifactTokens = 0,
        int foodTokens = 0,
        int availableMana = 10,
        bool commanderOnline = false)
    {
        MethodInfo method = typeof(DeckSimulationService)
            .GetMethod("EstimateGoldfishCastCost", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing goldfish cast-cost estimator.");
        object cost = method.Invoke(
                null,
                [card, battlefield, tokens, artifactTokens, foodTokens, availableMana, commanderOnline])
            ?? throw new InvalidOperationException("Goldfish cast-cost estimator returned null.");
        PropertyInfo property = cost.GetType().GetProperty("TotalManaSpent")
            ?? throw new InvalidOperationException("Goldfish cast-cost result is missing TotalManaSpent.");
        return (int)(property.GetValue(cost)
            ?? throw new InvalidOperationException("Goldfish cast-cost TotalManaSpent returned null."));
    }

    /// <summary>
    /// Creates a Baeloth plus Background-style goldfish fixture with delayed commander intent.
    /// </summary>
    private static DeckWorkspace CreateBackgroundFirstGoldfishDeck(string simulationSettings)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Baeloth Background Goldfish",
            Format = "commander",
            Description = DeckIntentText.UpsertDescription(
                null,
                $"""
                MTG MCP Deck Intent
                Version: 2

                Simulation
                {simulationSettings}
                End MTG MCP Deck Intent
                """),
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Baeloth Barrityl, Entertainer",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature - Elf Shaman",
                        ManaCost = "{4}{R}",
                        ManaValue = 5,
                        OracleText = "Choose a Background. Goaded creatures your opponents control can't block.",
                        ColorIdentity = ["R", "G"],
                    },
                },
                new DeckCard
                {
                    Name = "Raised by Giants",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Enchantment - Background",
                        ManaCost = "{5}{G}",
                        ManaValue = 6,
                        OracleText = "Commander creatures you own have base power and toughness 10/10 and are Giants.",
                        ColorIdentity = ["G"],
                    },
                },
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 60,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        ManaValue = 0,
                        OracleText = "{T}: Add {G}.",
                    },
                },
                new DeckCard
                {
                    Name = "Ramp Stone",
                    Quantity = 38,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ManaCost = "{2}",
                        ManaValue = 2,
                        OracleText = "{T}: Add one mana of any color.",
                    },
                },
            ],
        };
    }

    /// <summary>
    /// Creates a Partner-style goldfish fixture with inflated commander quantities to prove they stay out of the library.
    /// </summary>
    private static DeckWorkspace CreatePartnerGoldfishDeck()
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Partner Goldfish",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
            ],
            Cards =
            [
                new DeckCard
                {
                    Name = "Partner One",
                    Quantity = 30,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature - Human",
                        ManaCost = "{G}",
                        ManaValue = 1,
                        OracleText = "Partner",
                        ColorIdentity = ["G"],
                    },
                },
                new DeckCard
                {
                    Name = "Partner Two",
                    Quantity = 30,
                    PrimaryCategory = DeckRoles.Commander,
                    Categories = [DeckRoles.Commander],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Legendary Creature - Elf",
                        ManaCost = "{1}{G}",
                        ManaValue = 2,
                        OracleText = "Partner",
                        ColorIdentity = ["G"],
                    },
                },
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 3,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        ManaValue = 0,
                        OracleText = "{T}: Add {G}.",
                    },
                },
            ],
        };
    }

    /// <summary>
    /// Creates a compact fixture with printed stats for rules-backed race tests.
    /// </summary>
    private static DeckWorkspace CreateRulesBackedRaceFixtureDeck(string name, string id, int power)
    {
        return new DeckWorkspace
        {
            Id = id,
            Name = name,
            Format = "commander",
            Cards =
            [
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = 3,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        ProducedMana = ["G"],
                    },
                },
                new DeckCard
                {
                    Name = $"{name} Attacker",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Wincons,
                    Categories = [DeckRoles.Wincons],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature - Cat",
                        ManaValue = 1,
                        Power = power.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Toughness = "1",
                    },
                },
            ],
        };
    }

    /// <summary>
    /// Creates a compact Commander-style fixture for goldfish comparison tests.
    /// </summary>
    private static DeckWorkspace CreateGoldfishFixtureDeck(
        string name,
        string? archidektDeckId,
        int lands = 40,
        int ramp = 12,
        int tokens = 16,
        int wincons = 3)
    {
        return new DeckWorkspace
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Format = "commander",
            Mode = archidektDeckId is null ? WorkspaceMode.Local : WorkspaceMode.Archidekt,
            ArchidektDeckId = archidektDeckId,
            Cards =
            [
                new DeckCard
                {
                    Name = "Forest",
                    Quantity = lands,
                    PrimaryCategory = DeckRoles.Lands,
                    Categories = [DeckRoles.Lands],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Basic Land - Forest",
                        ManaValue = 0,
                        OracleText = "{T}: Add {G}."
                    }
                },
                new DeckCard
                {
                    Name = "Ramp",
                    Quantity = ramp,
                    PrimaryCategory = DeckRoles.Ramp,
                    Categories = [DeckRoles.Ramp],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Artifact",
                        ManaValue = 2,
                        OracleText = "{T}: Add {G}."
                    }
                },
                new DeckCard
                {
                    Name = "Token Maker",
                    Quantity = tokens,
                    PrimaryCategory = DeckRoles.Synergy,
                    Categories = [DeckRoles.Synergy],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature",
                        ManaValue = 3,
                        OracleText = "When this enters, create two 1/1 creature tokens."
                    }
                },
                new DeckCard
                {
                    Name = "Craterhoof Behemoth",
                    Quantity = wincons,
                    PrimaryCategory = DeckRoles.Wincons,
                    Categories = [DeckRoles.Wincons],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature",
                        ManaValue = 8,
                        OracleText = "Creatures you control get +X/+X and gain trample until end of turn."
                    }
                },
            ]
        };
    }
}
