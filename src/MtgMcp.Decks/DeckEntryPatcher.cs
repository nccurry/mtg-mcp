using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Applies a patch of deck entries against the deck state inside the batch transaction, so the patch sees every
/// change an earlier change of the batch made and a refused patch writes nothing.
/// </summary>
internal static class DeckEntryPatcher
{
    /// <summary>
    /// Applies the patch to every listed entry, or throws before the first write when the patch is refused. A
    /// zone category the deck lacks is created once, in this transaction.
    /// </summary>
    internal static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        PatchDeckEntriesChange change,
        Func<Guid> createId,
        CancellationToken cancellationToken)
    {
        if (PatchDeckEntriesRules.FirstProblem(change) is string problem)
        {
            throw DeckInputException.Refusal(problem);
        }

        DeckDocument deck = await DeckSql.ReadDeckAsync(connection, transaction, deckId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The deck could not be read.");
        Dictionary<Guid, DeckEntry> entries = deck.Entries.ToDictionary(value => value.EntryId);
        RequireEntries(entries, change);
        RequireCategories(deck, change);
        RequirePrintings(change);
        ZoneRule zone = await ResolveZoneAsync(connection, transaction, deck, change, createId, cancellationToken)
            .ConfigureAwait(false);
        ILookup<Guid, DeckCategoryAssignment> assignments = deck.CategoryAssignments.ToLookup(value => value.EntryId);
        foreach (Guid id in change.EntryIds)
        {
            await PatchEntryAsync(
                connection,
                transaction,
                deckId,
                entries[id],
                assignments[id],
                change,
                zone,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finds or creates the zone category a zone change with match needs, once for the whole patch, and refuses a
    /// primary category that the zone change would remove again.
    /// </summary>
    private static async Task<ZoneRule> ResolveZoneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeckDocument deck,
        PatchDeckEntriesChange change,
        Func<Guid> createId,
        CancellationToken cancellationToken)
    {
        if (change.Zone is null || !DeckZoneCategories.IsMatch(change.ZoneCategories))
        {
            return ZoneRule.Leave;
        }

        HashSet<Guid> zoneIds = DeckZoneCategories.ZoneCategoryIds(deck.Categories);
        if (DeckZoneCategories.IsMain(change.Zone))
        {
            return change.PrimaryCategoryId is Guid primary && zoneIds.Contains(primary)
                ? throw DeckInputException.Refusal(DeckZoneCategories.MainPrimaryProblem)
                : new ZoneRule(true, zoneIds, null);
        }

        (DeckCategory? category, DeckCategory? created) = DeckZoneCategories.FindOrCreate(
            change.Zone,
            deck.Categories,
            createId);
        if (created is not null)
        {
            await DeckSql.InsertCategoryAsync(connection, transaction, deck.DeckId, created, cancellationToken)
                .ConfigureAwait(false);
            zoneIds.Add(created.CategoryId);
        }

        return new ZoneRule(true, zoneIds, category);
    }

    /// <summary>
    /// Writes one entry's changed fields and its category difference.
    /// </summary>
    private static async Task PatchEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckEntry current,
        IEnumerable<DeckCategoryAssignment> assigned,
        PatchDeckEntriesChange change,
        ZoneRule zone,
        CancellationToken cancellationToken)
    {
        DeckEntry updated = Patched(current, change);
        if (updated != current)
        {
            await DeckSql.UpdateEntryAsync(
                connection,
                transaction,
                deckId,
                DeckContractValidator.Normalize(updated),
                cancellationToken).ConfigureAwait(false);
        }

        Dictionary<Guid, bool> existing = assigned.ToDictionary(value => value.CategoryId, value => value.IsPrimary);
        Dictionary<Guid, bool> final = FinalAssignments(current.EntryId, existing, change, zone);
        await WriteDifferenceAsync(connection, transaction, deckId, current.EntryId, existing, final, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a patch that names an entry the deck does not have.
    /// </summary>
    private static void RequireEntries(Dictionary<Guid, DeckEntry> entries, PatchDeckEntriesChange change)
    {
        foreach (Guid id in change.EntryIds)
        {
            if (!entries.ContainsKey(id))
            {
                throw DeckInputException.Refusal($"Entry {id} was not found in this deck.");
            }
        }
    }

    /// <summary>
    /// Refuses a patch that names a category the deck does not have.
    /// </summary>
    private static void RequireCategories(DeckDocument deck, PatchDeckEntriesChange change)
    {
        HashSet<Guid> known = [.. deck.Categories.Select(value => value.CategoryId)];
        List<Guid> named = [.. change.AddCategoryIds ?? [], .. change.RemoveCategoryIds ?? []];
        if (change.PrimaryCategoryId is Guid primary)
        {
            named.Add(primary);
        }

        foreach (Guid id in named)
        {
            if (!known.Contains(id))
            {
                throw DeckInputException.Refusal($"Category {id} was not found in this deck.");
            }
        }
    }

    /// <summary>
    /// Refuses a patch whose printing was not looked up against every listed entry's card, so a printing is never
    /// written as given over an entry's identity.
    /// </summary>
    private static void RequirePrintings(PatchDeckEntriesChange change)
    {
        if (change.Printing is null)
        {
            return;
        }

        foreach (Guid id in change.EntryIds)
        {
            if (FindPrinting(change, id) is null)
            {
                throw DeckInputException.Refusal($"Entry {id} has no looked-up printing for this patch.");
            }
        }
    }

    /// <summary>
    /// Finds the printing the card data lookup gave an entry, or null when it gave none.
    /// </summary>
    private static DeckEntryPrinting? FindPrinting(PatchDeckEntriesChange change, Guid entryId)
    {
        return change.Printings?.FirstOrDefault(value => value.EntryId == entryId);
    }

    /// <summary>
    /// Gives the entry with the patch's plain fields and looked-up printing applied.
    /// </summary>
    private static DeckEntry Patched(DeckEntry entry, PatchDeckEntriesChange change)
    {
        DeckEntry updated = entry with
        {
            Quantity = change.Quantity ?? entry.Quantity,
            Zone = change.Zone ?? entry.Zone,
            SortOrder = change.SortOrder ?? entry.SortOrder,
            Finish = change.Finish ?? entry.Finish,
            Language = change.Language ?? entry.Language,
        };
        return FindPrinting(change, entry.EntryId) is DeckEntryPrinting resolved
            ? updated with
            {
                PrintingId = resolved.PrintingId,
                SetCode = resolved.SetCode,
                CollectorNumber = resolved.CollectorNumber,
                Language = change.Language ?? resolved.Language ?? updated.Language,
            }
            : updated;
    }

    /// <summary>
    /// Works out the category assignments an entry ends with: the category edits first, then, for a zone change with
    /// match, the zone category rule.
    /// </summary>
    private static Dictionary<Guid, bool> FinalAssignments(
        Guid entryId,
        Dictionary<Guid, bool> existing,
        PatchDeckEntriesChange change,
        ZoneRule zone)
    {
        Dictionary<Guid, bool> final = new(existing);
        foreach (Guid id in change.RemoveCategoryIds ?? [])
        {
            final.Remove(id);
        }

        foreach (Guid id in change.AddCategoryIds ?? [])
        {
            final.TryAdd(id, false);
        }

        if (change.PrimaryCategoryId is Guid primary)
        {
            ReplacePrimary(final, existing, primary, change.AddCategoryIds ?? []);
        }

        if (!zone.Match)
        {
            return final;
        }

        bool lostPrimary = false;
        foreach (Guid id in zone.ZoneCategoryIds)
        {
            if (final.Remove(id, out bool wasPrimary) && wasPrimary)
            {
                lostPrimary = true;
            }
        }

        if (zone.Category is not null)
        {
            foreach (Guid id in final.Keys.ToArray())
            {
                final[id] = false;
            }

            final[zone.Category.CategoryId] = true;
        }
        else if (lostPrimary && final.Count > 0 && !final.ContainsValue(true))
        {
            throw DeckInputException.Refusal(
                $"Entry {entryId} loses its primary zone category in zone main and has other categories. Give " +
                "primaryCategoryId to choose its new primary category, or remove its other categories.");
        }

        return final;
    }

    /// <summary>
    /// Makes a category the only primary one. The previous primary assignment is dropped unless the patch adds that
    /// category, which keeps it as a non-primary one.
    /// </summary>
    private static void ReplacePrimary(
        Dictionary<Guid, bool> final,
        Dictionary<Guid, bool> existing,
        Guid primary,
        IReadOnlyList<Guid> added)
    {
        foreach (Guid id in final.Keys.ToArray())
        {
            final[id] = false;
        }

        foreach ((Guid id, bool isPrimary) in existing)
        {
            if (isPrimary && id != primary && !added.Contains(id))
            {
                final.Remove(id);
            }
        }

        final[primary] = true;
    }

    /// <summary>
    /// Writes the assignments that differ: removals first, then non-primary assignments, then the primary one.
    /// </summary>
    private static async Task WriteDifferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid entryId,
        Dictionary<Guid, bool> existing,
        Dictionary<Guid, bool> final,
        CancellationToken cancellationToken)
    {
        foreach (Guid id in existing.Keys.Where(value => !final.ContainsKey(value)))
        {
            await DeckSql.UnassignCategoryAsync(connection, transaction, deckId, entryId, id, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (bool isPrimary in new[] { false, true })
        {
            foreach ((Guid id, bool value) in final)
            {
                if (value == isPrimary && (!existing.TryGetValue(id, out bool before) || before != value))
                {
                    await DeckSql.AssignCategoryAsync(
                        connection,
                        transaction,
                        deckId,
                        new DeckCategoryAssignment(entryId, id, value),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Says how a zone change treats zone categories: left alone, or matched to the new zone.
    /// </summary>
    /// <param name="Match">Whether the zone categories follow the new zone.</param>
    /// <param name="ZoneCategoryIds">Every category of the deck that stands for a zone.</param>
    /// <param name="Category">The zone category the entries end with, or null for zone main.</param>
    private readonly record struct ZoneRule(bool Match, HashSet<Guid> ZoneCategoryIds, DeckCategory? Category)
    {
        /// <summary>
        /// Gets the rule that leaves every category as it is.
        /// </summary>
        internal static ZoneRule Leave { get; } = new(false, [], null);
    }
}
