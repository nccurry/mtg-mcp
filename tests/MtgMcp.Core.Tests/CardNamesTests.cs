using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Checks which card names count as the same card when Archidekt, Scryfall, and a caller may name a double-faced card
/// by all of its faces or by one.
/// </summary>
public sealed class CardNamesTests
{
    /// <summary>
    /// Verifies names that are equal, equal without regard to case, or a single face of the full name match.
    /// </summary>
    [Theory]
    [InlineData("Lightning Bolt", "lightning bolt")]
    [InlineData("Delver of Secrets // Insectile Aberration", "Delver of Secrets")]
    [InlineData("Delver of Secrets", "Delver of Secrets // Insectile Aberration")]
    [InlineData("delver of secrets // insectile aberration", "Insectile Aberration")]
    [InlineData("Fire // Ice", " fire ")]
    public void Match_SameCardInEitherForm_IsTrue(string left, string right)
    {
        Assert.True(CardNames.Match(left, right));
    }

    /// <summary>
    /// Verifies different cards, part of a face, a missing name, and a name that only starts like a face do not match.
    /// </summary>
    [Theory]
    [InlineData("Lightning Bolt", "Chain Lightning")]
    [InlineData("Delver of Secrets // Insectile Aberration", "Delver")]
    [InlineData("Delver of Secrets // Insectile Aberration", "Lightning Bolt")]
    [InlineData("Fire // Ice", "Fire // Water")]
    [InlineData("Lightning Bolt", null)]
    public void Match_DifferentCards_IsFalse(string left, string? right)
    {
        Assert.False(CardNames.Match(left, right));
    }
}
