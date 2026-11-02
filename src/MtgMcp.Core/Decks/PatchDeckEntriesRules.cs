namespace MtgMcp.Core.Decks;

/// <summary>
/// Holds the rules a patch of deck entries must meet whatever the deck holds, so the caller and the store refuse the
/// same patches with the same words. Rules that need the deck's entries and categories live in the store.
/// </summary>
public static class PatchDeckEntriesRules
{
    /// <summary>
    /// Gives the most entries one patch may name.
    /// </summary>
    public const int MaxEntries = 100;

    /// <summary>
    /// Returns the first reason the patch is refused, or null when it is acceptable.
    /// </summary>
    public static string? FirstProblem(PatchDeckEntriesChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return EntryIdsProblem(change.EntryIds) ??
            FieldProblem(change) ??
            PrintingProblem(change.Printing) ??
            CategoryProblem(change) ??
            ZoneProblem(change);
    }

    /// <summary>
    /// Checks a list of entry IDs a change names: 1 to 100 distinct, non-empty IDs. Returns the first problem, or
    /// null when the list is usable.
    /// </summary>
    public static string? EntryIdsProblem(IReadOnlyList<Guid>? entryIds)
    {
        if (entryIds is not { Count: >= 1 and <= MaxEntries })
        {
            return $"A patch-entries change needs 1 to {MaxEntries} entry IDs.";
        }

        HashSet<Guid> seen = [];
        foreach (Guid id in entryIds)
        {
            if (id == Guid.Empty)
            {
                return "An entry ID is invalid.";
            }

            if (!seen.Add(id))
            {
                return $"Entry ID {id} is listed more than once.";
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that the patch changes something and that each plain field it sets is usable.
    /// </summary>
    private static string? FieldProblem(PatchDeckEntriesChange change)
    {
        if (change.Quantity is null && change.Zone is null && change.SortOrder is null && change.Finish is null &&
            change.Language is null && change.Printing is null && change.PrimaryCategoryId is null &&
            change.AddCategoryIds is not { Count: > 0 } && change.RemoveCategoryIds is not { Count: > 0 })
        {
            return "A patch-entries change must set at least one field besides entryIds and zoneCategories.";
        }

        if (change.Quantity <= 0)
        {
            return "Entry quantity must be positive.";
        }

        if (change.Zone is not null && string.IsNullOrWhiteSpace(change.Zone))
        {
            return "Zone must not be blank.";
        }

        if (change.Finish is not null && string.IsNullOrWhiteSpace(change.Finish))
        {
            return "Finish must not be blank.";
        }

        return change.Language is not null && string.IsNullOrWhiteSpace(change.Language)
            ? "Language must not be blank."
            : null;
    }

    /// <summary>
    /// Checks that the printing group is either a printing ID or a set code with a collector number.
    /// </summary>
    private static string? PrintingProblem(DeckPrintingPatch? printing)
    {
        if (printing is null)
        {
            return null;
        }

        bool hasSet = !string.IsNullOrWhiteSpace(printing.SetCode);
        bool hasNumber = !string.IsNullOrWhiteSpace(printing.CollectorNumber);
        bool valid = printing.PrintingId is Guid id
            ? !hasSet && !hasNumber && id != Guid.Empty
            : hasSet && hasNumber;
        return valid
            ? null
            : "The printing must be a printingId alone, or a setCode with a collectorNumber.";
    }

    /// <summary>
    /// Checks the category IDs: none empty, and none named twice across or within the three category fields.
    /// </summary>
    private static string? CategoryProblem(PatchDeckEntriesChange change)
    {
        List<Guid> named = [.. change.AddCategoryIds ?? [], .. change.RemoveCategoryIds ?? []];
        if (change.PrimaryCategoryId is Guid primary)
        {
            named.Add(primary);
        }

        HashSet<Guid> seen = [];
        foreach (Guid id in named)
        {
            if (id == Guid.Empty)
            {
                return "A category ID is invalid.";
            }

            if (!seen.Add(id))
            {
                return $"Category ID {id} is named more than once in primaryCategoryId, addCategoryIds, and " +
                    "removeCategoryIds; name it in one field.";
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that a zone and its zone categories mode go together and agree with the other category fields.
    /// </summary>
    private static string? ZoneProblem(PatchDeckEntriesChange change)
    {
        if (change.Zone is null)
        {
            return change.ZoneCategories is null
                ? null
                : "zoneCategories applies only when the change sets a zone.";
        }

        if (!DeckZoneCategories.IsMode(change.ZoneCategories))
        {
            return "A change that sets a zone must also set zoneCategories to \"match\" or \"leave\".";
        }

        return DeckZoneCategories.IsMatch(change.ZoneCategories)
            ? DeckZoneCategories.MatchProblem(change.Zone, change.PrimaryCategoryId)
            : null;
    }
}
