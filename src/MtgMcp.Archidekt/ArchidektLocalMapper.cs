using System.Text.Json;
using MtgMcp.Core.Decks;

namespace MtgMcp.Archidekt;

/// <summary>
/// Translates canonical provider evidence to and from provider-neutral local deck contracts.
/// </summary>
public static class ArchidektLocalMapper
{
    /// <summary>
    /// Creates a lossless local-deck draft with deterministic identities derived from provider relation IDs.
    /// </summary>
    public static DeckCreateRequest ToCreateRequest(
        RemoteDeckSnapshot remote,
        DeckProviderBinding binding,
        Guid? deckId = null)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(binding);
        List<DeckCategoryDraft> categories = [];
        Dictionary<string, Guid> categoryIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (RemoteDeckCategory category in remote.Categories)
        {
            Guid categoryId = ArchidektContract.StableGuid("category", category.ProviderCategoryId);
            categoryIds[category.Name] = categoryId;
            categories.Add(new DeckCategoryDraft(
                category.Name,
                Color: null,
                category.SortOrder,
                categoryId));
        }

        List<DeckEntryDraft> entries = [];
        List<DeckCategoryAssignment> assignments = [];
        foreach (RemoteDeckEntry entry in remote.Entries)
        {
            Guid entryId = ArchidektContract.StableGuid("entry", entry.ProviderRelationId);
            entries.Add(new DeckEntryDraft(
                entry.Quantity,
                entry.CardName,
                entry.OracleId,
                entry.PrintingId,
                entry.SetCode,
                entry.CollectorNumber,
                entry.Language,
                entry.Finish,
                entry.Zone,
                entry.SortOrder,
                entryId));
            foreach (string categoryName in entry.CategoryNames)
            {
                if (categoryIds.TryGetValue(categoryName, out Guid categoryId))
                {
                    assignments.Add(new DeckCategoryAssignment(
                        entryId,
                        categoryId,
                        entry.PrimaryCategoryName?.Equals(
                            categoryName,
                            StringComparison.OrdinalIgnoreCase) == true));
                }
            }
        }

