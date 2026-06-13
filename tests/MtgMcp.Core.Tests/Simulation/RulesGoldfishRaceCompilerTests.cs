using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies snapshot-to-race-template compilation.
/// </summary>
public sealed class RulesGoldfishRaceCompilerTests
{
    /// <summary>
    /// Verifies that v1 templates compile from provider-neutral snapshots and unsupported text warns.
    /// </summary>
    [Fact]
    public void CompileDeck_MapsSupportedTemplatesAndWarnings()
    {
        DeckWorkspace workspace = new()
        {
            Id = "workspace-compiler",
            Name = "Compiler",
            Cards =
            [
                Card("Forest", 5, DeckRoles.Lands, "Basic Land - Forest", "", producedMana: ["G"]),
                Card("Sol Ring", 1, DeckRoles.Ramp, "Artifact", "{T}: Add {C}{C}.", manaValue: 1, producedMana: ["C"]),
                Card("Llanowar Elves", 1, DeckRoles.Ramp, "Creature - Elf Druid", "{T}: Add {G}.", manaValue: 1, power: "1", toughness: "1", producedMana: ["G"]),
                Card("Grizzly Bears", 1, DeckRoles.Utility, "Creature - Bear", "", manaValue: 2, power: "2", toughness: "2"),
                Card("Elvish Visionary", 1, DeckRoles.Draw, "Creature - Elf Shaman", "When this creature enters, draw a card.", manaValue: 2, power: "1", toughness: "1"),
                Card("Wood Elves", 1, DeckRoles.Ramp, "Creature - Elf Scout", "When this creature enters, search your library for a basic land card.", manaValue: 3, power: "1", toughness: "1"),
                Card("Token Maker", 1, DeckRoles.Synergy, "Creature - Human", "When this creature enters, create two 1/1 creature tokens.", manaValue: 3, power: "2", toughness: "2"),
                Card("Drain Spell", 1, DeckRoles.Wincons, "Sorcery", "Each opponent loses 3 life.", manaValue: 3),
                Card("Skyhunter Strike Force", 1, DeckRoles.Wincons, "Creature - Cat Knight", "Melee. Other creatures you control have melee.", manaValue: 3, power: "2", toughness: "2"),
                Card("Blade Historian", 1, DeckRoles.Wincons, "Creature - Human Cleric", "Attacking creatures you control have double strike.", manaValue: 4, power: "2", toughness: "3"),
                Card("Aurelia", 1, DeckRoles.Commander, "Legendary Creature - Angel", "Flying, vigilance, haste.", manaValue: 4, power: "3", toughness: "4"),
                Card("Counterspell", 1, DeckRoles.Interaction, "Instant", "Counter target spell.", manaValue: 2),
            ]
        };

        RulesGoldfishRaceDeck deck = RulesGoldfishRaceCompiler.CompileDeck(workspace, "active");

        deck.Label.Should().Be("active");
        deck.CommandZoneCards.Should().ContainSingle(card => card.Name == "Aurelia");
        deck.Cards.Should().NotContain(card => card.Name == "Aurelia");
        deck.Cards.Single(card => card.Name == "Forest").ManaProduced.Should().Be(1);
        deck.Cards.Single(card => card.Name == "Sol Ring").ManaProduced.Should().Be(1);
        deck.Cards.Single(card => card.Name == "Llanowar Elves").ManaSourceIsCreature.Should().BeTrue();
        deck.Cards.Single(card => card.Name == "Grizzly Bears").Power.Should().Be(2);
        deck.Cards.Single(card => card.Name == "Elvish Visionary").DrawCards.Should().Be(1);
        deck.Cards.Single(card => card.Name == "Wood Elves").RampLands.Should().Be(1);
        deck.Cards.Single(card => card.Name == "Token Maker").CreateTokens.Should().Be(2);
        deck.Cards.Single(card => card.Name == "Drain Spell").LifeLoss.Should().Be(3);
        deck.Cards.Single(card => card.Name == "Skyhunter Strike Force").TeamPowerBonus.Should().Be(1);
        deck.Cards.Single(card => card.Name == "Skyhunter Strike Force").IsCombatPayoff.Should().BeTrue();
        deck.Cards.Single(card => card.Name == "Blade Historian").GrantsTeamDoubleStrike.Should().BeTrue();
        deck.Warnings.Should().Contain(warning => warning.Contains("Counterspell", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that sparse nonland snapshots are not treated as zero-cost spells.
    /// </summary>
    [Fact]
    public void CompileDeck_MissingNonlandManaValueIsUncastable()
    {
        DeckWorkspace workspace = new()
        {
            Id = "workspace-missing-cost",
            Name = "Missing Cost",
            Cards =
            [
                Card("Plains", 3, DeckRoles.Lands, "Basic Land - Plains", "", producedMana: ["W"]),
                new DeckCard
                {
                    Name = "Mystery Beater",
                    Quantity = 1,
                    PrimaryCategory = DeckRoles.Wincons,
                    Categories = [DeckRoles.Wincons],
                    Snapshot = new CardSnapshot
                    {
                        TypeLine = "Creature - Avatar",
                        OracleText = "",
                        Power = "20",
                        Toughness = "20",
                    },
                },
            ],
        };

        RulesGoldfishRaceDeck deck = RulesGoldfishRaceCompiler.CompileDeck(workspace, "missing-cost");

        RulesGoldfishRaceCard beater = deck.Cards.Single(card => card.Name == "Mystery Beater");
        beater.CanBeCast.Should().BeFalse();
        deck.Warnings.Should().Contain(warning =>
            warning.Contains("Mystery Beater", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("mana value is missing", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that compiled templates can feed the race kernel deterministically.
    /// </summary>
    [Fact]
    public void CompileDeck_FeedsRaceKernel()
    {
        DeckWorkspace fast = new()
        {
            Id = "fast",
            Name = "Fast",
            Cards =
            [
                Card("Forest", 3, DeckRoles.Lands, "Basic Land - Forest", "", producedMana: ["G"]),
                Card("Attacker", 1, DeckRoles.Wincons, "Creature - Cat", "", manaValue: 1, power: "2", toughness: "2"),
            ]
        };
        DeckWorkspace slow = new()
        {
            Id = "slow",
            Name = "Slow",
            Cards =
            [
                Card("Forest", 3, DeckRoles.Lands, "Basic Land - Forest", "", producedMana: ["G"]),
                Card("Attacker", 1, DeckRoles.Wincons, "Creature - Cat", "", manaValue: 1, power: "1", toughness: "1"),
            ]
        };

        RulesGoldfishRaceResult result = RulesGoldfishRaceSimulator.Run(new RulesGoldfishRaceRequest
        {
            Seed = 2,
            Simulations = 2,
            StartingLife = 4,
            TurnLimit = 5,
            Mulligan = false,
            Decks =
            [
                RulesGoldfishRaceCompiler.CompileDeck(fast, "fast"),
                RulesGoldfishRaceCompiler.CompileDeck(slow, "slow"),
            ]
        });

        result.Decks.Single(deck => deck.Label == "fast").Wins.Should().Be(2);
        result.Decks.Single(deck => deck.Label == "slow").Losses.Should().Be(2);
    }

    /// <summary>
    /// Creates a workspace card with snapshot facts.
    /// </summary>
    private static DeckCard Card(
        string name,
        int quantity,
        string category,
        string typeLine,
        string oracleText,
        double? manaValue = 0,
        string? power = null,
        string? toughness = null,
        IReadOnlyList<string>? producedMana = null)
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
                OracleText = oracleText,
                ManaValue = manaValue,
                Power = power,
                Toughness = toughness,
                ProducedMana = producedMana?.ToList() ?? [],
            }
        };
    }
}
