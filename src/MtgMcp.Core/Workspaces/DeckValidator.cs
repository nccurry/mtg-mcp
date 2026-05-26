namespace MtgMcp.Core;

/// <summary>
/// Provides deck validator behavior.
/// </summary>
public sealed class DeckValidator
{
    /// <summary>
    /// Lists card names exempt from singleton copy limits.
    /// </summary>
    private static readonly HashSet<string> BasicLands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains",
        "Island",
        "Swamp",
        "Mountain",
        "Forest",
        "Wastes",
    };

    /// <summary>
    /// Validates the workspace.
    /// </summary>
    public static DeckValidationResult Validate(DeckWorkspace workspace)
    {
        DeckValidationResult result = new();
        List<DeckCard> includedCards = DeckCategoryInclusion.IncludedCards(workspace).ToList();
        int includedCount = CountIncludedCards(includedCards);
        string format = workspace.Format.Trim().ToLowerInvariant();

        if (format is "commander" or "edh")
        {
            ValidateCommander(includedCards, includedCount, result);
        }
        else if (includedCount < 60)
        {
            result.Errors.Add(
                $"Deck has {includedCount} included cards; constructed formats usually require at least 60."
            );
        }

        foreach (DeckCard card in workspace.Cards)
        {
            if (card.Quantity < 1)
            {
                result.Errors.Add($"{card.Name} has a non-positive quantity.");
            }

            if (string.IsNullOrWhiteSpace(DeckCategoryOrdering.PrimaryCategory(card)))
            {
                result.Errors.Add($"{card.Name} has no primary category.");
            }
        }

        return result;
    }

    /// <summary>
    /// Counts the included cards.
    /// </summary>
    private static int CountIncludedCards(IEnumerable<DeckCard> includedCards)
    {
        int total = 0;
        foreach (DeckCard card in includedCards)
        {
            total += card.Quantity;
        }

        return total;
    }

    /// <summary>
    /// Validates the commander.
    /// </summary>
    private static void ValidateCommander(
        IEnumerable<DeckCard> includedCards,
        int includedCount,
        DeckValidationResult result
    )
    {
        if (includedCount != 100)
        {
            result.Warnings.Add(
                $"Commander decks normally contain exactly 100 included cards; this workspace has {includedCount}."
            );
        }

        foreach (DeckCard card in includedCards)
        {
            if (card.Quantity > 1 && !BasicLands.Contains(card.Name))
            {
                result.Errors.Add(
                    $"Commander singleton violation: {card.Name} has quantity {card.Quantity}."
                );
            }
        }
    }
}
