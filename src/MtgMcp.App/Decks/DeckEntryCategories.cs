using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Looks up the category names of deck entries, so every tool that shows or ranks categories reads them the same way.
/// </summary>
internal static class DeckEntryCategories
{
    /// <summary>
    /// Maps each entry ID to the names of its assigned categories: the primary category first, then the others in
    /// name order. An entry with no category is not in the map.
    /// </summary>
    internal static Dictionary<Guid, IReadOnlyList<string>> NamesByEntry(DeckDocument deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        Dictionary<Guid, string> names = deck.Categories.ToDictionary(
            category => category.CategoryId,
            category => category.Name);
        Dictionary<Guid, IReadOnlyList<string>> byEntry = [];
        foreach (IGrouping<Guid, DeckCategoryAssignment> group in deck.CategoryAssignments
            .Where(assignment => names.ContainsKey(assignment.CategoryId))
            .GroupBy(assignment => assignment.EntryId))
        {
            byEntry[group.Key] =
            [
                .. group
                    .OrderByDescending(assignment => assignment.IsPrimary)
                    .ThenBy(assignment => names[assignment.CategoryId], StringComparer.Ordinal)
                    .Select(assignment => names[assignment.CategoryId]),
            ];
        }

        return byEntry;
    }

    /// <summary>
    /// Maps each entry ID to the name of its primary category. An entry with no primary category is not in the map.
    /// </summary>
    internal static Dictionary<Guid, string> PrimaryByEntry(DeckDocument deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        Dictionary<Guid, string> names = deck.Categories.ToDictionary(
            category => category.CategoryId,
            category => category.Name);
        Dictionary<Guid, string> primaries = [];
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (assignment.IsPrimary && names.TryGetValue(assignment.CategoryId, out string? name))
            {
                primaries[assignment.EntryId] = name;
            }
        }

        return primaries;
    }
}
