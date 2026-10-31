using MtgMcp.Archidekt;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Writes the sentences a push or pull preview and apply give when they refuse or describe a change. It does no input
/// or output and holds no state, so the text of every refusal is in one place, next to the removal guard.
/// </summary>
internal static class ArchidektSyncNotices
{
    /// <summary>
    /// Sets the most names or cards a notice lists before it says how many more there are.
    /// </summary>
    private const int MaximumListed = 5;

    /// <summary>
    /// Explains why a pull is refused over a deck whose baseline an earlier version recorded, and how to leave.
    /// </summary>
    private const string UnverifiedPullConflictMessage =
        "The local deck may have changed since its synchronization baseline. The earlier version that made the " +
        "baseline did not record category colors or ordering, so the server cannot tell whether they changed. " +
        "Push the deck to keep the local content (the push is refused too if Archidekt also changed), or preview " +
        "and apply the pull with discardLocalChanges to take the Archidekt state, which drops the local edits.";

    /// <summary>
    /// Explains why a pull cannot go ahead over the local changes since the last sync, or returns null when it can.
    /// A pull keeps cards and categories added locally; anything else needs <c>discardLocalChanges</c>.
    /// </summary>
    internal static string? DescribePullConflict(ArchidektPullKeep? keep, IReadOnlyList<ArchidektDifference> differences)
    {
        if (keep is null)
        {
            return differences.Any(value => value.State == ArchidektDifferenceStates.LocalChangedUnverified)
                ? UnverifiedPullConflictMessage
                : "The local deck changed since its synchronization baseline in a way a pull cannot keep: a pull " +
                    "keeps cards and categories added locally and replaces everything else from Archidekt. Preview " +
                    "the pull with discardLocalChanges to take the Archidekt state instead, which drops the local " +
                    "edits.";
        }

        if (keep.DuplicateCardNames.Count > 0)
        {
            return "Archidekt also added " +
                ListFirst(keep.DuplicateCardNames, value => $"'{value}'", ", ") + ", which was added locally too, " +
                "so a pull would leave two entries for the same card. Remove the local copy and pull again, or " +
                "preview the pull with discardLocalChanges to take the Archidekt state, which drops the local edits.";
        }

        return keep.ZoneContradictions.Count > 0
            ? "A card added locally would come back in another zone because of its categories: " +
                $"{DescribeContradictionList(keep.ZoneContradictions)}. Change its zone or categories and pull " +
                "again, or preview the pull with discardLocalChanges to take the Archidekt state, which drops the " +
                "local edits."
            : null;
    }

    /// <summary>
    /// Says what a pull with <c>discardLocalChanges</c> throws away, counted from the local-* differences.
    /// </summary>
    internal static string DescribeDiscard(ArchidektSyncDiff? diff)
    {
        const string replace = "Replace the local deck with the Archidekt deck.";
        IReadOnlyList<ArchidektDifference> local =
            [.. diff?.Differences.Where(ArchidektDifferenceStates.IsLocalChange) ?? []];
        if (local.Count == 0)
        {
            return $"{replace} There are no local changes since the last sync to discard.";
        }

        (int cardsAdded, int cardsChanged, int cardsRemoved) = CountChanges(local, "/entries/");
        (int categoriesAdded, int categoriesChanged, int categoriesRemoved) = CountChanges(local, "/categories/");
        return $"{replace} This discards local changes made since the last sync: {cardsAdded} cards added, " +
            $"{cardsChanged} changed, {cardsRemoved} removed (categories: {categoriesAdded} added, " +
            $"{categoriesChanged} changed, {categoriesRemoved} removed). They are listed in differences with " +
            "local-* states.";
    }

    /// <summary>
    /// Says what an ordinary pull does: it replaces the local deck, except the cards added locally when it keeps them.
    /// </summary>
    internal static string DescribeReplace(bool keepsLocalAdditions)
    {
        return keepsLocalAdditions
            ? "Replace the local deck with the Archidekt deck, except the cards added locally (see local-keep)."
            : "Replace the local deck with the Archidekt deck.";
    }

    /// <summary>
    /// Writes a count of cards as text, such as "1 card" or "3 cards".
    /// </summary>
    internal static string CardCount(int count)
    {
        return count == 1 ? "1 card" : $"{count} cards";
    }

    /// <summary>
    /// Explains which entries a push cannot place, and what to change.
    /// </summary>
    internal static string DescribeZoneContradictions(IReadOnlyList<ArchidektZoneContradiction> contradictions)
    {
        return "Archidekt keeps no zone of its own: a card's zone comes from its categories (a category named " +
            "Commander, Sideboard or Maybeboard, or the commander category; otherwise main, or maybeboard when its " +
            "primary category is not included in the deck). These cards would come back in another zone: " +
            $"{DescribeContradictionList(contradictions)}. Change the zone, or add or remove the matching category.";
    }

    /// <summary>
    /// Says why a push that Archidekt's own changes block would be refused.
    /// </summary>
    internal static string DescribeConflictingPush(int remoteRows)
    {
        return "archidekt_push_apply would refuse this push: the Archidekt deck changed since the " +
            $"last sync ({remoteRows} {(remoteRows == 1 ? "row" : "rows")}; see differences with remote-* " +
            "states). Run archidekt_pull_preview first; a pull keeps cards added locally when those are your " +
            "only local changes. Then push again.";
    }

    /// <summary>
    /// Describes cards whose zone differs from the zone their categories give them, up to five of them.
    /// </summary>
    private static string DescribeContradictionList(IReadOnlyList<ArchidektZoneContradiction> contradictions)
    {
        return ListFirst(
            contradictions,
            value => $"'{value.CardName}' (zone '{value.Zone}', but its categories give zone '{value.ExpectedZone}')",
            "; ");
    }

    /// <summary>
    /// Lists up to five items, then says how many more there are.
    /// </summary>
    private static string ListFirst<T>(IReadOnlyList<T> items, Func<T, string> describe, string separator)
    {
        string shown = string.Join(separator, items.Take(MaximumListed).Select(describe));
        return items.Count > MaximumListed ? $"{shown} and {items.Count - MaximumListed} more" : shown;
    }

    /// <summary>
    /// Counts the added, changed, and removed local rows under one path prefix.
    /// </summary>
    private static (int Added, int Changed, int Removed) CountChanges(
        IReadOnlyList<ArchidektDifference> local,
        string pathPrefix)
    {
        List<ArchidektDifference> rows = [.. local.Where(
            value => value.Path.StartsWith(pathPrefix, StringComparison.Ordinal))];
        return (
            rows.Count(value => value.State == ArchidektDifferenceStates.LocalAdded),
            rows.Count(value => value.State is ArchidektDifferenceStates.LocalChanged
                or ArchidektDifferenceStates.ConcurrentChanged),
            rows.Count(value => value.State == ArchidektDifferenceStates.LocalRemoved));
    }
}
