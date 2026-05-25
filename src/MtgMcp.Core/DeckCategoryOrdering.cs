namespace MtgMcp.Core;

/// <summary>
/// Keeps category arrays ordered so index zero is the card's primary category.
/// </summary>
public static class DeckCategoryOrdering
{
    /// <summary>
    /// Normalizes a card's category list and synchronizes its primary category mirror.
    /// </summary>
    public static void Normalize(DeckCard card, string fallbackPrimary = DeckDefaults.Mainboard)
    {
        card.Categories = OrderedDistinct(PrimaryCategory(card, fallbackPrimary), card.Categories);
        card.PrimaryCategory = card.Categories[0];
    }

    /// <summary>
    /// Returns the primary category from the ordered category array, falling back to the legacy mirror.
    /// </summary>
    public static string PrimaryCategory(DeckCard card, string fallbackPrimary = DeckDefaults.Mainboard)
    {
        string? firstCategory = card.Categories?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(firstCategory))
        {
            return firstCategory.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.PrimaryCategory))
        {
            return card.PrimaryCategory.Trim();
        }

        return NormalizeCategoryName(fallbackPrimary);
    }

    /// <summary>
    /// Moves a category to the front of the ordered category list.
    /// </summary>
    public static void SetPrimary(DeckCard card, string category)
    {
        string normalizedCategory = NormalizeCategoryName(category);
        card.Categories = OrderedDistinct(normalizedCategory, card.Categories);
        card.PrimaryCategory = card.Categories[0];
    }

    /// <summary>
    /// Appends a secondary category while leaving the current primary category in place.
    /// </summary>
    public static void AddSecondary(DeckCard card, string category)
    {
        string primary = PrimaryCategory(card);
        List<string> categories = OrderedDistinct(primary, card.Categories);
        string normalizedCategory = NormalizeCategoryName(category);
        if (!categories.Any(value => value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            categories.Add(normalizedCategory);
        }

        card.Categories = categories;
        card.PrimaryCategory = card.Categories[0];
    }

    /// <summary>
    /// Removes a category and promotes the next category when the primary category is removed.
    /// </summary>
    public static void Remove(DeckCard card, string category, string fallbackPrimary = DeckDefaults.Mainboard)
    {
        string normalizedCategory = NormalizeCategoryName(category);
        List<string> categories = OrderedDistinct(PrimaryCategory(card, fallbackPrimary), card.Categories)
            .Where(value => !value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (categories.Count == 0)
        {
            categories.Add(NormalizeCategoryName(fallbackPrimary));
        }

        card.Categories = categories;
        card.PrimaryCategory = card.Categories[0];
    }

    /// <summary>
    /// Replaces category names without changing the current primary ordering.
    /// </summary>
    public static void Replace(DeckCard card, string oldCategory, string newCategory)
    {
        string normalizedOldCategory = NormalizeCategoryName(oldCategory);
        string normalizedNewCategory = NormalizeCategoryName(newCategory);
        List<string> categories = [];
        foreach (string category in OrderedDistinct(PrimaryCategory(card), card.Categories))
        {
            categories.Add(category.Equals(normalizedOldCategory, StringComparison.OrdinalIgnoreCase)
                ? normalizedNewCategory
                : category);
        }

        card.Categories = OrderedDistinct(categories.FirstOrDefault() ?? normalizedNewCategory, categories);
        card.PrimaryCategory = card.Categories[0];
    }

    /// <summary>
    /// Checks whether a card has a primary or secondary category.
    /// </summary>
    public static bool HasCategory(DeckCard card, string category)
    {
        string normalizedCategory = NormalizeCategoryName(category);
        return OrderedDistinct(PrimaryCategory(card), card.Categories)
            .Any(value => value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds an ordered, duplicate-free category list with the requested primary category first.
    /// </summary>
    public static List<string> OrderedDistinct(string primaryCategory, IEnumerable<string?>? categories)
    {
        string normalizedPrimary = NormalizeCategoryName(primaryCategory);
        List<string> result = [normalizedPrimary];

        foreach (string? category in categories ?? [])
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                continue;
            }

            string normalizedCategory = category.Trim();
            if (!result.Any(value => value.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(normalizedCategory);
            }
        }

        return result;
    }

    /// <summary>
    /// Converts empty category names into the default mainboard category.
    /// </summary>
    public static string NormalizeCategoryName(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? DeckDefaults.Mainboard : category.Trim();
    }
}
