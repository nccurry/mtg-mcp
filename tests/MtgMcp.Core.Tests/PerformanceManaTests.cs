using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains oracle tests for Stats Lab mana payment modeling.
/// </summary>
public sealed class PerformanceManaTests
{
    /// <summary>
    /// Verifies that one flexible source cannot satisfy two colored requirements at once.
    /// </summary>
    [Fact]
    public void TryPay_RequiresExclusiveSourceForEachColoredRequirement()
    {
        DeckCard spell = Spell("Two-Color Answer", "{W}{U}", 2, ["W", "U"]);
        List<PerformanceManaSource> sources =
        [
            new(["W", "U"]),
            new(["C"]),
        ];

        bool paid = PerformanceMana.TryPay(spell, sources, out _);

        paid.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that two distinct flexible sources can satisfy a two-color cost.
    /// </summary>
    [Fact]
    public void TryPay_AllowsDistinctSourcesForColoredRequirements()
    {
        DeckCard spell = Spell("Two-Color Answer", "{W}{U}", 2, ["W", "U"]);
        List<PerformanceManaSource> sources =
        [
            new(["W", "U"]),
            new(["U"]),
        ];

        bool paid = PerformanceMana.TryPay(spell, sources, out List<PerformanceManaSource> remaining);

        paid.Should().BeTrue();
        remaining.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that spent colored sources cannot be reused for later payments.
    /// </summary>
    [Fact]
    public void TryPay_ReturnsRemainingSourcesAfterSpending()
    {
        DeckCard blueSpell = Spell("Blue Play", "{U}", 1, ["U"]);
        List<PerformanceManaSource> sources =
        [
            new(["U"]),
            new(["C"]),
        ];

        bool firstPayment = PerformanceMana.TryPay(blueSpell, sources, out List<PerformanceManaSource> remaining);
        bool secondPayment = PerformanceMana.TryPay(blueSpell, remaining, out _);

        firstPayment.Should().BeTrue();
        secondPayment.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that colorless symbols require a colorless-producing source.
    /// </summary>
    [Fact]
    public void TryPay_RequiresColorlessSourceForColorlessSymbol()
    {
        DeckCard colorlessSpell = Spell("Spatial Break", "{C}", 1, []);
        List<PerformanceManaSource> whiteSources = [new(["W"])];
        List<PerformanceManaSource> colorlessSources = [new(["C"])];

        PerformanceMana.TryPay(colorlessSpell, whiteSources, out _).Should().BeFalse();
        PerformanceMana.TryPay(colorlessSpell, colorlessSources, out _).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Wastes fallback data can produce colorless mana.
    /// </summary>
    [Fact]
    public void ReadProducedMana_InfersColorlessFromWastes()
    {
        DeckCard wastes = new()
        {
            Name = "Wastes",
            Snapshot = new CardSnapshot { TypeLine = "Basic Land" },
        };

        PerformanceMana.ReadProducedMana(wastes).Should().ContainSingle(symbol => symbol == "C");
    }

    /// <summary>
    /// Verifies that fallback text inference keeps produced colors distinct.
    /// </summary>
    [Fact]
    public void ReadProducedMana_DeduplicatesBasicLandFallbacks()
    {
        DeckCard dualLand = new()
        {
            Name = "Benchmark Plains Forest",
            Snapshot = new CardSnapshot
            {
                TypeLine = "Land - Plains Forest",
                OracleText = "Benchmark Plains Forest enters tapped.",
            },
        };

        PerformanceMana.ReadProducedMana(dualLand).Should().BeEquivalentTo(["W", "G"]);
    }

    /// <summary>
    /// Verifies that MDFC land slots use color identity as a conservative back-face mana fallback.
    /// </summary>
    [Fact]
    public void ReadProducedMana_InfersModalDoubleFacedLandSlotColor()
    {
        DeckCard malakir = new()
        {
            Name = "Malakir Rebirth // Malakir Mire",
            PrimaryCategory = DeckRoles.Lands,
            Categories = [DeckRoles.Lands],
            Snapshot = new CardSnapshot
            {
                TypeLine = "Instant // Land",
                OracleText = "Choose target creature. You lose 2 life.",
                ColorIdentity = ["B"],
            },
        };

        PerformanceMana.ReadProducedMana(malakir).Should().ContainSingle(symbol => symbol == "B");
        PerformanceMana.LooksTapped(malakir.Snapshot).Should().BeTrue();

        malakir.PrimaryCategory = DeckDefaults.Mainboard;
        malakir.Categories = [DeckDefaults.Mainboard];
        PerformanceMana.ReadProducedMana(malakir).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that conditional taplands are tap-pressure for analysis but not always unavailable in simulation.
    /// </summary>
    [Fact]
    public void ClassifyTapTiming_SplitsAlwaysConditionalAndUntappedLands()
    {
        DeckCard temple = Land("Temple of Deceit", "Land", "");
        DeckCard gate = Land("Dimir Guildgate", "Land - Gate", "");
        DeckCard triome = Land("Indatha Triome", "Land - Plains Swamp Forest", "");
        DeckCard shock = Land(
            "Watery Grave",
            "Land - Island Swamp",
            "As Watery Grave enters, you may pay 2 life. If you don't, it enters tapped.");
        DeckCard check = Land(
            "Drowned Catacomb",
            "Land",
            "Drowned Catacomb enters the battlefield tapped unless you control an Island or a Swamp.");
        DeckCard slow = Land(
            "Shipwreck Marsh",
            "Land",
            "Shipwreck Marsh enters the battlefield tapped unless you control two or more other lands.");
        DeckCard pathway = Land("Clearwater Pathway // Murkwater Pathway", "Land // Land", "");

        PerformanceMana.ClassifyTapTiming(temple).Should().Be(LandTapTiming.AlwaysTapped);
        PerformanceMana.ClassifyTapTiming(gate).Should().Be(LandTapTiming.AlwaysTapped);
        PerformanceMana.ClassifyTapTiming(triome).Should().Be(LandTapTiming.AlwaysTapped);
        PerformanceMana.ClassifyTapTiming(shock).Should().Be(LandTapTiming.ConditionalTapped);
        PerformanceMana.ClassifyTapTiming(check).Should().Be(LandTapTiming.ConditionalTapped);
        PerformanceMana.ClassifyTapTiming(slow).Should().Be(LandTapTiming.ConditionalTapped);
        PerformanceMana.ClassifyTapTiming(pathway).Should().Be(LandTapTiming.Untapped);
        PerformanceMana.LooksTapped(shock).Should().BeFalse();
        PerformanceMana.LooksTapped(pathway).Should().BeFalse();
    }

    /// <summary>
    /// Creates a nonland test card with cached mana data.
    /// </summary>
    private static DeckCard Spell(
        string name,
        string manaCost,
        double manaValue,
        List<string> colorIdentity)
    {
        return new DeckCard
        {
            Name = name,
            PrimaryCategory = DeckDefaults.Mainboard,
            Snapshot = new CardSnapshot
            {
                ManaCost = manaCost,
                ManaValue = manaValue,
                TypeLine = "Instant",
                ColorIdentity = colorIdentity,
            },
        };
    }

    /// <summary>
    /// Creates a land card with cached text.
    /// </summary>
    private static DeckCard Land(string name, string typeLine, string oracleText)
    {
        return new DeckCard
        {
            Name = name,
            PrimaryCategory = DeckRoles.Lands,
            Categories = [DeckRoles.Lands],
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                OracleText = oracleText,
            },
        };
    }
}
