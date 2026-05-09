namespace MtgMcp.Core;

public sealed class DeckValidator
{
    private static readonly HashSet<string> BasicLands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plains",
        "Island",
        "Swamp",
        "Mountain",
        "Forest",
        "Wastes"
    };

    public static DeckValidationResult Validate(DeckWorkspace workspace)
    {
        DeckValidationResult result = new();
        int includedCount = CountIncludedCards(workspace);
        string format = workspace.Format.Trim().ToLowerInvariant();

        if (format is "commander" or "edh")
        {
            ValidateCommander(workspace, includedCount, result);
        }
        else if (includedCount < 60)
        {
            result.Errors.Add($"Deck has {includedCount} included cards; constructed formats usually require at least 60.");
        }

        foreach (DeckCard card in workspace.Cards)
        {
            if (card.Quantity < 1)
            {
                result.Errors.Add($"{card.Name} has a non-positive quantity.");
            }

            if (string.IsNullOrWhiteSpace(card.PrimaryCategory))
            {
                result.Errors.Add($"{card.Name} has no primary category.");
            }
        }

        return result;
    }

    private static int CountIncludedCards(DeckWorkspace workspace)
    {
        HashSet<string> includedCategories = workspace.Categories
            .Where(category => category.IncludedInDeck)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int total = 0;
        foreach (DeckCard card in workspace.Cards)
        {
            if (includedCategories.Contains(card.PrimaryCategory))
            {
                total += card.Quantity;
            }
        }

        return total;
    }

    private static void ValidateCommander(DeckWorkspace workspace, int includedCount, DeckValidationResult result)
    {
        if (includedCount != 100)
        {
            result.Warnings.Add($"Commander decks normally contain exactly 100 included cards; this workspace has {includedCount}.");
        }

        foreach (DeckCard card in workspace.Cards)
        {
            if (card.Quantity > 1 && !BasicLands.Contains(card.Name))
            {
                result.Errors.Add($"Commander singleton violation: {card.Name} has quantity {card.Quantity}.");
            }
        }
    }
}
