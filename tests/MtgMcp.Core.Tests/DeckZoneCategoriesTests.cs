using MtgMcp.Core.Decks;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the zone category names, the match rules, and the lookup and creation of a zone category.
/// </summary>
public sealed class DeckZoneCategoriesTests
{
    /// <summary>
    /// Verifies match is accepted for main and for a zone with a category, and refused for another zone or an
    /// explicit primary category, with the same message each time.
    /// </summary>
    [Fact]
    public void MatchProblem_ChecksTheZoneAndThePrimaryCategory()
    {
        Guid primary = Guid.CreateVersion7();

        Assert.Null(DeckZoneCategories.MatchProblem(DeckZoneCategories.MainZone, primary));
        Assert.Null(DeckZoneCategories.MatchProblem(" Sideboard ", null));
        Assert.Equal(
            "zoneCategories \"match\" has no category for zone 'tokens'; use \"leave\".",
            DeckZoneCategories.MatchProblem(" tokens ", null));
        Assert.StartsWith(
            "primaryCategoryId cannot be set with zoneCategories \"match\"",
            DeckZoneCategories.MatchProblem(DeckZoneCategories.SideboardZone, primary),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a zone category is found by name ignoring case and space, created only when missing, and never made
    /// for main or an unknown zone.
    /// </summary>
    [Fact]
    public void FindOrCreate_ReusesCreatesOrGivesNothing()
    {
        DeckCategory existing = new(Guid.CreateVersion7(), " sideboard ", null, 3);
        DeckCategory other = new(Guid.CreateVersion7(), "Ramp", null, 0);
        Guid next = Guid.CreateVersion7();

        Assert.Equal(
            (existing, (DeckCategory?)null),
            DeckZoneCategories.FindOrCreate(DeckZoneCategories.SideboardZone, [other, existing], () => next));

        (DeckCategory? category, DeckCategory? created) =
            DeckZoneCategories.FindOrCreate(DeckZoneCategories.MaybeboardZone, [other], () => next);
        Assert.Equal(new DeckCategory(next, DeckZoneCategories.Maybeboard, null, 0), category);
        Assert.Same(category, created);

        Assert.Equal(
            ((DeckCategory?)null, (DeckCategory?)null),
            DeckZoneCategories.FindOrCreate(DeckZoneCategories.MainZone, [other], () => next));
        Assert.Equal(
            ((DeckCategory?)null, (DeckCategory?)null),
            DeckZoneCategories.FindOrCreate("tokens", [other], () => next));
    }

    /// <summary>
    /// Verifies the category lookups by name and the set of zone category IDs.
    /// </summary>
    [Fact]
    public void FindByNameAndZoneCategoryIds_ReadTheCategories()
    {
        DeckCategory commander = new(Guid.CreateVersion7(), "COMMANDER", null, 0);
        DeckCategory ramp = new(Guid.CreateVersion7(), "Ramp", null, 1);

        Assert.Equal(
            ramp.CategoryId,
            DeckZoneCategories.FindByName([commander, ramp with { Name = " RAMP " }], "Ramp")?.CategoryId);
        Assert.Null(DeckZoneCategories.FindByName([commander], "Ramp"));
        Assert.Equal([commander.CategoryId], DeckZoneCategories.ZoneCategoryIds([commander, ramp]));
    }
}
