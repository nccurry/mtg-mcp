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
}
