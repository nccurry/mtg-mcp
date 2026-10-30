namespace MtgMcp.Archidekt;

/// <summary>
/// Defines once how two Archidekt entries are compared. Archidekt may return an entry's category names in another
/// order and renumbers entry and category order numbers without any user edit, so the primary category is kept apart,
/// the other category names count as a set, and the order numbers are ignored. Difference rows, fingerprints, push
/// plans, and push verification all use this one form.
/// </summary>
internal static class ArchidektEntryComparison
{
    /// <summary>
    /// Returns the entry in comparison form: the primary category first, the other category names sorted, and the
    /// order number cleared. The result is for comparing and showing, never for sending to Archidekt.
    /// </summary>
    internal static RemoteDeckEntry Canonical(RemoteDeckEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        List<string> names = OtherNames(entry, StringComparison.Ordinal);
        names.Sort(StringComparer.Ordinal);
        if (entry.PrimaryCategoryName is not null)
        {
            names.Insert(0, entry.PrimaryCategoryName);
        }

        return entry with { CategoryNames = names, SortOrder = 0 };
    }

    /// <summary>
    /// Tells whether two entries are in the same categories: the same primary category, and the same other category
    /// names in any order. Names are compared exactly, or without regard to case when asked.
    /// </summary>
    internal static bool SameCategories(RemoteDeckEntry left, RemoteDeckEntry right, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(left.PrimaryCategoryName, right.PrimaryCategoryName, comparison))
        {
            return false;
        }

        List<string> leftOthers = OtherNames(left, comparison);
        List<string> rightOthers = OtherNames(right, comparison);
        StringComparer comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        leftOthers.Sort(comparer);
        rightOthers.Sort(comparer);
        return leftOthers.SequenceEqual(rightOthers, comparer);
    }

    /// <summary>
    /// Lists the category names of an entry other than its primary category.
    /// </summary>
    private static List<string> OtherNames(RemoteDeckEntry entry, StringComparison comparison)
    {
        List<string> others = [];
        foreach (string name in entry.CategoryNames)
        {
            if (!string.Equals(name, entry.PrimaryCategoryName, comparison))
            {
                others.Add(name);
            }
        }

        return others;
    }
}
