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
}
