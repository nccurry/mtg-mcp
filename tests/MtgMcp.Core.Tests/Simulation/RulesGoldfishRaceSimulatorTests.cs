using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the conservative template-based goldfish race kernel.
/// </summary>
public sealed class RulesGoldfishRaceSimulatorTests
{
    /// <summary>
    /// Verifies that paired runs are deterministic and report explicit race semantics.
    /// </summary>
    [Fact]
    public void Run_ReplaysDeterministicallyAndReportsRaceSemantics()
    {
        RulesGoldfishRaceRequest request = new()
        {
            Seed = 77,
            Simulations = 3,
            StartingLife = 4,
            TurnLimit = 5,
            TraceLimit = 3,
            FirstPlayerDraws = false,
            Mulligan = false,
            Decks =
            [
                RaceDeck("fast", "Fast", creaturePower: 2),
                RaceDeck("slow", "Slow", creaturePower: 1),
            ]
        };

        RulesGoldfishRaceResult first = RulesGoldfishRaceSimulator.Run(request);
        RulesGoldfishRaceResult second = RulesGoldfishRaceSimulator.Run(request);

        first.ModelName.Should().Be(RulesGoldfishRaceConstants.ModelName);
        first.EngineVersion.Should().Be(RulesGoldfishRaceConstants.EngineVersion);
        first.RandomKind.Should().Be(DeterministicSimulationRandom.Kind);
        first.FirstPlayerDraws.Should().BeFalse();
        first.CommanderDamageIgnored.Should().BeTrue();
        first.SeatOrder.Should().Equal("fast", "slow");
        first.Decks[0].Wins.Should().Be(3);
        first.Decks[0].MedianLethalTurn.Should().Be(3);
        first.Decks[0].RepresentativeTrace.Should().HaveCountLessThanOrEqualTo(3);
        first.Decks[1].Losses.Should().Be(3);
        first.SampleOutcomes.Should().OnlyContain(outcome => outcome.WinnerLabel == "fast");
        second.Decks[0].Wins.Should().Be(first.Decks[0].Wins);
        second.Decks[0].MedianLethalTurn.Should().Be(first.Decks[0].MedianLethalTurn);
        second.Decks[0].RepresentativeTrace.Should().Equal(first.Decks[0].RepresentativeTrace);
    }

    /// <summary>
    /// Verifies that same-turn lethal is a tie rather than an arbitrary seat-order win.
    /// </summary>
    [Fact]
    public void Run_RecordsSameTurnLethalAsTie()
    {
        RulesGoldfishRaceRequest request = new()
        {
            Seed = 12,
            Simulations = 2,
            StartingLife = 2,
            TurnLimit = 3,
            Mulligan = false,
            Decks =
            [
                RaceDeck("first", "First", creaturePower: 2),
                RaceDeck("second", "Second", creaturePower: 2),
            ]
        };

        RulesGoldfishRaceResult result = RulesGoldfishRaceSimulator.Run(request);

        result.Decks.Should().OnlyContain(deck => deck.Ties == 2 && deck.Wins == 0);
        result.SampleOutcomes.Should().OnlyContain(outcome =>
            outcome.WinnerLabel == null
            && outcome.TiedLabels.Contains("first")
            && outcome.TiedLabels.Contains("second")
            && outcome.LethalTurn == 2);
        result.TiePolicy.Should().Contain("Same-turn lethal");
    }

    /// <summary>
    /// Verifies that command-zone cards are cast from outside the library and commander damage is not modeled.
    /// </summary>
    [Fact]
    public void Run_CastsCommandZoneCardsAndIgnoresCommanderDamage()
    {
        RulesGoldfishRaceDeck commanderDeck = RaceDeck("commander", "Commander", creaturePower: 0);
        commanderDeck.CommandZoneCards.Add(new RulesGoldfishRaceCard
        {
            Name = "Test Commander",
            Quantity = 1,
            ManaValue = 1,
            IsCreature = true,
            StaysOnBattlefield = true,
            Power = 2,
            Toughness = 2,
        });
        RulesGoldfishRaceRequest request = new()
        {
            Seed = 1,
            Simulations = 1,
            StartingLife = 2,
            TurnLimit = 3,
            Mulligan = false,
            Decks =
            [
                commanderDeck,
                RaceDeck("slow", "Slow", creaturePower: 1),
            ]
        };

        RulesGoldfishRaceResult result = RulesGoldfishRaceSimulator.Run(request);
        RulesGoldfishRaceDeckSummary commander = result.Decks.Single(deck => deck.Label == "commander");

        commander.Wins.Should().Be(1);
        commander.MedianLethalTurn.Should().Be(2);
        commander.RepresentativeTrace.Should().Contain(line => line.Contains("from command zone", StringComparison.OrdinalIgnoreCase));
        result.CommanderDamageIgnored.Should().BeTrue();
        result.Notes.Should().Contain(note => note.Contains("Commander damage is ignored", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that templates marked uncastable are skipped even when their mana value is zero.
    /// </summary>
    [Fact]
    public void Run_DoesNotCastUncastableTemplates()
    {
        RulesGoldfishRaceDeck unknownCostDeck = RaceDeck("unknown", "Unknown", creaturePower: 0);
        unknownCostDeck.Cards.Add(new RulesGoldfishRaceCard
        {
            Name = "Mystery Beater",
            Quantity = 1,
            ManaValue = 0,
            CanBeCast = false,
            IsCreature = true,
            StaysOnBattlefield = true,
            Power = 20,
            Toughness = 20,
        });
        RulesGoldfishRaceRequest request = new()
        {
            Seed = 5,
            Simulations = 1,
            StartingLife = 2,
            TurnLimit = 3,
            Mulligan = false,
            Decks =
            [
                unknownCostDeck,
                RaceDeck("slow", "Slow", creaturePower: 1),
            ],
        };

        RulesGoldfishRaceResult result = RulesGoldfishRaceSimulator.Run(request);

        result.Decks.Single(deck => deck.Label == "unknown").Wins.Should().Be(0);
        result.Decks.Single(deck => deck.Label == "slow").Wins.Should().Be(1);
    }

    /// <summary>
    /// Creates a tiny deterministic race deck.
    /// </summary>
    private static RulesGoldfishRaceDeck RaceDeck(string label, string name, int creaturePower)
    {
        return new RulesGoldfishRaceDeck
        {
            Label = label,
            WorkspaceId = label,
            Name = name,
            Cards =
            [
                new RulesGoldfishRaceCard
                {
                    Name = "Plains",
                    Quantity = 3,
                    IsLand = true,
                    ManaProduced = 1,
                },
                new RulesGoldfishRaceCard
                {
                    Name = $"{name} Creature",
                    Quantity = 1,
                    ManaValue = 1,
                    IsCreature = true,
                    StaysOnBattlefield = true,
                    Power = creaturePower,
                    Toughness = 1,
                }
            ]
        };
    }
}
