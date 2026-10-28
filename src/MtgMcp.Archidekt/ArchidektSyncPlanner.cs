namespace MtgMcp.Archidekt;

/// <summary>
/// Computes deterministic three-way evidence and stable primitive remote operation sequences.
/// </summary>
public static class ArchidektSyncPlanner
{
    /// <summary>
    /// Computes a conservative whole-content three-way comparison without selecting a conflict winner.
    /// </summary>
    public static ArchidektSyncDiff Diff(
        Guid localDeckId,
        long localRevision,
        string localFingerprint,
        RemoteDeckSnapshot localProjection,
        RemoteDeckSnapshot remote,
        ArchidektSyncBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(localProjection);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(baseline);
        bool localChanged = !string.Equals(
            localFingerprint,
            baseline.LocalFingerprint,
            StringComparison.Ordinal);
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
            value.State.StartsWith("local-", StringComparison.Ordinal) ||
            value.State is "concurrent-changed" or "converged");
        if (localChanged && !representedLocalChange)
        {
            differences.Add(new ArchidektDifference(
                "/localOnly/content",
                "local-changed",
                baseline.LocalFingerprint,
                localFingerprint,
                RemoteValue: null));
        }

        bool representedRemoteChange = differences.Any(value =>
            value.State.StartsWith("remote-", StringComparison.Ordinal) ||
            value.State is "concurrent-changed" or "converged");
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

        // The entry order number and the category rank are renumbered by Archidekt without a user edit.
        object? shown = value switch
        {
            RemoteDeckEntry entry => entry with { SortOrder = 0 },
            RemoteDeckCategory category => category with { SortOrder = 0 },
            _ => value,
        };
        return System.Text.Json.JsonSerializer.Serialize(shown, ArchidektContract.JsonOptions);
    }

    /// <summary>
    /// Classifies one row as a local, remote, or concurrent add, removal, or change.
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
            return string.Equals(local, remote, StringComparison.Ordinal) ? "converged" : "concurrent-changed";
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
    /// Plans remote operations in metadata, categories, additions/updates, removals, and verification order.
    /// </summary>
    public static ArchidektRemotePlan PlanRemoteApply(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target)
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
            if (!CategoryNames.TryFind(currentCategories, targetCategory.Name, out RemoteDeckCategory? currentCategory))
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
            if (!CategoryNames.TryFind(targetCategories, currentCategory.Name, out _))
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
            if (!EntryEqual(currentEntry, targetEntry))
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

        return PlanRemoteApply(current, target with { Entries = reconciledEntries });
    }

    /// <summary>
    /// Finds entries the plan adds or moves whose zone says "in the deck" while their primary category is not
    /// included in the deck. Archidekt derives an entry's zone from its primary category, so such a row would come
    /// back in another zone and the push could not verify.
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
            if (entry is null ||
                operation.Public.Kind is not ("entry-add" or "entry-update") ||
                entry.Zone is not ("main" or "commander") ||
                entry.PrimaryCategoryName is not { Length: > 0 } primary ||
                !CategoryNames.TryFind(categories, primary, out RemoteDeckCategory? category) ||
                category.IncludedInDeck != false)
            {
                continue;
            }

            if (operation.CurrentEntry is { } current &&
                string.Equals(current.Zone, entry.Zone, StringComparison.Ordinal) &&
                string.Equals(current.PrimaryCategoryName, entry.PrimaryCategoryName, StringComparison.Ordinal))
            {
                continue;
            }

            found.Add(new ArchidektZoneContradiction(entry.CardName, entry.Zone, category.Name));
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

        List<string?> categoryIds = [];
        foreach (RemoteDeckCategory category in target.Categories)
        {
            categoryIds.Add(CategoryNames.TryFind(verifiedCategories, category.Name, out string? verifiedId) ? verifiedId : null);
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
            string.Equals(observed.CardName, expected.CardName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Language, expected.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Finish, expected.Finish, StringComparison.Ordinal) &&
            string.Equals(observed.Zone, expected.Zone, StringComparison.Ordinal) &&
            CategoryMembershipEqual(observed.CategoryNames, expected.CategoryNames) &&
            string.Equals(
                observed.PrimaryCategoryName,
                expected.PrimaryCategoryName,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares category membership independently of provider-controlled array ordering.
    /// </summary>
    private static bool CategoryMembershipEqual(
        IReadOnlyList<string> observed,
        IReadOnlyList<string> expected)
    {
        return observed.Count == expected.Count &&
            observed.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(
                    expected.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(value => value, StringComparer.Ordinal),
                    StringComparer.OrdinalIgnoreCase);
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
            : string.Equals(observed.CardName, expected.CardName, StringComparison.OrdinalIgnoreCase);
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
    /// Reports whether one provider relation already has the requested exact content.
    /// </summary>
    private static bool EntryEqual(RemoteDeckEntry left, RemoteDeckEntry right)
    {
        return left.Quantity == right.Quantity &&
            string.Equals(left.ProviderCardId, right.ProviderCardId, StringComparison.Ordinal) &&
            string.Equals(left.CardName, right.CardName, StringComparison.Ordinal) &&
            left.PrintingId == right.PrintingId &&
            string.Equals(left.SetCode, right.SetCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.CollectorNumber, right.CollectorNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Language, right.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Finish, right.Finish, StringComparison.Ordinal) &&
            string.Equals(left.Zone, right.Zone, StringComparison.Ordinal) &&
            CategoryMembershipEqual(left.CategoryNames, right.CategoryNames) &&
            string.Equals(left.PrimaryCategoryName, right.PrimaryCategoryName, StringComparison.OrdinalIgnoreCase);
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
/// Names one entry whose zone contradicts its primary category's inclusion in the deck.
/// </summary>
public sealed record ArchidektZoneContradiction(string CardName, string Zone, string CategoryName);
