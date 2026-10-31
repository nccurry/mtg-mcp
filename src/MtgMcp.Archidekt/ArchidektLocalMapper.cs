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
        Guid localDeckId = deckId ?? Guid.CreateVersion7();
        List<DeckCategoryDraft> categories = [];
        Dictionary<string, Guid> categoryIds = new(StringComparer.Ordinal);
        foreach (RemoteDeckCategory category in remote.Categories)
        {
            Guid categoryId = ArchidektContract.StableGuid("category", localDeckId, category.ProviderCategoryId);
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
            Guid entryId = ArchidektContract.StableGuid("entry", localDeckId, entry.ProviderRelationId);
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
                if (CategoryNames.TryFind(categoryIds, categoryName, out Guid categoryId))
                {
                    assignments.Add(new DeckCategoryAssignment(
                        entryId,
                        categoryId,
                        CategoryNames.SameName(entry.PrimaryCategoryName, categoryName, categoryIds)));
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
            localDeckId);
    }

    /// <summary>
    /// Finds what a pull can keep of the local deck. Every local change since the last sync must be a card entry or a
    /// category added locally, and the fields no row shows (order numbers, category colors and ranks) must be as the
    /// baseline recorded them; the pull replaces everything else from Archidekt, so any other change is not kept.
    /// </summary>
    /// <returns>
    /// Null when the local deck has a change a pull cannot keep. Otherwise the added entries and categories, with the
    /// duplicates and zone contradictions that would stop the pull.
    /// </returns>
    public static ArchidektPullKeep? PlanPullKeep(
        DeckDocument local,
        ArchidektSyncBaseline baseline,
        RemoteDeckSnapshot remote,
        ArchidektSyncDiff diff)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(diff);
        if (baseline.LocalInvisibleFingerprint is null ||
            !diff.Differences.Where(ArchidektDifferenceStates.IsLocalChange).All(IsKeepableChange) ||
            !string.Equals(
                LinkedInvisibleFingerprint(local, baseline),
                baseline.LocalInvisibleFingerprint,
                StringComparison.Ordinal))
        {
            return null;
        }

        Dictionary<string, RemoteDeckEntry> baselineEntries = baseline.RemoteSnapshot.Entries
            .ToDictionary(value => value.ProviderRelationId, StringComparer.Ordinal);
        Dictionary<string, RemoteDeckCategory> baselineCategories = baseline.RemoteSnapshot.Categories
            .ToDictionary(value => value.ProviderCategoryId, StringComparer.Ordinal);
        RemoteDeckSnapshot target = ToRemoteTarget(local, baseline, remote);
        List<DeckEntry> entries = [];
        List<RemoteDeckEntry> keptRows = [];
        for (int index = 0; index < local.Entries.Count; index++)
        {
            if (FindBaselineRow(local.Entries[index].EntryId, baseline.EntryRelations, baselineEntries) is null)
            {
                entries.Add(local.Entries[index]);
                keptRows.Add(target.Entries[index]);
            }
        }

        Dictionary<string, RemoteDeckCategory> remoteCategories =
            CategoryNames.Index(remote.Categories, value => value.Name);
        Dictionary<string, DeckCategory> newCategories = new(StringComparer.Ordinal);
        List<RemoteDeckCategory> shownCategories = [.. remote.Categories];
        for (int index = 0; index < local.Categories.Count; index++)
        {
            DeckCategory category = local.Categories[index];
            bool added = FindBaselineRow(category.CategoryId, baseline.CategoryRelations, baselineCategories) is null;
            bool used = keptRows.Exists(value => value.CategoryNames.Contains(category.Name, StringComparer.Ordinal));
            if ((added || used) &&
                !CategoryNames.TryFind(remoteCategories, category.Name, out _) &&
                newCategories.TryAdd(category.Name, category))
            {
                shownCategories.Add(target.Categories[index]);
            }
        }

        if (entries.Count == 0 && newCategories.Count == 0)
        {
            return null;
        }

        HashSet<string> baselineRelationIds = baseline.RemoteSnapshot.Entries
            .Select(value => value.ProviderRelationId)
            .ToHashSet(StringComparer.Ordinal);
        List<RemoteDeckEntry> remoteAdded = [.. remote.Entries.Where(
            value => !baselineRelationIds.Contains(value.ProviderRelationId))];
        List<string> duplicates = [.. entries
            .Where(kept => remoteAdded.Exists(added => SameCard(kept, added)))
            .Select(kept => kept.CardName)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        return new ArchidektPullKeep(
            entries,
            [.. newCategories.Values],
            duplicates,
            ArchidektSyncPlanner.FindZoneContradictions(keptRows, shownCategories));
    }

    /// <summary>
    /// Tells whether a local change is an entry or category added locally, which a pull keeps. The row for the fields
    /// no row shows is accepted here; the caller checks that those fields did not change.
    /// </summary>
    private static bool IsKeepableChange(ArchidektDifference difference)
    {
        if (difference.State == ArchidektDifferenceStates.LocalChanged)
        {
            return difference.Path == "/localOnly/content";
        }

        return difference.State == ArchidektDifferenceStates.LocalAdded &&
            (difference.Path.StartsWith("/entries/", StringComparison.Ordinal) ||
                difference.Path.StartsWith("/categories/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Computes the fingerprint of the fields no row shows over the local rows the baseline links to Archidekt rows, so
    /// an added entry or category does not count as an edit of those fields.
    /// </summary>
    private static string LinkedInvisibleFingerprint(DeckDocument local, ArchidektSyncBaseline baseline)
    {
        return InvisibleFingerprint(
            local.Entries.Where(value => baseline.EntryRelations.ContainsKey(value.EntryId))
                .Select(value => (value.EntryId, value.SortOrder)),
            local.Categories.Where(value => baseline.CategoryRelations.ContainsKey(value.CategoryId))
                .Select(value => (value.CategoryId, value.Color, value.SortOrder)));
    }

    /// <summary>
    /// Tells whether a locally added entry is the same card as an entry Archidekt added: the same printing, the same
    /// Oracle card, or the same name.
    /// </summary>
    private static bool SameCard(DeckEntry kept, RemoteDeckEntry added)
    {
        return (kept.PrintingId is not null && kept.PrintingId == added.PrintingId) ||
            (kept.OracleId is not null && kept.OracleId == added.OracleId) ||
            string.Equals(kept.CardName, added.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds one transactional replacement change list while retaining the selected local deck identity. A
    /// <paramref name="keep"/> brings the locally added entries and categories back after the replacement.
    /// </summary>
    public static IReadOnlyList<DeckChange> BuildPullChanges(
        DeckDocument local,
        RemoteDeckSnapshot remote,
        DeckProviderBinding binding,
        string canonicalBaseline,
        ArchidektPullKeep? keep = null)
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

        if (keep is not null)
        {
            AddKeptChanges(changes, local, target, keep);
        }

        changes.Add(new UpsertDeckProviderBindingChange(binding, canonicalBaseline));
        return changes;
    }

    /// <summary>
    /// Adds the changes that bring the locally added entries and categories back after the replacement. A category
    /// Archidekt has keeps the Archidekt row; a category it lacks is created again by its name.
    /// </summary>
    private static void AddKeptChanges(
        List<DeckChange> changes,
        DeckDocument local,
        DeckCreateRequest target,
        ArchidektPullKeep keep)
    {
        Dictionary<string, Guid> remoteCategoryIds = (target.Categories ?? [])
            .ToDictionary(value => value.Name, value => value.CategoryId ?? Guid.Empty, StringComparer.Ordinal);
        Dictionary<string, Guid> keptCategoryIds = new(StringComparer.Ordinal);
        foreach (DeckCategory category in keep.Categories)
        {
            keptCategoryIds[category.Name] = category.CategoryId;
            changes.Add(new AddDeckCategoryChange(new DeckCategoryDraft(
                category.Name,
                category.Color,
                category.SortOrder,
                category.CategoryId)));
        }

        foreach (DeckEntry entry in keep.Entries)
        {
            changes.Add(new AddDeckEntryChange(new DeckEntryDraft(
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
                entry.EntryId)));
        }

        Dictionary<Guid, string> localCategoryNames = local.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        foreach (DeckEntry entry in keep.Entries)
        {
            HashSet<Guid> assigned = [];
            foreach (DeckCategoryAssignment assignment in local.CategoryAssignments.Where(
                value => value.EntryId == entry.EntryId))
            {
                if (localCategoryNames.TryGetValue(assignment.CategoryId, out string? name) &&
                    (CategoryNames.TryFind(remoteCategoryIds, name, out Guid categoryId) ||
                        keptCategoryIds.TryGetValue(name, out categoryId)) &&
                    assigned.Add(categoryId))
                {
                    changes.Add(new AssignDeckCategoryChange(entry.EntryId, categoryId, assignment.IsPrimary));
                }
            }
        }
    }

    /// <summary>
    /// Creates the baseline JSON after a verified push, linking local rows the push created to their new provider IDs.
    /// </summary>
    /// <param name="local">The local deck whose content was pushed.</param>
    /// <param name="verified">The provider deck read back after the push.</param>
    /// <param name="target">The pushed target, built from <paramref name="local"/> in entry and category order.</param>
    /// <param name="verificationMismatch">True when the verification found a difference; the baseline then keeps every
    /// local difference visible as a local change.</param>
    /// <returns>
    /// The baseline JSON and the local entry IDs the push could not link to a provider row, or null when all linked.
    /// A category with no verified match stores no link, and the next push plans it by name.
    /// </returns>
    public static (string Json, IReadOnlyList<Guid>? UnlinkedLocalIds) CreatePushBaseline(
        DeckDocument local,
        RemoteDeckSnapshot verified,
        RemoteDeckSnapshot target,
        bool verificationMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Entries.Count != local.Entries.Count || target.Categories.Count != local.Categories.Count)
        {
            throw new ArgumentException("The pushed target must be built from the local deck.", nameof(target));
        }

        ArchidektRowLinks links = ArchidektSyncPlanner.LinkRows(verified, target);
        Dictionary<Guid, string> entryRelations = [];
        foreach (RemoteDeckEntry value in verified.Entries)
        {
            entryRelations[ArchidektContract.StableGuid("entry", local.DeckId, value.ProviderRelationId)] =
                value.ProviderRelationId;
            entryRelations.TryAdd(ArchidektContract.StableGuid("entry", value.ProviderRelationId), value.ProviderRelationId);
        }

        Dictionary<Guid, string> categoryRelations = [];
        foreach (RemoteDeckCategory value in verified.Categories)
        {
            categoryRelations[ArchidektContract.StableGuid("category", local.DeckId, value.ProviderCategoryId)] =
                value.ProviderCategoryId;
            categoryRelations.TryAdd(
                ArchidektContract.StableGuid("category", value.ProviderCategoryId),
                value.ProviderCategoryId);
        }

        List<Guid> unlinked = [];
        for (int index = 0; index < local.Entries.Count; index++)
        {
            Guid entryId = local.Entries[index].EntryId;
            if (links.EntryRelationIds[index] is { } relationId)
            {
                entryRelations[entryId] = relationId;
            }
            else
            {
                unlinked.Add(entryId);
            }
        }

        for (int index = 0; index < local.Categories.Count; index++)
        {
            if (links.CategoryIds[index] is { } providerCategoryId)
            {
                categoryRelations[local.Categories[index].CategoryId] = providerCategoryId;
            }
        }

        // After a verification mismatch the local deck is not what was synchronized: the baseline keeps the verified
        // remote state and a local fingerprint no deck can have, so every difference Archidekt did not take stays
        // visible as a local change until a later push or pull succeeds.
        ArchidektSyncBaseline baseline = new(
            2,
            verified.RemoteId,
            verified.RemoteFingerprint,
            verificationMismatch ? UnsynchronizedFingerprint : LocalFingerprint(local),
            verified,
            entryRelations,
            categoryRelations,
            verificationMismatch ? null : LocalInvisibleFingerprint(local));
        string json = JsonSerializer.Serialize(baseline, ArchidektContract.JsonOptions);
        return (json, unlinked.Count == 0 ? null : unlinked);
    }

    /// <summary>
    /// Creates an initial baseline from the exact local draft that will be inserted transactionally.
    /// </summary>
    public static string CreateBaseline(DeckCreateRequest local, RemoteDeckSnapshot remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        Guid deckId = local.DeckId ?? throw new ArgumentException("The creation draft needs a deck ID.", nameof(local));
        ArchidektSyncBaseline baseline = new(
            2,
            remote.RemoteId,
            remote.RemoteFingerprint,
            FingerprintLocalContent(local),
            remote,
            remote.Entries.ToDictionary(
                value => ArchidektContract.StableGuid("entry", deckId, value.ProviderRelationId),
                value => value.ProviderRelationId),
            remote.Categories.ToDictionary(
                value => ArchidektContract.StableGuid("category", deckId, value.ProviderCategoryId),
                value => value.ProviderCategoryId),
            InvisibleFingerprint(
                (local.Entries ?? []).Select(value => (value.EntryId ?? Guid.Empty, value.SortOrder)),
                (local.Categories ?? []).Select(value => (value.CategoryId ?? Guid.Empty, value.Color, value.SortOrder))));
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
            if (baseline is null || baseline.SchemaVersion is not (1 or 2) || baseline.RemoteSnapshot is null ||
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
        Dictionary<string, RemoteDeckCategory> baselineCategories = baseline.RemoteSnapshot.Categories
            .ToDictionary(value => value.ProviderCategoryId, StringComparer.Ordinal);
        List<RemoteDeckCategory> categories = [];
        foreach (DeckCategory localCategory in local.Categories)
        {
            RemoteDeckCategory? baselineRow = FindBaselineRow(
                localCategory.CategoryId,
                baseline.CategoryRelations,
                baselineCategories);
            string providerId = baselineRow?.ProviderCategoryId ?? string.Empty;
            bool isMaybeboard = localCategory.Name.Equals(
                "maybeboard",
                StringComparison.OrdinalIgnoreCase);
            categories.Add(new RemoteDeckCategory(
                providerId,
                localCategory.Name,
                baselineRow?.IncludedInDeck ?? !isMaybeboard,
                baselineRow?.IncludedInPrice ?? !isMaybeboard,
                baselineRow?.IsPremier ?? false,
                localCategory.SortOrder));
        }

        Dictionary<Guid, string> categoryNames = local.Categories.ToDictionary(
            value => value.CategoryId,
            value => value.Name);
        Dictionary<Guid, List<DeckCategoryAssignment>> assignments = local.CategoryAssignments
            .GroupBy(value => value.EntryId)
            .ToDictionary(group => group.Key, group => group.ToList());
        Dictionary<string, RemoteDeckEntry> baselineEntries = baseline.RemoteSnapshot.Entries
            .ToDictionary(value => value.ProviderRelationId, StringComparer.Ordinal);
        Dictionary<string, RemoteDeckCategory> targetCategoriesByName = CategoryNames.Index(categories, value => value.Name);
        List<RemoteDeckEntry> entries = [];
        foreach (DeckEntry localEntry in local.Entries)
        {
            RemoteDeckEntry? baselineRow = FindBaselineRow(
                localEntry.EntryId,
                baseline.EntryRelations,
                baselineEntries);
            (List<string> names, string? primary) = MapAssignments(
                localEntry.EntryId,
                assignments,
                categoryNames);
            // A deck pulled by 0.13.0 keeps zone main for an entry whose first category is not included in the deck,
            // and its baseline row says main too. That is a zone the old rule gave, not a zone the user chose, so it
            // follows the categories as a read of Archidekt now gives it; a zone the user changed is kept as chosen.
            string zone = baselineRow is not null &&
                string.Equals(localEntry.Zone, baselineRow.Zone, StringComparison.Ordinal) &&
                IsStaleZone(baselineRow, baseline.RemoteSnapshot)
                    ? ArchidektJsonContract.MapZone(names, targetCategoriesByName)
                    : localEntry.Zone;

            entries.Add(new RemoteDeckEntry(
                baselineRow?.ProviderRelationId ?? string.Empty,
                baselineRow?.ProviderCardId ?? string.Empty,
                localEntry.Quantity,
                localEntry.CardName,
                localEntry.OracleId,
                localEntry.PrintingId,
                localEntry.SetCode,
                localEntry.CollectorNumber,
                localEntry.Language,
                localEntry.Finish,
                zone,
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
    /// Tells whether a stored baseline row has a zone that the current zone rule would not give its categories, which
    /// is what a deck pulled before entries in categories outside the deck were mapped to the maybeboard looks like.
    /// </summary>
    private static bool IsStaleZone(RemoteDeckEntry row, RemoteDeckSnapshot snapshot)
    {
        return !string.Equals(
            row.Zone,
            ArchidektJsonContract.MapZone(row.CategoryNames, CategoryNames.Index(snapshot.Categories, value => value.Name)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds the baseline provider row a local row is linked to, or null when the baseline has no link for it.
    /// </summary>
    private static TRow? FindBaselineRow<TRow>(
        Guid localId,
        IReadOnlyDictionary<Guid, string> links,
        Dictionary<string, TRow> rowsByProviderId)
        where TRow : class
    {
        if (links.TryGetValue(localId, out string? providerId) &&
            rowsByProviderId.TryGetValue(providerId, out TRow? linked))
        {
            return linked;
        }

        return null;
    }

    /// <summary>
    /// Holds the local fingerprint a baseline gets after a verification mismatch; no deck has this value, so the
    /// deck reads as changed until a later synchronization succeeds.
    /// </summary>
    public const string UnsynchronizedFingerprint = "unsynchronized";

    /// <summary>
    /// Computes the fingerprint of the local fields a remote row does not show: entry order numbers, category colors,
    /// and category ranks. They are not compared as rows, so this keeps an edit to them from being overwritten.
    /// </summary>
    public static string LocalInvisibleFingerprint(DeckDocument local)
    {
        ArgumentNullException.ThrowIfNull(local);
        return InvisibleFingerprint(
            local.Entries.Select(value => (value.EntryId, value.SortOrder)),
            local.Categories.Select(value => (value.CategoryId, value.Color, value.SortOrder)));
    }

    /// <summary>
    /// Computes the fingerprint an earlier version used for a stored deck: the rows in the order the store listed
    /// them. A baseline made by that version is compared with this value.
    /// </summary>
    public static string LegacyLocalFingerprint(DeckDocument local)
    {
        ArgumentNullException.ThrowIfNull(local);
        return FingerprintLocalContent(local, sorted: false);
    }

    /// <summary>
    /// Computes the fingerprint version 0.10.0 recorded when it pulled a deck: it hashed the creation draft, whose
    /// rows follow the order of the Archidekt response (entries, categories, then each entry's assignments in the
    /// order of its category names), not the order the store lists them in. The rows are put back in the order the
    /// baseline's remote snapshot has, using the baseline's links; rows with no link follow in store order.
    /// </summary>
    public static string LegacyPullOrderLocalFingerprint(DeckDocument local, ArchidektSyncBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(baseline);
        Dictionary<string, int> entryPositions = [];
        Dictionary<string, int> categoryPositions = [];
        Dictionary<string, Guid> categoryIdsByName = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Guid> localCategoryByProvider = [];
        foreach ((Guid localId, string providerId) in baseline.CategoryRelations)
        {
            localCategoryByProvider[providerId] = localId;
        }

        for (int index = 0; index < baseline.RemoteSnapshot.Categories.Count; index++)
        {
            RemoteDeckCategory category = baseline.RemoteSnapshot.Categories[index];
            categoryPositions.TryAdd(category.ProviderCategoryId, index);
            if (localCategoryByProvider.TryGetValue(category.ProviderCategoryId, out Guid localCategoryId))
            {
                categoryIdsByName[category.Name] = localCategoryId;
            }
        }

        for (int index = 0; index < baseline.RemoteSnapshot.Entries.Count; index++)
        {
            entryPositions.TryAdd(baseline.RemoteSnapshot.Entries[index].ProviderRelationId, index);
        }

        Dictionary<string, RemoteDeckEntry> remoteByProvider = [];
        foreach (RemoteDeckEntry remoteEntry in baseline.RemoteSnapshot.Entries)
        {
            remoteByProvider.TryAdd(remoteEntry.ProviderRelationId, remoteEntry);
        }

        List<DeckEntry> entries = [.. local.Entries
            .OrderBy(value => PositionOf(value.EntryId, baseline.EntryRelations, entryPositions))
            .Select(value => baseline.EntryRelations.TryGetValue(value.EntryId, out string? providerId) &&
                remoteByProvider.TryGetValue(providerId, out RemoteDeckEntry? remoteEntry)
                    ? AsDrafted(value, remoteEntry)
                    : value)];
        List<DeckCategory> categories = [.. local.Categories.OrderBy(
            value => PositionOf(value.CategoryId, baseline.CategoryRelations, categoryPositions))];
        Dictionary<(Guid, Guid), DeckCategoryAssignment> stored = [];
        foreach (DeckCategoryAssignment assignment in local.CategoryAssignments)
        {
            stored[(assignment.EntryId, assignment.CategoryId)] = assignment;
        }

        List<DeckCategoryAssignment> assignments = [];
        HashSet<(Guid, Guid)> placed = [];
        Dictionary<string, Guid> localEntryByProvider = [];
        foreach ((Guid localId, string providerId) in baseline.EntryRelations)
        {
            localEntryByProvider.TryAdd(providerId, localId);
        }

        foreach (RemoteDeckEntry remoteEntry in baseline.RemoteSnapshot.Entries)
        {
            if (!localEntryByProvider.TryGetValue(remoteEntry.ProviderRelationId, out Guid entryId))
            {
                continue;
            }

            foreach (string name in remoteEntry.CategoryNames)
            {
                if (categoryIdsByName.TryGetValue(name, out Guid categoryId) &&
                    stored.TryGetValue((entryId, categoryId), out DeckCategoryAssignment? assignment) &&
                    placed.Add((entryId, categoryId)))
                {
                    assignments.Add(assignment);
                }
            }
        }

        assignments.AddRange(local.CategoryAssignments.Where(
            value => !placed.Contains((value.EntryId, value.CategoryId))));
        return FingerprintLocalContent(local, entries, categories, assignments);
    }

    /// <summary>
    /// Puts back the values the store trimmed or lowercased when it saved the draft, so the hash matches the one 0.10.0
    /// took of the draft before saving. A stored value that is not the normalized form of the drafted value was
    /// edited and is kept.
    /// </summary>
    private static DeckEntry AsDrafted(DeckEntry stored, RemoteDeckEntry drafted)
    {
        return stored with
        {
            CardName = DraftedRequired(stored.CardName, drafted.CardName, static value => value.Trim()),
            SetCode = Drafted(stored.SetCode, drafted.SetCode, NormalizedCode),
            CollectorNumber = Drafted(stored.CollectorNumber, drafted.CollectorNumber, NormalizedText),
            Language = DraftedRequired(stored.Language, drafted.Language, NormalizedCode),
            Finish = DraftedRequired(stored.Finish, drafted.Finish, NormalizedCode),
            Zone = DraftedRequired(stored.Zone, drafted.Zone, NormalizedCode),
        };
    }

    /// <summary>
    /// Returns the drafted value when the stored value is what the store makes of it, and the stored value otherwise.
    /// </summary>
    private static string? Drafted(string? stored, string? drafted, Func<string, string?> normalize)
    {
        return drafted is not null && string.Equals(stored, normalize(drafted), StringComparison.Ordinal)
            ? drafted
            : stored;
    }

    /// <summary>
    /// Returns the drafted value when the stored required value is what the store makes of it, and the stored value
    /// otherwise.
    /// </summary>
    private static string DraftedRequired(string stored, string drafted, Func<string, string?> normalize)
    {
        return string.Equals(stored, normalize(drafted), StringComparison.Ordinal) ? drafted : stored;
    }

    /// <summary>
    /// Applies the store's rule for optional text: trimmed, and absent when blank.
    /// </summary>
    private static string? NormalizedText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Applies the store's rule for codes and vocabulary words: trimmed and lowercase.
    /// </summary>
    private static string? NormalizedCode(string value)
    {
        return NormalizedText(value)?.ToLowerInvariant();
    }

    /// <summary>
    /// Finds where a linked local row sits in the baseline's remote rows, or the end when it has no link.
    /// </summary>
    private static int PositionOf(
        Guid id,
        IReadOnlyDictionary<Guid, string> links,
        Dictionary<string, int> positions)
    {
        return links.TryGetValue(id, out string? providerId) && positions.TryGetValue(providerId, out int position)
            ? position
            : int.MaxValue;
    }

    /// <summary>
    /// Hashes the fields a remote row does not show, in a fixed order.
    /// </summary>
    private static string InvisibleFingerprint(
        IEnumerable<(Guid Id, int SortOrder)> entries,
        IEnumerable<(Guid Id, string? Color, int SortOrder)> categories)
    {
        return ArchidektContract.Fingerprint(new
        {
            entries = entries.OrderBy(value => value.Id).Select(value => new { value.Id, value.SortOrder }),
            categories = categories
                .OrderBy(value => value.Id)
                .Select(value => new { value.Id, value.Color, value.SortOrder }),
        });
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
    private static string FingerprintLocalContent(DeckDocument local, bool sorted = true)
    {
        return FingerprintLocalContent(
            local,
            Ordered(local.Entries, sorted, value => value.EntryId),
            Ordered(local.Categories, sorted, value => value.CategoryId),
            sorted
                ? local.CategoryAssignments.OrderBy(value => value.EntryId).ThenBy(value => value.CategoryId)
                : local.CategoryAssignments);
    }

    /// <summary>
    /// Computes the canonical local content projection over rows in the order the caller supplies.
    /// </summary>
    private static string FingerprintLocalContent(
        DeckDocument local,
        IEnumerable<DeckEntry> entries,
        IEnumerable<DeckCategory> categories,
        IEnumerable<DeckCategoryAssignment> assignments)
    {
        return ArchidektContract.Fingerprint(new
        {
            local.Name,
            local.Description,
            local.Format,
            entries = entries.Select(value => new
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
            categories = categories.Select(value => new
            {
                value.CategoryId,
                value.Name,
                value.Color,
                value.SortOrder,
            }),
            assignments = assignments.Select(value => new
            {
                value.EntryId,
                value.CategoryId,
                value.IsPrimary,
            }),
        });
    }

    /// <summary>
    /// Sorts rows by ID, or keeps the order the store listed them in for the earlier fingerprint.
    /// </summary>
    private static IEnumerable<T> Ordered<T>(IEnumerable<T> rows, bool sorted, Func<T, Guid> id)
    {
        return sorted ? rows.OrderBy(id) : rows;
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
            entries = (local.Entries ?? []).OrderBy(value => value.EntryId ?? Guid.Empty).Select(value => new
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
            categories = (local.Categories ?? [])
                .OrderBy(value => value.CategoryId ?? Guid.Empty)
                .Select(value => new
                {
                    CategoryId = value.CategoryId ?? Guid.Empty,
                    value.Name,
                    value.Color,
                    value.SortOrder,
                }),
            assignments = (local.CategoryAssignments ?? [])
                .OrderBy(value => value.EntryId)
                .ThenBy(value => value.CategoryId)
                .Select(value => new
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

        foreach (DeckCategoryAssignment assignment in values)
        {
            if (!categoryNames.TryGetValue(assignment.CategoryId, out string? categoryName))
            {
                continue;
            }

            names.Add(categoryName);
            primary = assignment.IsPrimary ? categoryName : primary;
        }

        if (primary is null && names.Count == 1)
        {
            primary = names[0];
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
    IReadOnlyDictionary<Guid, string> CategoryRelations,
    string? LocalInvisibleFingerprint = null)
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

/// <summary>
/// Holds what a pull keeps of the local deck: the entries and categories added locally since the last sync, and what
/// would stop the pull from keeping them.
/// </summary>
/// <param name="Entries">The local entries with no Archidekt row in the baseline.</param>
/// <param name="Categories">The local categories to create again, because Archidekt has no category of that name.</param>
/// <param name="DuplicateCardNames">The kept cards that Archidekt also added since the last sync.</param>
/// <param name="ZoneContradictions">The kept cards whose zone is not the zone their categories give them.</param>
public sealed record ArchidektPullKeep(
    IReadOnlyList<DeckEntry> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<string> DuplicateCardNames,
    IReadOnlyList<ArchidektZoneContradiction> ZoneContradictions);
