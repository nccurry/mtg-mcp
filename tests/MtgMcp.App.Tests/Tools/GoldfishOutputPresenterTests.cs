using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.App.Tests.Tools;

/// <summary>
/// Verifies goldfish presenters preserve replay metadata while bounding optional evidence.
/// </summary>
public sealed class GoldfishOutputPresenterTests
{
    /// <summary>
    /// Verifies generalized and Archidekt comparisons support summary, normal, and full output.
    /// </summary>
    [Fact]
    public void ComparisonPresenters_BoundDetailsAndPreserveFullResults()
    {
        GoldfishDeckComparison deck = CreateComparisonDeck("active");
        DeckGoldfishComparisonResult comparison = new()
        {
            WorkspaceId = "workspace",
            TargetTurn = 7,
            Simulations = 10,
            Seed = 42,
            Mulligan = true,
            BaselineDeck = deck,
            ComparedDecks = [CreateComparisonDeck("reference")],
            Notes = ["Deterministic fixture."],
            Warnings = ["Heuristic output."]
        };
        ArchidektGoldfishComparisonResult archidekt = new()
        {
            WorkspaceId = "workspace",
            TargetTurn = 7,
            Simulations = 10,
            Seed = 42,
            ActiveDeck = deck,
            ReferenceDecks = [CreateComparisonDeck("archidekt-reference")]
        };

        JsonElement summary = JsonSerializer.SerializeToElement(Present(comparison, "summary"));
        JsonElement normal = JsonSerializer.SerializeToElement(Present(comparison, "normal"));
        JsonElement archidektNormal = JsonSerializer.SerializeToElement(Present(archidekt, "normal"));

        summary.GetProperty("baselineDeck").GetProperty("details").ValueKind.Should().Be(JsonValueKind.Null);
        normal.GetProperty("baselineDeck").GetProperty("details").ValueKind.Should().Be(JsonValueKind.Object);
        archidektNormal.GetProperty("activeDeck").GetProperty("details").ValueKind.Should().Be(JsonValueKind.Object);
        Present(comparison, "full").Should().BeSameAs(comparison);
        Present(archidekt, "full").Should().BeSameAs(archidekt);
    }

    /// <summary>
    /// Verifies conservative race output includes samples only at normal detail.
    /// </summary>
    [Fact]
    public void RacePresenter_BoundsSamplesAndTraceEvidence()
    {
        RulesGoldfishRaceResult race = new()
        {
            Seed = 19,
            Simulations = 5,
            StartingLife = 40,
            TurnLimit = 10,
            SeatOrder = ["active"],
            SeedPolicy = "paired",
            TiePolicy = "same-turn",
            Decks =
            [
                new RulesGoldfishRaceDeckSummary
                {
                    Label = "active",
                    Seat = 1,
                    WorkspaceId = "workspace",
                    Name = "Fixture",
                    Wins = 3,
                    LethalTurnCounts = new Dictionary<int, int> { [7] = 3 },
                    RepresentativeTrace = ["Turn 7: lethal."],
                    Warnings = ["Conservative model."]
                }
            ],
            SampleOutcomes = [new RulesGoldfishRaceOutcome { Run = 0, WinnerLabel = "active" }],
            Notes = ["One", "Two", "Three"],
            Warnings = ["Not a rules engine."]
        };

        JsonElement summary = JsonSerializer.SerializeToElement(Present(race, "summary"));
        JsonElement normal = JsonSerializer.SerializeToElement(Present(race, "normal"));

        summary.GetProperty("sampleOutcomes").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("notes").GetArrayLength().Should().Be(2);
        normal.GetProperty("sampleOutcomes").GetArrayLength().Should().Be(1);
        normal.GetProperty("decks")[0].GetProperty("representativeTrace").GetArrayLength().Should().Be(1);
        Present(race, "full").Should().BeSameAs(race);
    }

    /// <summary>
    /// Verifies batch output presents each analysis area and exposes detailed goldfish evidence on request.
    /// </summary>
    [Fact]
    public void BatchPresenter_ShapesAnalysisAndGoldfishEvidence()
    {
        DeckBatchTuningReport report = new()
        {
            TargetTurn = 7,
            Simulations = 10,
            Seed = 42,
            MaxBudget = 100,
            Decks =
            [
                new DeckBatchTuningDeckReport
                {
                    WorkspaceId = "workspace",
                    Name = "Fixture",
                    Goldfish = CreateGoldfish(),
                    Risks = ["Fixture risk"]
                }
            ],
            Notes = ["Read-only evidence."]
        };

        JsonElement summary = JsonSerializer.SerializeToElement(Present(report, "summary"));
        JsonElement normal = JsonSerializer.SerializeToElement(Present(report, "normal"));

        summary.GetProperty("decks")[0].GetProperty("goldfishDetails").ValueKind.Should().Be(JsonValueKind.Null);
        normal.GetProperty("decks")[0].GetProperty("goldfishDetails").ValueKind.Should().Be(JsonValueKind.Object);
        Present(report, "full").Should().BeSameAs(report);
    }

    /// <summary>
    /// Creates a representative comparison row with deterministic simulation evidence.
    /// </summary>
    private static GoldfishDeckComparison CreateComparisonDeck(string label)
    {
        return new GoldfishDeckComparison
        {
            Label = label,
            Source = "local",
            WorkspaceId = $"{label}-workspace",
            Name = $"{label} deck",
            IncludedCards = 100,
            Goldfish = CreateGoldfish()
        };
    }

    /// <summary>
    /// Creates representative metrics with a route and turn summary for presenter branches.
    /// </summary>
    private static GoldfishSimulationResult CreateGoldfish()
    {
        return new GoldfishSimulationResult
        {
            WorkspaceId = "workspace",
            ModelLabel = "fixture-model",
            RngKind = "seeded",
            Simulations = 10,
            TargetTurn = 7,
            Mulligans = 2,
            TurnSummaries = [new ProjectedTurnState { Turn = 7, LikelyBoard = "Fixture board" }],
            WinEstimate = new WinTurnEstimate
            {
                ObservedWins = 4,
                ObservedWinRate = 0.4,
                WinByTurnRates = new Dictionary<int, double> { [7] = 0.4 },
                Routes =
                [
                    new WinRoute
                    {
                        Name = "combat",
                        Kind = "combat",
                        EarliestTurn = 7,
                        Cards = ["Fixture One", "Fixture Two"],
                        Rationale = "Fixture route."
                    }
                ]
            },
            RepresentativeLines = ["Representative line."],
            Notes = ["Seeded fixture."],
            Warnings = ["Heuristic evidence."]
        };
    }

    /// <summary>
    /// Invokes the internal overloaded presenter method for the supplied result type.
    /// </summary>
    private static object Present<T>(T result, string detailLevel)
        where T : class
    {
        Type presenter = typeof(MtgMcpHost).Assembly.GetType("MtgMcp.App.GoldfishOutputPresenter")
            ?? throw new InvalidOperationException("Goldfish presenter type was not found.");
        MethodInfo method = presenter
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == "Present"
                && candidate.GetParameters()[0].ParameterType == typeof(T));
        return method.Invoke(null, [result, detailLevel])
            ?? throw new InvalidOperationException("Goldfish presenter returned null.");
    }
}
