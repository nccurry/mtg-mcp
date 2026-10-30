using System.Text.Json.Serialization;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Names one entry a deck write refused, skipped, or could not fully resolve, so the caller can act on it.
/// </summary>
internal sealed record DeckWriteIssue(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("entryId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? EntryId = null,
    [property: JsonPropertyName("cardName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CardName = null)
{
    /// <summary>
    /// Names an entry whose card identity could not be found or settled.
    /// </summary>
    internal const string Unresolved = "unresolved";

    /// <summary>
    /// Names an entry or step that the write left out on purpose.
    /// </summary>
    internal const string Skipped = "skipped";
}

/// <summary>
/// Reports what a local deck write changed in a form that stays small however large the deck is: the new revision,
/// counts, the IDs of changed entries and categories, and anything unresolved or skipped. The whole deck is added
/// only when the caller asks for it with <c>returnDeck</c>.
/// </summary>
internal sealed record DeckWriteResult(
    [property: JsonPropertyName("deckId")] Guid DeckId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("entryCount")] int EntryCount,
    [property: JsonPropertyName("cardCount")] int CardCount,
    [property: JsonPropertyName("categoryCount")] int CategoryCount,
    [property: JsonPropertyName("changeCounts")] DeckChangeCounts ChangeCounts)
{
    /// <summary>
    /// Gets the revision the deck had before this write, or null when the write created the deck.
    /// </summary>
    [JsonPropertyName("previousRevision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? PreviousRevision { get; init; }

    /// <summary>
    /// Gets the IDs of entries the write added, or null when none were added or the list was cut short.
    /// </summary>
    [JsonPropertyName("addedEntryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? AddedEntryIds { get; init; }

    /// <summary>
    /// Gets the IDs of entries whose fields or categories the write changed.
    /// </summary>
    [JsonPropertyName("updatedEntryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? UpdatedEntryIds { get; init; }

    /// <summary>
    /// Gets the IDs of entries the write removed.
    /// </summary>
    [JsonPropertyName("removedEntryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? RemovedEntryIds { get; init; }

    /// <summary>
    /// Gets the IDs of categories the write added.
    /// </summary>
    [JsonPropertyName("addedCategoryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? AddedCategoryIds { get; init; }

    /// <summary>
    /// Gets the IDs of categories the write changed.
    /// </summary>
    [JsonPropertyName("updatedCategoryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? UpdatedCategoryIds { get; init; }

    /// <summary>
    /// Gets the IDs of categories the write removed.
    /// </summary>
    [JsonPropertyName("removedCategoryIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? RemovedCategoryIds { get; init; }

    /// <summary>
    /// Gets the entries or steps the write refused, skipped, or could not resolve, or null when there are none.
    /// </summary>
    [JsonPropertyName("issues")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DeckWriteIssue>? Issues { get; init; }

    /// <summary>
    /// Gets the number of issues the write found, which can be more than the list holds.
    /// </summary>
    [JsonPropertyName("issueCount")]
    public int IssueCount { get; init; }

    /// <summary>
    /// Gets true when a list of IDs or issues holds fewer items than the matching count says.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to do to read what a cut list left out.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the whole deck after the write, set only when the caller passed <c>returnDeck</c>.
    /// </summary>
    [JsonPropertyName("deck")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeckDocument? Deck { get; init; }
}

/// <summary>
/// Counts what a deck write changed. The counts are exact even when the ID lists are cut short.
/// </summary>
internal sealed record DeckChangeCounts(
    [property: JsonPropertyName("entriesAdded")] int EntriesAdded,
    [property: JsonPropertyName("entriesUpdated")] int EntriesUpdated,
    [property: JsonPropertyName("entriesRemoved")] int EntriesRemoved,
    [property: JsonPropertyName("categoriesAdded")] int CategoriesAdded,
    [property: JsonPropertyName("categoriesUpdated")] int CategoriesUpdated,
    [property: JsonPropertyName("categoriesRemoved")] int CategoriesRemoved);

/// <summary>
/// Builds the small result of a deck write by comparing the deck before and after it.
/// </summary>
internal static class DeckWriteResults
{
    /// <summary>
    /// Gives the most IDs a result lists across all of its ID lists, so a large batch still fits the size limit.
    /// </summary>
    internal const int MaximumListedIds = 100;

    /// <summary>
    /// Gives the most issues a result lists.
    /// </summary>
    internal const int MaximumListedIssues = 50;

    /// <summary>
    /// Says what to do when an ID list or the issue list was cut short.
    /// </summary>
    private const string CutNarrowHint =
        "Counts are exact, but a list holds only the first items. Call local_deck_get to read every entry and " +
        "category, or pass returnDeck: true on the next write.";

    /// <summary>
    /// Builds the result for a write that changed an existing deck.
    /// </summary>
    internal static DeckWriteResult After(
        DeckDocument before,
        DeckDocument after,
        IReadOnlyList<DeckWriteIssue> issues,
        bool returnDeck)
    {
        ArgumentNullException.ThrowIfNull(before);
        return Build(before, after, issues, returnDeck);
    }

    /// <summary>
    /// Builds the result for a write that created a deck, where every entry and category is new.
    /// </summary>
    internal static DeckWriteResult Created(
        DeckDocument created,
        IReadOnlyList<DeckWriteIssue> issues,
        bool returnDeck)
    {
        return Build(null, created, issues, returnDeck);
    }

    /// <summary>
    /// Compares the two decks and writes the counts, the capped ID lists, and the capped issue list.
    /// </summary>
    private static DeckWriteResult Build(
        DeckDocument? before,
        DeckDocument after,
        IReadOnlyList<DeckWriteIssue> issues,
        bool returnDeck)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(issues);
        (List<Guid> addedEntries, List<Guid> updatedEntries, List<Guid> removedEntries) = CompareEntries(before, after);
        (List<Guid> addedCategories, List<Guid> updatedCategories, List<Guid> removedCategories) =
            CompareCategories(before, after);
        DeckChangeCounts counts = new(
            addedEntries.Count,
            updatedEntries.Count,
            removedEntries.Count,
            addedCategories.Count,
            updatedCategories.Count,
            removedCategories.Count);

        int budget = MaximumListedIds;
        IReadOnlyList<Guid>? addedEntryIds = Take(addedEntries, ref budget);
        IReadOnlyList<Guid>? updatedEntryIds = Take(updatedEntries, ref budget);
        IReadOnlyList<Guid>? removedEntryIds = Take(removedEntries, ref budget);
        IReadOnlyList<Guid>? addedCategoryIds = Take(addedCategories, ref budget);
        IReadOnlyList<Guid>? updatedCategoryIds = Take(updatedCategories, ref budget);
        IReadOnlyList<Guid>? removedCategoryIds = Take(removedCategories, ref budget);
        int listed = (addedEntryIds?.Count ?? 0) + (updatedEntryIds?.Count ?? 0) + (removedEntryIds?.Count ?? 0) +
            (addedCategoryIds?.Count ?? 0) + (updatedCategoryIds?.Count ?? 0) + (removedCategoryIds?.Count ?? 0);
        int changed = addedEntries.Count + updatedEntries.Count + removedEntries.Count + addedCategories.Count +
            updatedCategories.Count + removedCategories.Count;
        bool issuesCut = issues.Count > MaximumListedIssues;
        bool truncated = listed < changed || issuesCut;
        return new DeckWriteResult(
            after.DeckId,
            after.Name,
            after.Revision,
            after.Entries.Count,
            after.Entries.Sum(entry => entry.Quantity),
            after.Categories.Count,
            counts)
        {
            PreviousRevision = before?.Revision,
            AddedEntryIds = addedEntryIds,
            UpdatedEntryIds = updatedEntryIds,
            RemovedEntryIds = removedEntryIds,
            AddedCategoryIds = addedCategoryIds,
            UpdatedCategoryIds = updatedCategoryIds,
            RemovedCategoryIds = removedCategoryIds,
            Issues = issues.Count == 0 ? null : [.. issues.Take(MaximumListedIssues)],
            IssueCount = issues.Count,
            Truncated = truncated ? true : null,
            Narrow = truncated ? CutNarrowHint : null,
            Deck = returnDeck ? after : null,
        };
    }

    /// <summary>
    /// Takes as many IDs as the budget allows, spends that many from it, and returns null for an empty list.
    /// </summary>
    private static List<Guid>? Take(List<Guid> ids, ref int budget)
    {
        if (ids.Count == 0 || budget <= 0)
        {
            return null;
        }

        int count = Math.Min(ids.Count, budget);
        budget -= count;
        return ids.GetRange(0, count);
    }

    /// <summary>
    /// Finds the entries that are new, changed, or gone. An entry counts as changed when its fields or its category
    /// assignments differ.
    /// </summary>
    private static (List<Guid> Added, List<Guid> Updated, List<Guid> Removed) CompareEntries(
        DeckDocument? before,
        DeckDocument after)
    {
        Dictionary<Guid, DeckEntry> previous = before is null ? [] : before.Entries.ToDictionary(entry => entry.EntryId);
        Dictionary<Guid, HashSet<DeckCategoryAssignment>> previousAssignments = GroupAssignments(before);
        Dictionary<Guid, HashSet<DeckCategoryAssignment>> currentAssignments = GroupAssignments(after);
        List<Guid> added = [];
        List<Guid> updated = [];
        HashSet<Guid> seen = [];
        foreach (DeckEntry entry in after.Entries)
        {
            seen.Add(entry.EntryId);
            if (!previous.TryGetValue(entry.EntryId, out DeckEntry? old))
            {
                added.Add(entry.EntryId);
            }
            else if (old != entry || !SameAssignments(previousAssignments, currentAssignments, entry.EntryId))
            {
                updated.Add(entry.EntryId);
            }
        }

        List<Guid> removed = [.. previous.Keys.Where(id => !seen.Contains(id))];
        return (added, updated, removed);
    }

    /// <summary>
    /// Finds the categories that are new, changed, or gone.
    /// </summary>
    private static (List<Guid> Added, List<Guid> Updated, List<Guid> Removed) CompareCategories(
        DeckDocument? before,
        DeckDocument after)
    {
        Dictionary<Guid, DeckCategory> previous =
            before is null ? [] : before.Categories.ToDictionary(category => category.CategoryId);
        List<Guid> added = [];
        List<Guid> updated = [];
        HashSet<Guid> seen = [];
        foreach (DeckCategory category in after.Categories)
        {
            seen.Add(category.CategoryId);
            if (!previous.TryGetValue(category.CategoryId, out DeckCategory? old))
            {
                added.Add(category.CategoryId);
            }
            else if (old != category)
            {
                updated.Add(category.CategoryId);
            }
        }

        List<Guid> removed = [.. previous.Keys.Where(id => !seen.Contains(id))];
        return (added, updated, removed);
    }

    /// <summary>
    /// Groups a deck's category assignments by entry.
    /// </summary>
    private static Dictionary<Guid, HashSet<DeckCategoryAssignment>> GroupAssignments(DeckDocument? deck)
    {
        Dictionary<Guid, HashSet<DeckCategoryAssignment>> groups = [];
        if (deck is null)
        {
            return groups;
        }

        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (!groups.TryGetValue(assignment.EntryId, out HashSet<DeckCategoryAssignment>? set))
            {
                set = [];
                groups.Add(assignment.EntryId, set);
            }

            set.Add(assignment);
        }

        return groups;
    }

    /// <summary>
    /// Tells whether an entry has the same category assignments in both decks.
    /// </summary>
    private static bool SameAssignments(
        Dictionary<Guid, HashSet<DeckCategoryAssignment>> previous,
        Dictionary<Guid, HashSet<DeckCategoryAssignment>> current,
        Guid entryId)
    {
        previous.TryGetValue(entryId, out HashSet<DeckCategoryAssignment>? old);
        current.TryGetValue(entryId, out HashSet<DeckCategoryAssignment>? now);
        return (old ?? []).SetEquals(now ?? []);
    }
}

/// <summary>
/// Reports a committed local import as a small deck summary together with the preview diagnostics the caller
/// accepted. The whole deck is inside the summary only when the caller passed <c>returnDeck</c>.
/// </summary>
internal sealed record DeckImportCreateView(
    [property: JsonPropertyName("summary")] DeckWriteResult Summary,
    [property: JsonPropertyName("completeness")] string Completeness,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics,
    [property: JsonPropertyName("omittedDiagnosticCount")] int OmittedDiagnosticCount)
{
    /// <summary>
    /// Builds the view from a created import, replacing its whole deck with the deck summary.
    /// </summary>
    internal static DeckImportCreateView From(DeckImportCreateResult created, bool returnDeck)
    {
        ArgumentNullException.ThrowIfNull(created);
        return new DeckImportCreateView(
            DeckWriteResults.Created(created.Deck, [], returnDeck),
            created.Completeness,
            created.Diagnostics,
            created.OmittedDiagnosticCount);
    }
}
