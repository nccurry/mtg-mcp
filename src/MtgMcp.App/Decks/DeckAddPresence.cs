using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Holds a batch after its adds were checked against the cards already in the deck, the issues found, and a reason
/// the whole batch must be refused, or null when it may go on.
/// </summary>
internal sealed record DeckAddPresenceResult(
    IReadOnlyList<DeckChange> Changes,
    IReadOnlyList<DeckWriteIssue> Issues,
    string? Refusal);

/// <summary>
/// Checks each entry a batch adds against the entries the deck already holds and the entries added earlier in the
/// same batch. A match is reported so a caller who repeats an add sees it; when the caller asks for it, the repeated
/// add is left out instead.
/// </summary>
internal static class DeckAddPresence
{
    /// <summary>
    /// Names the parameter value that adds every entry and reports repeats.
    /// </summary>
    internal const string Add = "add";

    /// <summary>
    /// Names the parameter value that leaves out an add whose card is already in the same zone.
    /// </summary>
    internal const string Skip = "skip";

    /// <summary>
    /// Holds the text a message gives for an entry that has no category.
    /// </summary>
    private const string NoCategories = "none";

    /// <summary>
    /// Checks every change in order, as the batch will apply them, and returns the batch without the adds to leave
    /// out. Skipping applies to every card, basic lands included, because the caller asked for it. An add is left
    /// out only when the entry it matches is still in the same zone after the whole batch.
    /// </summary>
    internal static DeckAddPresenceResult Check(DeckDocument deck, IReadOnlyList<DeckChange> changes, bool skipPresent)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(changes);
        List<Present> present = FromDeck(deck);
        Dictionary<Guid, Skipped> skippedById = [];
        List<DeckChange> kept = new(changes.Count);
        List<DeckWriteIssue> issues = [];
        for (int index = 0; index < changes.Count; index++)
        {
            DeckChange change = changes[index];
            if (ReferencedEntry(change) is Guid referenced && skippedById.TryGetValue(referenced, out Skipped? gone))
            {
                return new DeckAddPresenceResult(changes, [], Refusal(index + 1, gone));
            }

            if (change is UpdateDeckEntryChange update)
            {
                Present? before = present.Find(value => value.EntryId == update.Entry.EntryId);
                present.RemoveAll(value => value.EntryId == update.Entry.EntryId);
                present.Add(Present.From(update.Entry, before?.Categories ?? NoCategories));
            }
            else if (change is RemoveDeckEntryChange remove)
            {
                present.RemoveAll(value => value.EntryId == remove.EntryId);
            }

            if (change is not AddDeckEntryChange add)
            {
                kept.Add(change);
                continue;
            }

            DeckEntryDraft draft = add.Entry;
            Present added = Present.FromDraft(draft);
            Present? sameZone = skipPresent
                ? present.Find(value => SameCard(value, added) && SameZone(value, added) &&
                    StaysInZone(changes, index, value, added))
                : null;
            if (sameZone is not null)
            {
                if (DeckEntryDraftRules.FirstProblem(draft) is string problem)
                {
                    return new DeckAddPresenceResult(
                        changes,
                        [],
                        $"Change {index + 1} is an add-entry that cannot be written: {problem} Nothing was written.");
                }

                issues.Add(new DeckWriteIssue(
                    DeckWriteIssue.Skipped,
                    $"'{added.CardName}' is already in zone {sameZone.Zone} as {Describe(sameZone)}; not added.",
                    sameZone.EntryId,
                    added.CardName));

                // When the add carried the ID of the entry it matched, a later change that names that ID already
                // targets the right entry, so there is nothing to refuse.
                if (draft.EntryId is Guid skippedId && skippedId != sameZone.EntryId)
                {
                    skippedById[skippedId] = new Skipped(index + 1, added.CardName, sameZone);
                }

                continue;
            }

            Present? other = BasicLandNames.Contains(added.CardName)
                ? null
                : present.Find(value => SameCard(value, added));
            if (other is not null)
            {
                issues.Add(AlreadyInDeck(added, other));
            }

            present.Add(added);
            kept.Add(change);
        }

