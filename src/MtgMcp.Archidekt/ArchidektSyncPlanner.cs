using MtgMcp.Core.Decks;

namespace MtgMcp.Archidekt;

/// <summary>
/// Computes deterministic three-way evidence and stable primitive remote operation sequences.
/// </summary>
public static class ArchidektSyncPlanner
{
    /// <summary>
    /// Computes the three-way comparison for a stored local deck, using the local fingerprints of that deck: the full
    /// one, the one for fields no row shows, and the two orders an earlier version recorded.
    /// </summary>
    public static ArchidektSyncDiff Diff(
        DeckDocument local,
        RemoteDeckSnapshot localProjection,
        RemoteDeckSnapshot remote,
        ArchidektSyncBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(baseline);
        return Diff(
            local.DeckId,
            local.Revision,
            ArchidektLocalMapper.LocalFingerprint(local),
            localProjection,
            remote,
            baseline,
            ArchidektLocalMapper.LocalInvisibleFingerprint(local),
            baseline.SchemaVersion < 2 ? ArchidektLocalMapper.LegacyLocalFingerprint(local) : null,
            baseline.SchemaVersion < 2 ? ArchidektLocalMapper.LegacyPullOrderLocalFingerprint(local, baseline) : null);
    }

    /// <summary>
    /// Computes a conservative whole-content three-way comparison without selecting a conflict winner.
    /// </summary>
    public static ArchidektSyncDiff Diff(
        Guid localDeckId,
        long localRevision,
        string localFingerprint,
        RemoteDeckSnapshot localProjection,
        RemoteDeckSnapshot remote,
        ArchidektSyncBaseline baseline,
        string? localInvisibleFingerprint = null,
        string? legacyLocalFingerprint = null,
        string? legacyPullOrderFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(localProjection);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(baseline);
        // A baseline made by an earlier version (schema 1) recorded its local fingerprint over rows in the order of the
        // moment: the store's listing after a push, or the Archidekt response order after a pull. It is also compared
        // with both orders. A deck that matches neither is still judged by its rows, and reported as unverified when
        // none of them shows why it differs.
        bool legacyBaseline = baseline.SchemaVersion < 2;
        bool localChanged = !string.Equals(
            localFingerprint,
            baseline.LocalFingerprint,
            StringComparison.Ordinal);
        if (localChanged && legacyBaseline &&
            (string.Equals(legacyLocalFingerprint, baseline.LocalFingerprint, StringComparison.Ordinal) ||
                string.Equals(legacyPullOrderFingerprint, baseline.LocalFingerprint, StringComparison.Ordinal)))
        {
            localChanged = false;
        }

        bool remoteChanged = !string.Equals(
            ArchidektJsonContract.Recompute(remote).Remote,
            ArchidektJsonContract.Recompute(baseline.RemoteSnapshot).Remote,
            StringComparison.Ordinal);
        RemoteDeckSnapshot effectiveLocal = localChanged
            ? localProjection
            : baseline.RemoteSnapshot;
        RemoteDeckSnapshot effectiveRemote = remoteChanged
            ? remote
            : baseline.RemoteSnapshot;
        List<ArchidektDifference> differences = BuildDifferences(
            baseline.RemoteSnapshot,
            effectiveLocal,
            effectiveRemote,
            localChanged,
            remoteChanged);
        bool representedLocalChange = differences.Any(value =>
            ArchidektDifferenceStates.IsLocalChange(value) || value.State == ArchidektDifferenceStates.Converged);
        // Rows explain the change only when no field outside them changed: a field no row shows (category color,
        // entry order, category rank) must still keep the deck from reading as unchanged.
        bool hiddenChange = !legacyBaseline &&
            baseline.LocalInvisibleFingerprint is not null &&
            localInvisibleFingerprint is not null &&
            !string.Equals(baseline.LocalInvisibleFingerprint, localInvisibleFingerprint, StringComparison.Ordinal);
        // A baseline from an earlier version did not record category colors or ordering, so a difference no row shows
        // cannot be told from an edit to those fields: it is reported as unverified, never as unchanged.
        if (localChanged && (!representedLocalChange || hiddenChange))
        {
            differences.Add(new ArchidektDifference(
                "/localOnly/content",
                legacyBaseline ? ArchidektDifferenceStates.LocalChangedUnverified : "local-changed",
                baseline.LocalFingerprint,
                localFingerprint,
                RemoteValue: null));
        }

        bool representedRemoteChange = differences.Any(value =>
            ArchidektDifferenceStates.IsRemoteChange(value) || value.State == ArchidektDifferenceStates.Converged);
        if (remoteChanged && !representedRemoteChange)
        {
            differences.Add(new ArchidektDifference(
                "/provider/content",
                "remote-changed",
                baseline.RemoteFingerprint,
                LocalValue: null,
                remote.RemoteFingerprint));
        }

        differences.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));

        return new ArchidektSyncDiff(
            localDeckId,
            localRevision,
            remote.RemoteId,
            remote.RemoteFingerprint,
            baseline.RemoteFingerprint,
            localChanged && remoteChanged,
            differences);
    }

    /// <summary>
    /// Produces stable, path-addressed three-way rows without choosing a conflict winner.
    /// </summary>
    private static List<ArchidektDifference> BuildDifferences(
        RemoteDeckSnapshot baseline,
        RemoteDeckSnapshot local,
        RemoteDeckSnapshot remote,
        bool localChanged,
        bool remoteChanged)
    {
        List<ArchidektDifference> differences = [];
        AddValueDifference(differences, "/metadata/name", baseline.Name, local.Name, remote.Name, localChanged, remoteChanged);
        AddValueDifference(differences, "/metadata/description", baseline.Description, local.Description, remote.Description, localChanged, remoteChanged);
        AddValueDifference(differences, "/metadata/format", baseline.Format, local.Format, remote.Format, localChanged, remoteChanged);
        AddValueDifference(differences, "/metadata/visibility", baseline.Visibility, local.Visibility, remote.Visibility, localChanged, remoteChanged);
        AddValueDifference(differences, "/metadata/parentFolderId", baseline.ParentFolderId, local.ParentFolderId, remote.ParentFolderId, localChanged, remoteChanged);

        AddEntityDifferences(
            differences,
            "/categories",
            KeyCategories(baseline.Categories),
            KeyCategories(local.Categories),
            KeyCategories(remote.Categories),
            localChanged,
            remoteChanged);
        AddEntityDifferences(
            differences,
            "/entries",
            KeyEntries(baseline.Entries),
            KeyEntries(local.Entries),
            KeyEntries(remote.Entries),
            localChanged,
            remoteChanged);
        return differences;
    }

    /// <summary>
    /// Adds one scalar row when either side differs from the synchronization baseline.
    /// </summary>
    private static void AddValueDifference(
        List<ArchidektDifference> differences,
        string path,
        string? baseline,
        string? local,
        string? remote,
        bool localEligible,
        bool remoteEligible)
    {
        bool localChanged = localEligible && !string.Equals(baseline, local, StringComparison.Ordinal);
        bool remoteChanged = remoteEligible && !string.Equals(baseline, remote, StringComparison.Ordinal);
        if (!localChanged && !remoteChanged)
        {
            return;
        }

        differences.Add(new ArchidektDifference(
            path,
            DifferenceState(baseline, local, remote, localChanged, remoteChanged),
            baseline,
            local,
            remote));
    }

    /// <summary>
    /// Adds canonical category or entry rows keyed by stable provider identity where available.
    /// </summary>
    private static void AddEntityDifferences<T>(
        List<ArchidektDifference> differences,
        string pathPrefix,
        IReadOnlyDictionary<string, T> baseline,
        IReadOnlyDictionary<string, T> local,
        IReadOnlyDictionary<string, T> remote,
        bool localEligible,
        bool remoteEligible)
    {
        SortedSet<string> keys = new(baseline.Keys, StringComparer.Ordinal);
        keys.UnionWith(local.Keys);
        keys.UnionWith(remote.Keys);
        foreach (string key in keys)
        {
            string? baselineValue = SerializeEntity(baseline, key);
            string? localValue = SerializeEntity(local, key);
            string? remoteValue = SerializeEntity(remote, key);
            AddValueDifference(
                differences,
                $"{pathPrefix}/{EscapePathSegment(key)}",
                baselineValue,
                localValue,
                remoteValue,
                localEligible,
                remoteEligible);
        }
    }

    /// <summary>
    /// Serializes one normalized entity for model-readable before/after evidence.
    /// </summary>
    private static string? SerializeEntity<T>(IReadOnlyDictionary<string, T> values, string key)
    {
        if (!values.TryGetValue(key, out T? value))
        {
            return null;
        }

        // The entry order number and the category rank are renumbered by Archidekt without a user edit, and it may
        // return an entry's category names in another order.
        object? shown = value switch
        {
            RemoteDeckEntry entry => ArchidektEntryComparison.Canonical(entry),
            RemoteDeckCategory category => category with { SortOrder = 0 },
            _ => value,
        };
        return System.Text.Json.JsonSerializer.Serialize(shown, ArchidektContract.JsonOptions);
    }

    /// <summary>
    /// Classifies one row as a local, remote, or concurrent add, removal, or change, or as converged when both sides
    /// changed it to the same value.
    /// </summary>
    private static string DifferenceState(
        string? baseline,
        string? local,
        string? remote,
        bool localChanged,
        bool remoteChanged)
    {
        if (localChanged && remoteChanged)
        {
            return string.Equals(local, remote, StringComparison.Ordinal)
                ? ArchidektDifferenceStates.Converged
                : ArchidektDifferenceStates.ConcurrentChanged;
        }

        string side = localChanged ? "local" : "remote";
        string? value = localChanged ? local : remote;
        string kind = baseline is null ? "added" : value is null ? "removed" : "changed";
        return $"{side}-{kind}";
    }

    /// <summary>
    /// Keys categories by provider identity, falling back to normalized names for new local rows.
    /// </summary>
    private static IReadOnlyDictionary<string, RemoteDeckCategory> KeyCategories(
        IReadOnlyList<RemoteDeckCategory> categories)
    {
        return KeyWithOccurrences(
            categories,
            value => string.IsNullOrWhiteSpace(value.ProviderCategoryId)
                ? $"name:{value.Name.ToLowerInvariant()}"
                : $"id:{value.ProviderCategoryId}");
    }

    /// <summary>
    /// Keys entries by relation identity, falling back to exact printing/card identity for new local rows.
    /// </summary>
    private static IReadOnlyDictionary<string, RemoteDeckEntry> KeyEntries(
        IReadOnlyList<RemoteDeckEntry> entries)
    {
        return KeyWithOccurrences(
            entries,
            value => !string.IsNullOrWhiteSpace(value.ProviderRelationId)
                ? $"relation:{value.ProviderRelationId}"
                : $"printing:{value.PrintingId?.ToString("D") ?? value.ProviderCardId}:{value.SetCode}:{value.CollectorNumber}:{value.Finish}:{value.CardName}");
    }

    /// <summary>
    /// Preserves duplicate logical identities by appending a deterministic occurrence suffix.
    /// </summary>
    private static IReadOnlyDictionary<string, T> KeyWithOccurrences<T>(
        IReadOnlyList<T> values,
        Func<T, string> keySelector)
    {
        Dictionary<string, T> keyed = new(StringComparer.Ordinal);
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string baseKey = keySelector(value);
            occurrences.TryGetValue(baseKey, out int occurrence);
            occurrences[baseKey] = occurrence + 1;
            string key = occurrence == 0 ? baseKey : $"{baseKey}#{occurrence + 1}";
            keyed.Add(key, value);
        }

        return keyed;
    }

    /// <summary>
    /// Escapes one JSON-Pointer path segment.
    /// </summary>
    private static string EscapePathSegment(string value)
    {
        return value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Plans remote operations in metadata, categories, additions/updates, removals, and verification order. Category
    /// names are compared exactly, because Archidekt allows two categories that differ only by case.
    /// </summary>
    public static ArchidektRemotePlan PlanRemoteApply(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target)
    {
        return PlanRemoteApply(current, target, lenientNames: false);
    }

    /// <summary>
    /// Plans operations; with <paramref name="lenientNames"/> (used only to verify a finished push) a category or
    /// primary category name that differs only by case counts as the same, because the provider may recase names.
    /// </summary>
    private static ArchidektRemotePlan PlanRemoteApply(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        bool lenientNames)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        List<ArchidektPlannedOperation> planned = [];
        int sequence = 1;
        if (!MetadataEqual(current, target))
        {
            planned.Add(new ArchidektPlannedOperation(
                new ArchidektRemoteOperation(sequence++, "metadata-update", current.RemoteId, "Update explicit deck metadata."),
                TargetCategory: null,
                CurrentCategory: null,
                TargetEntry: null,
                CurrentEntry: null));
        }

        Dictionary<string, RemoteDeckCategory> currentCategories = CategoryNames.Index(current.Categories, value => value.Name);
        Dictionary<string, RemoteDeckCategory> targetCategories = CategoryNames.Index(target.Categories, value => value.Name);
        foreach (RemoteDeckCategory targetCategory in target.Categories)
        {
            if (!FindCategory(currentCategories, targetCategories, targetCategory.Name, lenientNames, out RemoteDeckCategory? currentCategory))
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "category-create", targetCategory.Name, "Create one category."),
                    targetCategory,
                    CurrentCategory: null,
                    TargetEntry: null,
                    CurrentEntry: null));
            }
            else if (!CategoryEqual(currentCategory, targetCategory))
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "category-update", targetCategory.Name, "Update one category."),
                    targetCategory,
                    currentCategory,
                    TargetEntry: null,
                    CurrentEntry: null));
            }
        }

        foreach (RemoteDeckCategory currentCategory in current.Categories)
        {
            if (!FindCategory(targetCategories, currentCategories, currentCategory.Name, lenientNames, out _))
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "category-delete", currentCategory.Name, "Delete one unused category."),
                    TargetCategory: null,
                    currentCategory,
                    TargetEntry: null,
                    CurrentEntry: null));
            }
        }

        Dictionary<string, RemoteDeckEntry> currentEntries = current.Entries
            .Where(value => !string.IsNullOrWhiteSpace(value.ProviderRelationId))
            .ToDictionary(value => value.ProviderRelationId, StringComparer.Ordinal);
        HashSet<string> retainedRelations = new(StringComparer.Ordinal);
        foreach (RemoteDeckEntry targetEntry in target.Entries)
        {
            RemoteDeckEntry? currentEntry = null;
            if (!string.IsNullOrWhiteSpace(targetEntry.ProviderRelationId))
            {
                currentEntries.TryGetValue(targetEntry.ProviderRelationId, out currentEntry);
            }

            if (currentEntry is null)
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "entry-add", targetEntry.CardName, "Add one exact card relation."),
                    TargetCategory: null,
                    CurrentCategory: null,
                    targetEntry,
                    CurrentEntry: null));
                continue;
            }

            retainedRelations.Add(currentEntry.ProviderRelationId);
            if (!EntryEqual(currentEntry, targetEntry, lenientNames, targetCategories.Keys))
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "entry-update", targetEntry.CardName, "Update one card relation."),
                    TargetCategory: null,
                    CurrentCategory: null,
                    targetEntry,
                    currentEntry));
            }
        }

        foreach (RemoteDeckEntry currentEntry in current.Entries)
        {
            if (!retainedRelations.Contains(currentEntry.ProviderRelationId))
            {
                planned.Add(new ArchidektPlannedOperation(
                    new ArchidektRemoteOperation(sequence++, "entry-remove", currentEntry.CardName, "Remove one exact card relation."),
                    TargetCategory: null,
                    CurrentCategory: null,
                    TargetEntry: null,
                    currentEntry));
            }
        }

        int unresolvedAdds = planned.Count(value =>
            value.Public.Kind == "entry-add" &&
            string.IsNullOrWhiteSpace(value.TargetEntry?.ProviderCardId));
        int predictedRequests = planned.Count + unresolvedAdds + 1;
        string planFingerprint = ArchidektContract.Fingerprint(new
        {
            current.RemoteFingerprint,
            target.ContentFingerprint,
            operations = planned.Select(value => value.Public),
            predictedRequests,
        });
        return new ArchidektRemotePlan(planned, predictedRequests, planFingerprint);
    }

    /// <summary>
    /// Verifies a completed apply after binding uniquely matched rows to provider-generated identities.
    /// </summary>
    public static ArchidektRemotePlan PlanRemoteVerification(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        Dictionary<int, int> targetOrderCounts = target.Entries
            .GroupBy(value => value.SortOrder)
            .ToDictionary(group => group.Key, group => group.Count());
        IReadOnlyList<RemoteDeckEntry?> matches = MatchEntries(current, target);
        List<RemoteDeckEntry> reconciledEntries = [];
        for (int index = 0; index < target.Entries.Count; index++)
        {
            RemoteDeckEntry targetEntry = target.Entries[index];
            RemoteDeckEntry? matched = matches[index];
            if (matched is null)
            {
                reconciledEntries.Add(targetEntry);
                continue;
            }

            int sortOrder = targetOrderCounts[targetEntry.SortOrder] > 1
                ? matched.SortOrder
                : targetEntry.SortOrder;
            reconciledEntries.Add(targetEntry with
            {
                ProviderRelationId = matched.ProviderRelationId,
                ProviderCardId = matched.ProviderCardId,
                CategoryNames = matched.CategoryNames,
                SortOrder = sortOrder,
            });
        }

        return PlanRemoteApply(current, target with { Entries = reconciledEntries }, lenientNames: true);
    }

    /// <summary>
    /// Finds entries the plan adds or updates whose zone is not the zone a read of Archidekt gives them. Archidekt
    /// keeps no zone of its own: the zone comes from the categories, by the same rule the read uses (<c>MapZone</c>),
    /// so such a row would come back in another zone and the push could not verify.
    /// </summary>
    public static IReadOnlyList<ArchidektZoneContradiction> FindZoneContradictions(
        ArchidektRemotePlan plan,
        RemoteDeckSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        Dictionary<string, RemoteDeckCategory> categories = CategoryNames.Index(target.Categories, value => value.Name);
        List<ArchidektZoneContradiction> found = [];
        foreach (ArchidektPlannedOperation operation in plan.PlannedOperations)
        {
            RemoteDeckEntry? entry = operation.TargetEntry;
            if (entry is null || operation.Public.Kind is not ("entry-add" or "entry-update"))
            {
                continue;
            }

            string expected = ArchidektJsonContract.MapZone(entry.CategoryNames, categories);
            if (!string.Equals(entry.Zone, expected, StringComparison.Ordinal))
            {
                found.Add(new ArchidektZoneContradiction(entry.CardName, entry.Zone, expected));
            }
        }

        return found;
    }

    /// <summary>
    /// Links each pushed target row to the verified provider row with the same relation ID,
    /// or else the only row with identical content.
    /// </summary>
    internal static ArchidektRowLinks LinkRows(RemoteDeckSnapshot verified, RemoteDeckSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<RemoteDeckEntry?> matches = LinkByCardIdentity(verified, target, MatchEntries(verified, target));
        List<string?> entryRelationIds = [];
        foreach (RemoteDeckEntry? matched in matches)
        {
            entryRelationIds.Add(matched?.ProviderRelationId);
        }

        Dictionary<string, string> verifiedCategories = new(StringComparer.Ordinal);
        foreach (RemoteDeckCategory category in verified.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category.ProviderCategoryId))
            {
                verifiedCategories[category.Name] = category.ProviderCategoryId;
            }
        }

        // An exact name wins. A name with no exact match may take a verified category that differs only by case, but
        // never one that another target category matched exactly, and only when exactly one such category is left.
        HashSet<string> targetNames = new(target.Categories.Select(value => value.Name), StringComparer.Ordinal);
        List<string?> categoryIds = [];
        foreach (RemoteDeckCategory category in target.Categories)
        {
            if (verifiedCategories.TryGetValue(category.Name, out string? exactId))
            {
                categoryIds.Add(exactId);
                continue;
            }

            string[] loose =
            [
                .. verifiedCategories.Keys.Where(name =>
                    !targetNames.Contains(name) && string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase)),
            ];
            categoryIds.Add(loose.Length == 1 ? verifiedCategories[loose[0]] : null);
        }

        return new ArchidektRowLinks(entryRelationIds, categoryIds);
    }

    /// <summary>
    /// Links rows the exact match left alone when a verification found them different: a target row with no
    /// provider ID is linked to an unmatched provider row when they are the only pair with the same card identity.
    /// The difference stays in the next push plan as an update, and the created row is not added again.
    /// </summary>
    private static IReadOnlyList<RemoteDeckEntry?> LinkByCardIdentity(
        RemoteDeckSnapshot verified,
        RemoteDeckSnapshot target,
        IReadOnlyList<RemoteDeckEntry?> exact)
    {
        HashSet<string> used = new(
            exact.Where(value => value is not null).Select(value => value!.ProviderRelationId),
            StringComparer.Ordinal);
        List<RemoteDeckEntry?> linked = [.. exact];
        for (int index = 0; index < linked.Count; index++)
        {
            RemoteDeckEntry targetEntry = target.Entries[index];
            if (linked[index] is not null || !string.IsNullOrWhiteSpace(targetEntry.ProviderRelationId))
            {
                continue;
            }

            RemoteDeckEntry[] rows = [.. verified.Entries.Where(value =>
                !used.Contains(value.ProviderRelationId) && CardIdentityEqual(value, targetEntry))];
            int rivals = target.Entries.Where((value, other) =>
                linked[other] is null &&
                string.IsNullOrWhiteSpace(value.ProviderRelationId) &&
                CardIdentityEqual(targetEntry, value)).Count();
            if (rows.Length == 1 && rivals == 1)
            {
                linked[index] = rows[0];
                used.Add(rows[0].ProviderRelationId);
            }
        }

        return linked;
    }

    /// <summary>
    /// Matches target rows to provider rows by relation ID, then by unique exact content in stable relation order.
    /// </summary>
    private static IReadOnlyList<RemoteDeckEntry?> MatchEntries(RemoteDeckSnapshot current, RemoteDeckSnapshot target)
    {
        HashSet<string> matchedRelations = new(StringComparer.Ordinal);
        List<RemoteDeckEntry?> matches = [];
        foreach (RemoteDeckEntry targetEntry in target.Entries)
        {
            RemoteDeckEntry? matched = current.Entries.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(targetEntry.ProviderRelationId) &&
                !matchedRelations.Contains(value.ProviderRelationId) &&
                string.Equals(
                    value.ProviderRelationId,
                    targetEntry.ProviderRelationId,
                    StringComparison.Ordinal));
            if (matched is null)
            {
                RemoteDeckEntry[] candidates = current.Entries
                    .Where(value =>
                        !matchedRelations.Contains(value.ProviderRelationId) &&
                        VerificationContentEqual(value, targetEntry))
                    .OrderBy(value => value.ProviderRelationId, StringComparer.Ordinal)
                    .ToArray();
                matched = candidates.FirstOrDefault();
            }

            if (matched is not null)
            {
                matchedRelations.Add(matched.ProviderRelationId);
            }

            matches.Add(matched);
        }

        return matches;
    }

    /// <summary>
    /// Matches one uniquely identifiable row while excluding provider-owned IDs and an ambiguous order rank.
    /// </summary>
    private static bool VerificationContentEqual(RemoteDeckEntry observed, RemoteDeckEntry expected)
    {
        return CardIdentityEqual(observed, expected) &&
            observed.Quantity == expected.Quantity &&
            ArchidektCardNames.Match(observed.CardName, expected.CardName) &&
            string.Equals(observed.Language, expected.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Finish, expected.Finish, StringComparison.Ordinal) &&
            string.Equals(observed.Zone, expected.Zone, StringComparison.Ordinal) &&
            ArchidektEntryComparison.SameCategories(observed, expected, ignoreCase: true);
    }

    /// <summary>
    /// Finds a category by name in one snapshot. Exact names always match. With <paramref name="lenient"/>, a name
    /// with no exact match may take a category that differs only by case, never one the other snapshot has under that
    /// exact name, and only when exactly one is left.
    /// </summary>
    private static bool FindCategory(
        IReadOnlyDictionary<string, RemoteDeckCategory> index,
        IReadOnlyDictionary<string, RemoteDeckCategory> other,
        string name,
        bool lenient,
        out RemoteDeckCategory found)
    {
        if (index.TryGetValue(name, out RemoteDeckCategory? exact))
        {
            found = exact;
            return true;
        }

        if (lenient)
        {
            RemoteDeckCategory[] loose =
            [
                .. index
                    .Where(pair => !other.ContainsKey(pair.Key) &&
                        string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value),
            ];
            if (loose.Length == 1)
            {
                found = loose[0];
                return true;
            }
        }

        found = default!;
        return false;
    }

    /// <summary>
    /// Resolves a category name written in another case to the category the deck has: an exact name stays as it is, and
    /// a name that matches no category exactly maps to the one category that differs only by case, when there is one.
    /// A name that matches two categories stays unresolved, so it is never taken for either.
    /// </summary>
    private static string? Resolve(string? name, IReadOnlyCollection<string> categoryNames)
    {
        if (name is null || categoryNames.Contains(name))
        {
            return name;
        }

        string[] loose = [.. categoryNames.Where(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase))];
        return loose.Length == 1 ? loose[0] : name;
    }

    /// <summary>
    /// Compares the strongest caller-supplied printing identity available for one verification row.
    /// </summary>
    private static bool CardIdentityEqual(RemoteDeckEntry observed, RemoteDeckEntry expected)
    {
        if (expected.PrintingId is not null)
        {
            return observed.PrintingId == expected.PrintingId;
        }

        if (!string.IsNullOrWhiteSpace(expected.SetCode) &&
            !string.IsNullOrWhiteSpace(expected.CollectorNumber))
        {
            return string.Equals(observed.SetCode, expected.SetCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    observed.CollectorNumber,
                    expected.CollectorNumber,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (expected.OracleId is not null)
        {
            return observed.OracleId == expected.OracleId;
        }

        return !string.IsNullOrWhiteSpace(expected.ProviderCardId)
            ? string.Equals(observed.ProviderCardId, expected.ProviderCardId, StringComparison.Ordinal)
            : ArchidektCardNames.Match(observed.CardName, expected.CardName);
    }

    /// <summary>
    /// Reports whether provider-editable metadata is already equivalent.
    /// </summary>
    private static bool MetadataEqual(RemoteDeckSnapshot left, RemoteDeckSnapshot right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
            string.Equals(left.Format, right.Format, StringComparison.Ordinal) &&
            string.Equals(left.Visibility, right.Visibility, StringComparison.Ordinal) &&
            string.Equals(left.ParentFolderId, right.ParentFolderId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports whether one provider category requires a primitive mutation.
    /// </summary>
    private static bool CategoryEqual(RemoteDeckCategory left, RemoteDeckCategory right)
    {
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
            left.IncludedInDeck == right.IncludedInDeck &&
            left.IncludedInPrice == right.IncludedInPrice &&
            left.IsPremier == right.IsPremier;
    }

    /// <summary>
    /// Reports whether a provider row is the printing the target names. A target that names no printing (a card
    /// added by name) accepts the printing Archidekt chose, so that choice never shows as a difference.
    /// </summary>
    private static bool PrintingEqual(RemoteDeckEntry current, RemoteDeckEntry target)
    {
        return (target.PrintingId is null || current.PrintingId == target.PrintingId) &&
            (string.IsNullOrWhiteSpace(target.SetCode) ||
                string.Equals(current.SetCode, target.SetCode, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(target.CollectorNumber) ||
                string.Equals(current.CollectorNumber, target.CollectorNumber, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reports whether one provider relation already has the requested content. A category named in another case counts as
    /// the same only when it resolves to exactly one category of the deck (see <see cref="Resolve"/>).
    /// </summary>
    private static bool EntryEqual(
        RemoteDeckEntry left,
        RemoteDeckEntry right,
        bool lenientNames,
        IReadOnlyCollection<string> categoryNames)
    {
        // The remote row is the truth about names it already uses; the target's names resolve against those and the
        // deck's categories.
        HashSet<string> known = new(categoryNames, StringComparer.Ordinal);
        known.UnionWith(left.CategoryNames);
        if (left.PrimaryCategoryName is not null)
        {
            known.Add(left.PrimaryCategoryName);
        }

        RemoteDeckEntry resolved = right with
        {
            CategoryNames = [.. right.CategoryNames.Select(value => Resolve(value, known)!)],
            PrimaryCategoryName = Resolve(right.PrimaryCategoryName, known),
        };
        return left.Quantity == right.Quantity &&
            string.Equals(left.ProviderCardId, right.ProviderCardId, StringComparison.Ordinal) &&
            ArchidektCardNames.Match(left.CardName, right.CardName) &&
            PrintingEqual(left, right) &&
            string.Equals(left.Language, right.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Finish, right.Finish, StringComparison.Ordinal) &&
            string.Equals(left.Zone, right.Zone, StringComparison.Ordinal) &&
            ArchidektEntryComparison.SameCategories(left, resolved, lenientNames);
    }
}

/// <summary>
/// Carries the provider IDs matched to pushed target rows, in target order; null means no exact match.
/// </summary>
internal sealed record ArchidektRowLinks(
    IReadOnlyList<string?> EntryRelationIds,
    IReadOnlyList<string?> CategoryIds);

/// <summary>
/// Carries one deterministic internal plan plus its model-facing descriptors and request bound.
/// </summary>
public sealed record ArchidektRemotePlan(
    IReadOnlyList<ArchidektPlannedOperation> PlannedOperations,
    int PredictedProviderRequests,
    string PlanFingerprint)
{
    /// <summary>
    /// Gets immutable internal operations in execution order.
    /// </summary>
    public IReadOnlyList<ArchidektPlannedOperation> PlannedOperations { get; init; } =
        Array.AsReadOnly(PlannedOperations.ToArray());

    /// <summary>
    /// Gets the safe public operation descriptors.
    /// </summary>
    public IReadOnlyList<ArchidektRemoteOperation> PublicOperations =>
        Array.AsReadOnly(PlannedOperations.Select(value => value.Public).ToArray());
}

/// <summary>
/// Retains canonical source and target values needed to execute one primitive provider operation.
/// </summary>
public sealed record ArchidektPlannedOperation(
    ArchidektRemoteOperation Public,
    RemoteDeckCategory? TargetCategory,
    RemoteDeckCategory? CurrentCategory,
    RemoteDeckEntry? TargetEntry,
    RemoteDeckEntry? CurrentEntry);

/// <summary>
/// Names one entry whose zone differs from the zone its categories give it.
/// </summary>
public sealed record ArchidektZoneContradiction(string CardName, string Zone, string ExpectedZone);
