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
    /// match and the zone has a zone category the deck lacks, that category is returned as created for the caller to
    /// store and to add to the categories before the next draft. Throws when a category is unknown or the draft's
    /// categories contradict each other.
    /// </summary>
    internal static (IReadOnlyList<DeckCategoryAssignment> Assignments, DeckCategory? Created) Plan(
        DeckNewEntry added,
        IReadOnlyList<DeckCategory> categories,
        Func<Guid> createId)
    {
        List<Guid> ids = [.. added.CategoryIds];
        Guid? primary = added.PrimaryCategoryId;
        string zone = added.Entry.Zone;
        bool match = DeckZoneCategories.IsMatch(added.ZoneCategories);
        if (match)
        {
            HashSet<Guid> zoneCategories = DeckZoneCategories.ZoneCategoryIds(categories);
            ids.RemoveAll(zoneCategories.Contains);
            if (DeckZoneCategories.MatchProblem(zone, primary) is string problem)
            {
                throw DeckInputException.Refusal(problem);
            }

            if (DeckZoneCategories.IsMain(zone) && primary is Guid named && zoneCategories.Contains(named))
            {
                throw DeckInputException.Refusal(DeckZoneCategories.MainPrimaryProblem);
            }
        }

        foreach (Guid id in primary is Guid value ? [.. ids, value] : ids)
        {
            if (!categories.Any(category => category.CategoryId == id))
            {
                throw DeckInputException.Refusal($"Category {id} was not found in this deck.");
            }
        }

        DeckCategory? created = null;
        if (match)
        {
            (DeckCategory? zoneCategory, created) = DeckZoneCategories.FindOrCreate(zone, categories, createId);
            primary = zoneCategory?.CategoryId ?? primary;
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

        return (assignments, created);
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
        IReadOnlyList<DeckCategory> categories = await DeckSql.ReadCategoriesAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<DeckCategoryAssignment> assignments, DeckCategory? created) = Plan(added, categories, createId);
        if (created is not null)
        {
            await DeckSql.InsertCategoryAsync(connection, transaction, deckId, created, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (DeckCategoryAssignment assignment in assignments)
        {
            await DeckSql.AssignCategoryAsync(connection, transaction, deckId, assignment, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