        return new DeckAddPresenceResult(kept, issues, null);
    }

    /// <summary>
    /// Tells whether an entry is still the same card in the same zone once the changes after the add have run. A
    /// later remove-entry of it, or an update-entry that moves it out of the zone or makes it another card, means it
    /// does not stay, so leaving out the add would lose the card.
    /// </summary>
    private static bool StaysInZone(IReadOnlyList<DeckChange> changes, int addIndex, Present existing, Present added)
    {
        if (existing.EntryId is not Guid id)
        {
            return true;
        }

        bool stays = true;
        for (int index = addIndex + 1; index < changes.Count; index++)
        {
            if (changes[index] is RemoveDeckEntryChange remove && remove.EntryId == id)
            {
                stays = false;
            }
            else if (changes[index] is UpdateDeckEntryChange update && update.Entry.EntryId == id)
            {
                Present updated = Present.From(update.Entry, NoCategories);
                stays = SameCard(updated, added) && SameZone(updated, added);
            }
        }

        return stays;
    }

    /// <summary>
    /// Builds the issue for an add whose card is already in the deck.
    /// </summary>
    private static DeckWriteIssue AlreadyInDeck(Present added, Present existing)
    {
        string fix = existing.EntryId is Guid id
            ? $"to change copies use update-entry on {id}, or remove-entry on the new entry."
            : "to change copies use update-entry on the first add's entry, or remove-entry on the new entry.";
        return new DeckWriteIssue(
            DeckWriteIssue.AlreadyInDeck,
            $"'{added.CardName}' was already in the deck as {Describe(existing)} (zone {existing.Zone}, " +
            $"{existing.Quantity} copies, categories: {existing.Categories}). This add made another entry; {fix}",
            existing.EntryId,
            added.CardName);
    }

    /// <summary>
    /// Names an entry for a message: its ID, or an earlier add of this batch that has no ID yet.
    /// </summary>
    private static string Describe(Present value)
    {
        return value.EntryId is Guid id ? $"entry {id}" : "an earlier add in this batch";
    }

    /// <summary>
    /// Builds the reason a batch is refused when a change refers to an add that was left out.
    /// </summary>
    private static string Refusal(int changeNumber, Skipped skipped)
    {
        string fix = skipped.Existing.EntryId is Guid id
            ? $"Refer to entry {id} instead"
            : "Give the earlier add an entryId and refer to it";
        return $"Change {changeNumber} refers to the entry of change {skipped.ChangeNumber}, an add-entry for " +
            $"'{skipped.CardName}' that whenCardPresent \"skip\" left out because the card is already in the same " +
            $"zone as {Describe(skipped.Existing)}. Nothing was written. {fix}, or use whenCardPresent \"add\".";
    }

    /// <summary>
    /// Gives the entry ID an entry-level change refers to, or null for a change that does not name an entry.
    /// </summary>
    private static Guid? ReferencedEntry(DeckChange change)
    {
        return change switch
        {
            UpdateDeckEntryChange update => update.Entry.EntryId,
            RemoveDeckEntryChange remove => remove.EntryId,
            AssignDeckCategoryChange assign => assign.EntryId,
            UnassignDeckCategoryChange unassign => unassign.EntryId,
            _ => null,
        };
    }

    /// <summary>
    /// Tells whether two entries are the same card: the same Oracle ID when both have one, otherwise the same name.
    /// Printings never matter.
    /// </summary>
    private static bool SameCard(Present left, Present right)
    {
        return left.OracleId is Guid first && right.OracleId is Guid second
            ? first == second
            : CardNames.Match(left.CardName, right.CardName);
    }

    /// <summary>
    /// Tells whether two entries are in the same zone, ignoring letter case.
    /// </summary>
    private static bool SameZone(Present left, Present right)
    {
        return string.Equals(left.Zone, right.Zone, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists the entries the deck holds before the batch, with the names of their categories.
    /// </summary>
    private static List<Present> FromDeck(DeckDocument deck)
    {
        Dictionary<Guid, IReadOnlyList<string>> categories = DeckEntryCategories.NamesByEntry(deck);
        List<Present> present = new(deck.Entries.Count);
        foreach (DeckEntry entry in deck.Entries)
        {
            present.Add(Present.From(
                entry,
                categories.TryGetValue(entry.EntryId, out IReadOnlyList<string>? names)
                    ? string.Join(", ", names)
                    : NoCategories));
        }

        return present;
    }

    /// <summary>
    /// Trims text the way the store does, treating a missing value as empty.
    /// </summary>
    private static string Trimmed(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Holds what a message needs to know about one entry that is in the deck or was added earlier in the batch. The
    /// card name and zone are trimmed, as the store trims them.
    /// </summary>
    private sealed record Present(
        Guid? EntryId,
        string CardName,
        Guid? OracleId,
        string Zone,
        int Quantity,
        string Categories)
    {
        /// <summary>
        /// Builds the record for an entry of the deck.
        /// </summary>
        internal static Present From(DeckEntry entry, string categories)
        {
            return new Present(
                entry.EntryId,
                Trimmed(entry.CardName),
                entry.OracleId,
                Trimmed(entry.Zone),
                entry.Quantity,
                categories);
        }

        /// <summary>
        /// Builds the record for an entry the batch adds; its categories are not known yet.
        /// </summary>
        internal static Present FromDraft(DeckEntryDraft draft)
        {
            return new Present(
                draft.EntryId,
                Trimmed(draft.CardName),
                draft.OracleId,
                Trimmed(draft.Zone),
                draft.Quantity,
                NoCategories);
        }
    }

    /// <summary>
    /// Holds an add that was left out, so a later change that refers to it can be refused by name.
    /// </summary>
    private sealed record Skipped(int ChangeNumber, string CardName, Present Existing);
}
