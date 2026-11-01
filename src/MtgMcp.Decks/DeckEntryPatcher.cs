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
    /// Names the reason code of every refusal of a patch.
    /// </summary>
    private const string Reason = "invalid-deck-change";

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
            throw Refuse(problem);
        }

        DeckDocument deck = await DeckSql.ReadDeckAsync(connection, transaction, deckId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The deck could not be read.");
        Dictionary<Guid, DeckEntry> entries = deck.Entries.ToDictionary(value => value.EntryId);
        foreach (Guid id in change.EntryIds)
        {
            if (!entries.ContainsKey(id))
            {
                throw Refuse($"Entry {id} was not found in this deck.");
            }
        }

        RequireCategories(deck, change);
        string? zone = change.Zone?.Trim().ToLowerInvariant();
        bool match = zone is not null && DeckZoneCategories.IsMatch(change.ZoneCategories);
        List<DeckCategory> categories = [.. deck.Categories];
        DeckCategory? zoneCategory = null;
        if (match && !DeckZoneCategories.IsMain(zone))
        {
            string name = DeckZoneCategories.NameForZone(zone)!;
            zoneCategory = DeckDraftCategories.FindByName(categories, name);
            if (zoneCategory is null)
            {
                zoneCategory = new DeckCategory(createId(), name, null, 0);
                categories.Add(zoneCategory);
                await DeckSql.InsertCategoryAsync(connection, transaction, deckId, zoneCategory, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        HashSet<Guid> zoneIds = [.. categories
            .Where(value => DeckZoneCategories.ZoneForName(value.Name) is not null)
            .Select(value => value.CategoryId)];
        ILookup<Guid, DeckCategoryAssignment> assignments = deck.CategoryAssignments.ToLookup(value => value.EntryId);
        foreach (Guid id in change.EntryIds)
        {
            DeckEntry current = entries[id];
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

            Dictionary<Guid, bool> existing = assignments[id].ToDictionary(value => value.CategoryId, value => value.IsPrimary);
            Dictionary<Guid, bool> final = FinalAssignments(id, existing, change, match ? zoneIds : null, zoneCategory);
            await WriteDifferenceAsync(connection, transaction, deckId, id, existing, final, cancellationToken)
                .ConfigureAwait(false);
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
                throw Refuse($"Category {id} was not found in this deck.");
            }
        }
    }

    /// <summary>
    /// Gives the entry with the patch's plain fields and printing applied.
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
        if (change.Printing is null)
        {
            return updated;
        }

        DeckEntryPrinting? resolved = change.Printings?.FirstOrDefault(value => value.EntryId == entry.EntryId);
        return resolved is null
            ? updated with
            {
                PrintingId = change.Printing.PrintingId,
                SetCode = change.Printing.SetCode,
                CollectorNumber = change.Printing.CollectorNumber,
            }
            : updated with
            {
                PrintingId = resolved.PrintingId,
                SetCode = resolved.SetCode,
                CollectorNumber = resolved.CollectorNumber,
                Language = change.Language ?? resolved.Language ?? updated.Language,
            };
    }

    /// <summary>
    /// Works out the category assignments an entry ends with: the category edits first, then, for a zone change with
    /// match, the zone category rule. The zone IDs are null when zone categories are left alone.
    /// </summary>
    private static Dictionary<Guid, bool> FinalAssignments(
        Guid entryId,
        Dictionary<Guid, bool> existing,
        PatchDeckEntriesChange change,
        HashSet<Guid>? zoneIds,
        DeckCategory? zoneCategory)
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

        if (zoneIds is null)
        {
            return final;
        }

        bool lostPrimary = false;
        foreach (Guid id in zoneIds)
        {
            if (final.Remove(id, out bool wasPrimary) && wasPrimary)
            {
                lostPrimary = true;
            }
        }

        if (zoneCategory is not null)
        {
            foreach (Guid id in final.Keys.ToArray())
            {
                final[id] = false;
            }

            final[zoneCategory.CategoryId] = true;
        }
        else if (lostPrimary && final.Count > 0 && !final.ContainsValue(true))
        {
            throw Refuse(
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
    /// Builds the refusal of a patch, which rolls the whole batch back.
    /// </summary>
    private static DeckInputException Refuse(string message)
    {
        return new DeckInputException(message + " Nothing was written.", Reason);
    }
}
