using MtgMcp.Core.Decks;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies local validation reports structural evidence without semantic inference.
/// </summary>
public sealed class DeckValidatorTests
{
    /// <summary>
    /// Verifies every documented local invariant is reported with stable ordering.
    /// </summary>
    [Fact]
    public void Validate_WithMalformedGraph_ReportsAllLocalDefects()
    {
        Guid deckId = Guid.CreateVersion7();
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckDocument deck = new(
            deckId,
            "Malformed",
            string.Empty,
            "commander",
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [
                new DeckEntry(entryId, 0, "First", null, null, null, null, "en", "nonfoil", string.Empty, 0),
                new DeckEntry(entryId, 1, "Second", null, null, null, null, "en", "nonfoil", "main", 0),
            ],
            [new DeckCategory(categoryId, "Category", null, 0)],
            [
                new DeckCategoryAssignment(entryId, categoryId, true),
                new DeckCategoryAssignment(entryId, Guid.CreateVersion7(), true),
            ],
            []);

        DeckValidationReport result = DeckValidator.Validate(deck);

        Assert.False(result.IsStructurallyValid);
        Assert.Equal(
            [
                "duplicate-entry-id",
                "invalid-category-reference",
                "invalid-entry-quantity",
                "invalid-entry-zone",
                "multiple-primary-categories",
            ],
            result.Issues.Select(value => value.ReasonCode));
    }

    /// <summary>
    /// Verifies all deck formats use the same relational checks.
    /// </summary>
    [Fact]
    public void Validate_WithCustomFormatAndValidGraph_ReturnsValid()
    {
        DeckDocument deck = new(
            Guid.CreateVersion7(),
            "Custom",
            string.Empty,
            "future-format",
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            []);

        DeckValidationReport result = DeckValidator.Validate(deck);

        Assert.True(result.IsStructurallyValid);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies a Commander label does not imply a required zone or legality rule.
    /// </summary>
    [Fact]
    public void Validate_WithCommanderLabelAndEmptyGraph_ReturnsValid()
    {
        DeckDocument deck = new(
            Guid.CreateVersion7(),
            "Format-neutral",
            string.Empty,
            "commander",
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            []);

        DeckValidationReport result = DeckValidator.Validate(deck);

        Assert.True(result.IsStructurallyValid);
        Assert.Empty(result.Issues);
    }
}
