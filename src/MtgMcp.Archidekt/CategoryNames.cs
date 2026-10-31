namespace MtgMcp.Archidekt;

/// <summary>
/// Finds categories by name when two categories of one deck may differ only by case, as Archidekt allows. An exact
/// match always wins; a name that matches no category exactly is matched without regard to case only when that
/// leaves exactly one category.
/// </summary>
internal static class CategoryNames
{
    /// <summary>
    /// Indexes items by their exact name; the first item wins for a repeated exact name.
    /// </summary>
    internal static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> name)
    {
        Dictionary<string, T> index = new(StringComparer.Ordinal);
        foreach (T item in items)
        {
            index.TryAdd(name(item), item);
        }

        return index;
    }

    /// <summary>
    /// Tells whether a primary category name is the category name being checked: exact text, or the same
    /// ignoring case when no other category of the deck has the exact primary name.
    /// </summary>
    internal static bool SameName<T>(string? primary, string name, IReadOnlyDictionary<string, T> index)
    {
        if (primary is null)
        {
            return false;
        }

        return primary.Equals(name, StringComparison.Ordinal) ||
            (!index.ContainsKey(primary) && primary.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Looks a name up: exact first, then case-insensitively when that finds exactly one category.
    /// </summary>
    internal static bool TryFind<T>(IReadOnlyDictionary<string, T> index, string name, out T value)
    {
        if (index.TryGetValue(name, out T? exact))
        {
            value = exact;
            return true;
        }

        List<T> loose = [.. index.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)];
        if (loose.Count == 1)
        {
            value = loose[0];
            return true;
        }

        value = default!;
        return false;
    }
}