        return new DeckCreateRequest(
            remote.Name,
            remote.Description,
            remote.Format,
            entries,
            categories,
            assignments,
            [binding],
            deckId);
    }

    /// <summary>
    /// Builds one transactional replacement change list while retaining the selected local deck identity.
    /// </summary>
    public static IReadOnlyList<DeckChange> BuildPullChanges(
        DeckDocument local,
        RemoteDeckSnapshot remote,
        DeckProviderBinding binding,
        string canonicalBaseline)
    {
        ArgumentNullException.ThrowIfNull(local);
        DeckCreateRequest target = ToCreateRequest(remote, binding, local.DeckId);
        List<DeckChange> changes = [];
        foreach (DeckEntry entry in local.Entries)
        {
            changes.Add(new RemoveDeckEntryChange(entry.EntryId));
        }

        foreach (DeckCategory category in local.Categories)
        {
            changes.Add(new RemoveDeckCategoryChange(category.CategoryId));
        }

        changes.Add(new UpdateDeckMetadataChange(
            target.Name,
            target.Description,
            target.Format));
        foreach (DeckCategoryDraft category in target.Categories ?? [])
        {
            changes.Add(new AddDeckCategoryChange(category));
        }

        foreach (DeckEntryDraft entry in target.Entries ?? [])
        {
            changes.Add(new AddDeckEntryChange(entry));
        }

        foreach (DeckCategoryAssignment assignment in target.CategoryAssignments ?? [])
        {
            changes.Add(new AssignDeckCategoryChange(
                assignment.EntryId,
                assignment.CategoryId,
                assignment.IsPrimary));
        }

        changes.Add(new UpsertDeckProviderBindingChange(binding, canonicalBaseline));
        return changes;
    }

    /// <summary>
    /// Creates the canonical synchronization baseline stored with one provider-neutral binding.
    /// </summary>
    public static string CreateBaseline(DeckDocument local, RemoteDeckSnapshot remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArchidektSyncBaseline baseline = new(
            1,
            remote.RemoteId,
            remote.RemoteFingerprint,
            LocalFingerprint(local),
            remote,
            remote.Entries.ToDictionary(
                value => ArchidektContract.StableGuid("entry", value.ProviderRelationId),
                value => value.ProviderRelationId),
            remote.Categories.ToDictionary(
                value => ArchidektContract.StableGuid("category", value.ProviderCategoryId),
                value => value.ProviderCategoryId));
        return JsonSerializer.Serialize(baseline, ArchidektContract.JsonOptions);
    }

    /// <summary>
    /// Creates an initial baseline from the exact local draft that will be inserted transactionally.
    /// </summary>
    public static string CreateBaseline(DeckCreateRequest local, RemoteDeckSnapshot remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArchidektSyncBaseline baseline = new(
            1,
            remote.RemoteId,
            remote.RemoteFingerprint,
            FingerprintLocalContent(local),
            remote,
            remote.Entries.ToDictionary(
                value => ArchidektContract.StableGuid("entry", value.ProviderRelationId),
                value => value.ProviderRelationId),
            remote.Categories.ToDictionary(
                value => ArchidektContract.StableGuid("category", value.ProviderCategoryId),
                value => value.ProviderCategoryId));
        return JsonSerializer.Serialize(baseline, ArchidektContract.JsonOptions);
    }

    /// <summary>
    /// Parses and validates a stored synchronization baseline without guessing around corruption.
    /// </summary>
    public static ArchidektSyncBaseline ParseBaseline(string json)
    {
        try
        {
            ArchidektSyncBaseline? baseline = JsonSerializer.Deserialize<ArchidektSyncBaseline>(
                ArchidektContract.Required(json, nameof(json)),
                ArchidektContract.JsonOptions);
            if (baseline is null || baseline.SchemaVersion != 1 || baseline.RemoteSnapshot is null ||
                string.IsNullOrWhiteSpace(baseline.RemoteDeckId) ||
                string.IsNullOrWhiteSpace(baseline.RemoteFingerprint) ||
                string.IsNullOrWhiteSpace(baseline.LocalFingerprint))
            {
                throw new InvalidDataException("Archidekt synchronization baseline is invalid.");
            }

            return baseline;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Archidekt synchronization baseline is invalid.", exception);
        }
    }

    /// <summary>
    /// Maps one current local deck into a remote target using only provider IDs retained by the baseline.
    /// </summary>
    public static RemoteDeckSnapshot ToRemoteTarget(
        DeckDocument local,
        ArchidektSyncBaseline baseline,
        RemoteDeckSnapshot currentRemote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentRemote);
        Dictionary<Guid, RemoteDeckCategory> baselineCategories = baseline.RemoteSnapshot.Categories
            .ToDictionary(
                value => ArchidektContract.StableGuid("category", value.ProviderCategoryId));
        List<RemoteDeckCategory> categories = [];
        foreach (DeckCategory localCategory in local.Categories)
        {
            baselineCategories.TryGetValue(localCategory.CategoryId, out RemoteDeckCategory? prior);
            string providerId = prior?.ProviderCategoryId ?? string.Empty;
            categories.Add(new RemoteDeckCategory(
                providerId,
                localCategory.Name,
                prior?.IncludedInDeck ?? true,
                prior?.IncludedInPrice ?? true,
                prior?.IsPremier ?? false,
                localCategory.SortOrder));
        }

        Dictionary<Guid, string> categoryNames = local.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        Dictionary<Guid, List<DeckCategoryAssignment>> assignments = local.CategoryAssignments
            .GroupBy(value => value.EntryId)
            .ToDictionary(group => group.Key, group => group.ToList());
        Dictionary<Guid, RemoteDeckEntry> baselineEntries = baseline.RemoteSnapshot.Entries
            .ToDictionary(value => ArchidektContract.StableGuid("entry", value.ProviderRelationId));
        List<RemoteDeckEntry> entries = [];
        foreach (DeckEntry localEntry in local.Entries)
        {
            baselineEntries.TryGetValue(localEntry.EntryId, out RemoteDeckEntry? prior);
            (List<string> names, string? primary) = MapAssignments(
                localEntry.EntryId,
                assignments,
                categoryNames);

            entries.Add(new RemoteDeckEntry(
                prior?.ProviderRelationId ?? string.Empty,
                prior?.ProviderCardId ?? string.Empty,
                localEntry.Quantity,
                localEntry.CardName,
                localEntry.OracleId,
                localEntry.PrintingId,
                localEntry.SetCode,
                localEntry.CollectorNumber,
                localEntry.Language,
                localEntry.Finish,
                localEntry.Zone,
                names,
                primary,
                localEntry.SortOrder));
        }

        string contentFingerprint = FingerprintLocalContent(local);
        return currentRemote with
        {
            Name = local.Name,
            Description = local.Description,
            Format = local.Format,
            Categories = categories,
            Entries = entries,
            ContentFingerprint = contentFingerprint,
            RemoteFingerprint = contentFingerprint,
        };
    }

    /// <summary>
    /// Computes a deterministic local fingerprint over caller-editable content only.
    /// </summary>
    public static string LocalFingerprint(DeckDocument local)
    {
        ArgumentNullException.ThrowIfNull(local);
        return FingerprintLocalContent(local);
    }

    /// <summary>
    /// Computes the canonical local content projection used by three-way comparisons.
    /// </summary>
    private static string FingerprintLocalContent(DeckDocument local)
    {
        return ArchidektContract.Fingerprint(new
        {
            local.Name,
            local.Description,
            local.Format,
            entries = local.Entries.Select(value => new
            {
                value.EntryId,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
            }),
            categories = local.Categories.Select(value => new
            {
                value.CategoryId,
                value.Name,
                value.Color,
                value.SortOrder,
            }),
            assignments = local.CategoryAssignments.Select(value => new
            {
                value.EntryId,
                value.CategoryId,
                value.IsPrimary,
            }),
        });
    }

    /// <summary>
    /// Computes the canonical local content projection for an exact creation draft.
    /// </summary>
    private static string FingerprintLocalContent(DeckCreateRequest local)
    {
        return ArchidektContract.Fingerprint(new
        {
            local.Name,
            Description = local.Description ?? string.Empty,
            local.Format,
            entries = (local.Entries ?? []).Select(value => new
            {
                EntryId = value.EntryId ?? Guid.Empty,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
            }),
            categories = (local.Categories ?? []).Select(value => new
            {
                CategoryId = value.CategoryId ?? Guid.Empty,
                value.Name,
                value.Color,
                value.SortOrder,
            }),
            assignments = (local.CategoryAssignments ?? []).Select(value => new
            {
                value.EntryId,
                value.CategoryId,
                value.IsPrimary,
            }),
        });
    }

    /// <summary>
    /// Maps one local entry's ordered category assignments into provider names and a primary value.
    /// </summary>
    private static (List<string> Names, string? Primary) MapAssignments(
        Guid entryId,
        IReadOnlyDictionary<Guid, List<DeckCategoryAssignment>> assignments,
        IReadOnlyDictionary<Guid, string> categoryNames)
    {
        List<string> names = [];
        string? primary = null;
        if (!assignments.TryGetValue(entryId, out List<DeckCategoryAssignment>? values))
        {
            return (names, primary);
        }

        foreach (DeckCategoryAssignment assignment in values.OrderBy(value => value.CategoryId))
        {
            if (!categoryNames.TryGetValue(assignment.CategoryId, out string? categoryName))
            {
                continue;
            }

            names.Add(categoryName);
            primary = assignment.IsPrimary ? categoryName : primary;
        }

        return (names, primary);
    }
}

/// <summary>
/// Stores the last canonical local/remote pair known to match one Archidekt binding.
/// </summary>
public sealed record ArchidektSyncBaseline(
    int SchemaVersion,
    string RemoteDeckId,
    string RemoteFingerprint,
    string LocalFingerprint,
    RemoteDeckSnapshot RemoteSnapshot,
    IReadOnlyDictionary<Guid, string> EntryRelations,
    IReadOnlyDictionary<Guid, string> CategoryRelations)
{
    /// <summary>
    /// Gets an immutable local-entry to provider-relation map.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> EntryRelations { get; init; } =
        new Dictionary<Guid, string>(EntryRelations);

    /// <summary>
    /// Gets an immutable local-category to provider-category map.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> CategoryRelations { get; init; } =
        new Dictionary<Guid, string>(CategoryRelations);
}
