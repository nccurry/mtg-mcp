using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Works out and writes the category assignments a new entry's draft asks for, so a new entry needs no second change
/// to be categorized.
/// </summary>
internal static class DeckDraftCategories
{
    /// <summary>
    /// Works out the assignments of a new entry against the categories that exist. When the zone categories mode is
    /// match and the zone has a zone category the deck lacks, the category is created and added to the list.
    /// Throws when a category is unknown or the draft's categories contradict each other.
    /// </summary>
    internal static IReadOnlyList<DeckCategoryAssignment> Plan(
        DeckNewEntry added,
        List<DeckCategory> categories,
        Func<Guid> createId)
    {
        List<Guid> ids = [.. added.CategoryIds];
        Guid? primary = added.PrimaryCategoryId;
        if (DeckZoneCategories.IsMatch(added.ZoneCategories))
        {
            string zone = added.Entry.Zone;
            ids.RemoveAll(id => IsZoneCategory(categories, id));
            if (DeckZoneCategories.IsMain(zone))
            {
                if (primary is Guid named && IsZoneCategory(categories, named))
                {
                    throw new DeckInputException(
                        "primaryCategoryId names a zone category, but the entry is in zone main and " +
                        "zoneCategories is \"match\".");
                }
            }
            else
            {
                primary = ZoneCategoryFor(added, categories, createId).CategoryId;
            }
        }

        foreach (Guid id in primary is Guid value ? [.. ids, value] : ids)
        {
            if (!categories.Exists(category => category.CategoryId == id))
            {
                throw new DeckInputException($"Category {id} was not found in this deck.");
            }
        }

        List<DeckCategoryAssignment> assignments = [];
        foreach (Guid id in ids)
        {
            if (id != primary)
            {
                assignments.Add(new DeckCategoryAssignment(added.Entry.EntryId, id, false));
            }
        }

        if (primary is Guid primaryId)
        {
            assignments.Add(new DeckCategoryAssignment(added.Entry.EntryId, primaryId, true));
        }

        return assignments;
    }

    /// <summary>
    /// Finds the zone category for the new entry's zone, creating and listing it when the deck lacks it.
    /// </summary>
    private static DeckCategory ZoneCategoryFor(DeckNewEntry added, List<DeckCategory> categories, Func<Guid> createId)
    {
        if (added.PrimaryCategoryId is not null)
        {
            throw new DeckInputException(
                "primaryCategoryId cannot be set with zoneCategories \"match\" for a zone that has a zone category; " +
                "the zone category becomes the primary category.");
        }

        string name = DeckZoneCategories.NameForZone(added.Entry.Zone) ?? throw new DeckInputException(
            $"zoneCategories \"match\" has no category for zone '{added.Entry.Zone}'; use \"leave\".");
        DeckCategory? existing = FindByName(categories, name);
        if (existing is not null)
        {
            return existing;
        }

        DeckCategory created = new(createId(), name, null, 0);
        categories.Add(created);
        return created;
    }

    /// <summary>
    /// Finds a category by name, ignoring case and surrounding space, in the deck's category order.
    /// </summary>
    internal static DeckCategory? FindByName(IReadOnlyList<DeckCategory> categories, string name)
    {
        foreach (DeckCategory category in categories)
        {
            if (string.Equals(category.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether the category with the given ID is a zone category.
    /// </summary>
    private static bool IsZoneCategory(IReadOnlyList<DeckCategory> categories, Guid id)
    {
        foreach (DeckCategory category in categories)
        {
            if (category.CategoryId == id)
            {
                return DeckZoneCategories.ZoneForName(category.Name) is not null;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes the categories of an entry the batch has just inserted, creating the zone category when it is missing.
    /// </summary>
    internal static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckNewEntry added,
        Func<Guid> createId,
        CancellationToken cancellationToken)
    {
        List<DeckCategory> categories = [.. await DeckSql.ReadCategoriesAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false)];
        int known = categories.Count;
        IReadOnlyList<DeckCategoryAssignment> assignments = Plan(added, categories, createId);
        if (categories.Count > known)
        {
            await DeckSql.InsertCategoryAsync(
                connection,
                transaction,
                deckId,
                categories[^1],
                cancellationToken).ConfigureAwait(false);
        }

        foreach (DeckCategoryAssignment assignment in assignments)
        {
            await DeckSql.AssignCategoryAsync(connection, transaction, deckId, assignment, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
