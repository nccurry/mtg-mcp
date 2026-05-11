namespace MtgMcp.Core;

/// <summary>
/// Applies deck inclusion rules from a card's primary category.
/// </summary>
internal static class DeckCategoryInclusion
{
    /// <summary>
    /// Builds a case-insensitive lookup of workspace categories.
    /// </summary>
    internal static Dictionary<string, DeckCategory> BuildCategoryMap(DeckWorkspace workspace)
    {
        return workspace.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the card's primary category contributes to the active deck.
    /// </summary>
    internal static bool IsIncludedInDeck(DeckWorkspace workspace, DeckCard card)
    {
        return IsIncludedInDeck(BuildCategoryMap(workspace), DeckCategoryOrdering.PrimaryCategory(card));
    }

    /// <summary>
    /// Checks whether the card's primary category contributes to the active deck.
    /// </summary>
    internal static bool IsIncludedInDeck(
        IReadOnlyDictionary<string, DeckCategory> categories,
        DeckCard card)
    {
        return IsIncludedInDeck(categories, DeckCategoryOrdering.PrimaryCategory(card));
    }

    /// <summary>
    /// Checks whether the named primary category contributes to the active deck.
    /// </summary>
    internal static bool IsIncludedInDeck(
        IReadOnlyDictionary<string, DeckCategory> categories,
        string primaryCategory)
    {
        return !categories.TryGetValue(primaryCategory, out DeckCategory? category)
            || category.IncludedInDeck;
    }

    /// <summary>
    /// Enumerates cards whose primary categories contribute to the active deck.
    /// </summary>
    internal static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        Dictionary<string, DeckCategory> categories = BuildCategoryMap(workspace);
        foreach (DeckCard card in workspace.Cards)
        {
            if (IsIncludedInDeck(categories, card))
            {
                yield return card;
            }
        }
    }
}
