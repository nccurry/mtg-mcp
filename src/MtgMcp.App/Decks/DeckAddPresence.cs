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
    /// Checks every change in order, as the batch will apply them, and returns the batch without the adds to leave
    /// out. Skipping applies to every card, basic lands included, because the caller asked for it.
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
                present.RemoveAll(value => value.EntryId == update.Entry.EntryId);
                present.Add(Present.From(update.Entry, []));
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
            Present? sameZone = skipPresent
                ? present.Find(value => SameCard(value, draft) && SameZone(value.Zone, draft.Zone))
                : null;
            if (sameZone is not null)
            {
                issues.Add(new DeckWriteIssue(
                    DeckWriteIssue.Skipped,
                    $"'{draft.CardName.Trim()}' is already in zone {sameZone.Zone} as {Describe(sameZone)}; " +
                    "not added.",
                    sameZone.EntryId,
                    draft.CardName.Trim()));
                if (draft.EntryId is Guid skippedId)
                {
                    skippedById[skippedId] = new Skipped(index + 1, draft.CardName.Trim(), sameZone);
                }

                continue;
            }

            Present? other = BasicLandNames.Contains(draft.CardName)
                ? null
                : present.Find(value => SameCard(value, draft));
            if (other is not null)
            {
                issues.Add(AlreadyInDeck(draft, other));
            }

            present.Add(Present.FromDraft(draft));
            kept.Add(change);
        }

        return new DeckAddPresenceResult(kept, issues, null);
    }

    /// <summary>
    /// Builds the issue for an add whose card is already in the deck.
    /// </summary>
    private static DeckWriteIssue AlreadyInDeck(DeckEntryDraft draft, Present existing)
    {
        string name = draft.CardName.Trim();
        string fix = existing.EntryId is Guid id
            ? $"to change copies use update-entry on {id}, or remove-entry on the new entry."
            : "to change copies use update-entry on the first add's entry, or remove-entry on the new entry.";
        return new DeckWriteIssue(
            DeckWriteIssue.AlreadyInDeck,
            $"'{name}' was already in the deck as {Describe(existing)} (zone {existing.Zone}, " +
            $"{existing.Quantity} copies, categories: {existing.Categories}). This add made another entry; {fix}",
            existing.EntryId,
            name);
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
        string existing = Describe(skipped.Existing);
        return $"Change {changeNumber} refers to the entry of change {skipped.ChangeNumber}, an add-entry for " +
            $"'{skipped.CardName}' that whenCardPresent \"skip\" left out because the card is already in the same " +
            $"zone as {existing}. Nothing was written. Refer to {existing} instead, or use whenCardPresent \"add\".";
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
    /// Tells whether an entry and a new draft are the same card: the same Oracle ID when both have one, otherwise
    /// the same name. Printings never matter.
    /// </summary>
    private static bool SameCard(Present value, DeckEntryDraft draft)
    {
        return value.OracleId is Guid left && draft.OracleId is Guid right
            ? left == right
            : CardNames.Match(value.CardName, draft.CardName);
    }

    /// <summary>
    /// Tells whether two zone names are the same, ignoring case.
    /// </summary>
    private static bool SameZone(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists the entries the deck holds before the batch, with the names of their categories.
    /// </summary>
    private static List<Present> FromDeck(DeckDocument deck)
    {
        Dictionary<Guid, string> categoryNames =
            deck.Categories.ToDictionary(value => value.CategoryId, value => value.Name);
        List<Present> present = new(deck.Entries.Count);
        foreach (DeckEntry entry in deck.Entries)
        {
            List<string> names = [];
            foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
            {
                if (assignment.EntryId == entry.EntryId &&
                    categoryNames.TryGetValue(assignment.CategoryId, out string? name))
                {
                    names.Add(name);
                }
            }

            names.Sort(StringComparer.Ordinal);
            present.Add(Present.From(entry, names));
        }

        return present;
    }

    /// <summary>
    /// Holds what a message needs to know about one entry that is in the deck or was added earlier in the batch.
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
        internal static Present From(DeckEntry entry, List<string> categories)
        {
            return new Present(
                entry.EntryId,
                entry.CardName,
                entry.OracleId,
                entry.Zone,
                entry.Quantity,
                categories.Count == 0 ? "none" : string.Join(", ", categories));
        }

        /// <summary>
        /// Builds the record for an entry the batch adds; its categories are not known yet.
        /// </summary>
        internal static Present FromDraft(DeckEntryDraft draft)
        {
            return new Present(draft.EntryId, draft.CardName, draft.OracleId, draft.Zone, draft.Quantity, "none");
        }
    }

    /// <summary>
    /// Holds an add that was left out, so a later change that refers to it can be refused by name.
    /// </summary>
    private sealed record Skipped(int ChangeNumber, string CardName, Present Existing);
}
