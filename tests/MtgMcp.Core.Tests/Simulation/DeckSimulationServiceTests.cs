using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains deck simulation and projection tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
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
        goldfish.Mulligans.Should().Be(100);
        goldfish.TurnSummaries.Should().HaveCount(5);
        goldfish.TurnSummaries.Select(summary => summary.MedianNonlandPermanents)
            .Should()
            .Equal(7, 8, 9, 10, 11);
        goldfish.TurnSummaries.Should().OnlyContain(summary =>
            summary.MedianLands == 0
            && summary.MedianManaSources == 0
            && summary.MedianCardsInHand == 0
            && summary.MedianPower == 0
            && summary.MedianTokens == 0
            && summary.Confidence == 0.50);
        goldfish.WinEstimate.MedianWinTurn.Should().Be(5);
        goldfish.WinEstimate.P25WinTurn.Should().Be(5);
        goldfish.WinEstimate.P75WinTurn.Should().Be(5);
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
        projected.MedianNonlandPermanents.Should().Be(9);
        projected.LikelyBoard.Should().Be("0 lands, 0 mana sources, 9 nonland permanents, about 0 pressure, 0 cards in hand.");
        winTurn.Routes.Should().ContainSingle(route => route.Kind == "combo" && route.Probability == 1);
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
        archidekt.ImportedDecksByInput[thirdUrl] = CreateGoldfishFixtureDeck("Reference Three", "333", tokens: 24);
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
        comparison.ReferenceDecks.Select(deck => deck.ArchidektDeckId).Should().Equal("111", "222", "333");
        archidekt.ImportRequests.Select(request => request.DeckIdOrUrl).Should().Equal(firstUrl, secondUrl, thirdUrl);
        archidekt.ImportRequests.Should().OnlyContain(request => !request.WriteBack);
        comparison.Notes.Should().Contain(note => note.Contains("writeBack=false", StringComparison.OrdinalIgnoreCase));
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

        estimate.MedianWinTurn.Should().BeNull();
        estimate.Routes.Should().BeEmpty();
        estimate.Notes.Should().Contain(note => note.Contains("No likely win", StringComparison.OrdinalIgnoreCase));
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
