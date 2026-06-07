namespace MtgMcp.Core;

/// <summary>
/// Matches deck-intent protection entries against cards that should not be casual cuts.
/// </summary>
public static class DeckIntentProtection
{
    /// <summary>
    /// Checks whether a card is protected by deck intent.
    /// </summary>
    public static bool IsProtectedCard(DeckCard card, DeckIntent? intent)
    {
        if (intent is null)
        {
            return false;
        }

        foreach (string value in intent.Protect)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string protectedValue = value.Trim();
            if (protectedValue.Equals("commander", StringComparison.OrdinalIgnoreCase)
                && IsCommanderCard(card))
            {
                return true;
            }

            if (card.Name.Equals(protectedValue, StringComparison.OrdinalIgnoreCase)
                || card.Name.Contains(protectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a card is categorized as a commander.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
                DeckRoles.Commander,
                StringComparison.OrdinalIgnoreCase)
            || DeckCategoryOrdering.HasCategory(card, DeckRoles.Commander);
    }
}
