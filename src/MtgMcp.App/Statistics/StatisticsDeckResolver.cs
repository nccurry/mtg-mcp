using System.Globalization;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Resolves typed local-deck selectors into explicit format-neutral statistics inputs.
/// </summary>
internal sealed class StatisticsDeckResolver
{
    /// <summary>
    /// Stores the local read boundary used for revisioned deck selection.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Names the path of the population object in the request of a population tool.
    /// </summary>
    private const string PopulationPath = "request.population";

    /// <summary>
    /// Creates a resolver around one process-local deck store.
    /// </summary>
    internal StatisticsDeckResolver(SqliteDeckStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// Resolves one raw or local-deck input into canonicalizable disjoint buckets.
    /// </summary>
    internal async Task<OperationResult<StatisticsPopulation>> ResolvePopulationAsync(
        StatisticsPopulationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input is RawStatisticsPopulationInput raw)
        {
            return new OperationSuccess<StatisticsPopulation>(
                new StatisticsPopulation(raw.Buckets, raw.DeclaredGroups));
        }

        if (input is not DeckStatisticsPopulationInput deckInput)
        {
            return Invalid<StatisticsPopulation>(
                "The statistics population kind is unsupported.",
                PopulationPath + ".kind");
        }

        OperationResult<ResolvedDeckSelection> selection = await ResolveDeckSelectionAsync(
            deckInput.DeckId,
            deckInput.ExpectedRevision,
            deckInput.PopulationSelectors,
            PopulationPath,
            cancellationToken).ConfigureAwait(false);
        return selection switch
        {
            OperationSuccess<ResolvedDeckSelection> success =>
                ResolveGroups(success.Data, deckInput.Groups, PopulationPath),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Resolves one deck summary source into stored fields and caller-supplied values only.
    /// </summary>
    internal async Task<OperationResult<DeckSummaryRequest>> ResolveSummaryAsync(
        DeckSummarySourceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        OperationResult<ResolvedDeckSelection> selection = await ResolveDeckSelectionAsync(
            input.DeckId,
            input.ExpectedRevision,
            input.PopulationSelectors,
            "request",
            cancellationToken).ConfigureAwait(false);
        return selection switch
        {
            OperationSuccess<ResolvedDeckSelection> success => new OperationSuccess<DeckSummaryRequest>(
                BuildSummaryRequest(success.Data, input)),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Loads one exact deck revision and resolves the required population selector union.
    /// </summary>
    private async Task<OperationResult<ResolvedDeckSelection>> ResolveDeckSelectionAsync(
        Guid deckId,
        long expectedRevision,
        IReadOnlyList<DeckStatisticsSelectorInput>? selectors,
        string path,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty)
        {
            return Invalid<ResolvedDeckSelection>("The deck ID must identify one local deck.", path + ".deckId");
        }

        if (expectedRevision <= 0)
        {
            return Invalid<ResolvedDeckSelection>(
                "The expected revision must be a local deck revision.",
                path + ".expectedRevision");
        }

        OperationResult<DeckDocument> loaded = await store.GetAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return loaded switch
        {
            OperationSuccess<DeckDocument> success when success.Data.Revision != expectedRevision =>
                new OperationConflict(
                    SharedReasonCodes.StaleDeckRevision,
                    "The local deck revision changed; load it again before calculating statistics.",
                    new ConflictState(Revision: success.Data.Revision)),
            OperationSuccess<DeckDocument> success => ResolveSelection(success.Data, selectors, path),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Resolves selector terms by union and records the canonical selected/excluded partition.
    /// </summary>
    private static OperationResult<ResolvedDeckSelection> ResolveSelection(
        DeckDocument deck,
        IReadOnlyList<DeckStatisticsSelectorInput>? selectors,
        string path)
    {
        if (!TrySelectEntryIds(deck, selectors, out HashSet<Guid> selectedIds, out string? error))
        {
            return Invalid<ResolvedDeckSelection>(error!, path + ".populationSelectors");
        }

        if (selectedIds.Count == 0)
        {
            return Invalid<ResolvedDeckSelection>(
                "The population selectors must select at least one local deck entry.",
                path + ".populationSelectors");
        }

        List<DeckEntry> selected = [];
        List<DeckEntry> excluded = [];
        foreach (DeckEntry entry in deck.Entries)
        {
            (selectedIds.Contains(entry.EntryId) ? selected : excluded).Add(entry);
        }

        return new OperationSuccess<ResolvedDeckSelection>(
            new ResolvedDeckSelection(deck, selected, excluded));
    }

    /// <summary>
    /// Resolves named group selectors, proves subset membership, and creates disjoint entry buckets.
    /// </summary>
    private static OperationResult<StatisticsPopulation> ResolveGroups(
        ResolvedDeckSelection selection,
        IReadOnlyList<DeckStatisticsGroupInput>? inputs,
        string path)
    {
        if (inputs is null || inputs.Count > 8)
        {
            return Invalid<StatisticsPopulation>(
                "Deck statistics groups must contain at most eight named selections.",
                path + ".groups");
        }

        HashSet<Guid> populationIds = selection.Selected
            .Select(value => value.EntryId)
            .ToHashSet();
        Dictionary<string, HashSet<Guid>> groups = new(StringComparer.Ordinal);
        for (int index = 0; index < inputs.Count; index++)
        {
            DeckStatisticsGroupInput? input = inputs[index];
            string groupPath = $"{path}.groups[{index.ToString(CultureInfo.InvariantCulture)}]";
            if (input is null)
            {
                return Invalid<StatisticsPopulation>("Deck statistics groups cannot contain null entries.", groupPath);
            }

            if (!IsExactText(input.Name) || groups.ContainsKey(input.Name))
            {
                return Invalid<StatisticsPopulation>(
                    "Deck statistics groups must use unique exact names.",
                    groupPath + ".name");
            }

            if (!TrySelectEntryIds(selection.Deck, input.Selectors, out HashSet<Guid> ids, out _))
            {
                return Invalid<StatisticsPopulation>(
                    "Deck statistics groups must use valid selector terms.",
                    groupPath + ".selectors");
            }

            if (!ids.IsSubsetOf(populationIds))
            {
                return Invalid<StatisticsPopulation>(
                    "Every deck statistics group must select only entries in the chosen population.",
                    groupPath + ".selectors");
            }

            groups.Add(input.Name, ids);
        }

        string[] groupNames = [.. groups.Keys];
        Array.Sort(groupNames, StringComparer.Ordinal);
        List<StatisticsPopulationBucket> buckets = [];
        foreach (DeckEntry entry in selection.Selected)
        {
            List<string> memberships = [];
            foreach (string groupName in groupNames)
            {
                if (groups[groupName].Contains(entry.EntryId))
                {
                    memberships.Add(groupName);
                }
            }

            buckets.Add(new StatisticsPopulationBucket(entry.Quantity, memberships));
        }

        StatisticsDeckSelectionEvidence evidence = new(
            selection.Deck.DeckId,
            selection.Deck.Revision,
            selection.Selected.Select(ToEntryEvidence).ToArray(),
            selection.Excluded.Select(ToEntryEvidence).ToArray());
        return new OperationSuccess<StatisticsPopulation>(
            new StatisticsPopulation(buckets, groupNames, evidence));
    }

    /// <summary>
    /// Converts one resolved deck selection into the stored-field summary input.
    /// </summary>
    private static DeckSummaryRequest BuildSummaryRequest(
        ResolvedDeckSelection selection,
        DeckSummarySourceInput input)
    {
        Dictionary<Guid, DeckCategory> categoryById = selection.Deck.Categories
            .ToDictionary(value => value.CategoryId);
        Dictionary<Guid, List<StatisticsDeckCategoryInput>> categoriesByEntry = [];
        foreach (DeckCategoryAssignment assignment in selection.Deck.CategoryAssignments)
        {
            if (!categoryById.TryGetValue(assignment.CategoryId, out DeckCategory? category))
            {
                continue;
            }

            if (!categoriesByEntry.TryGetValue(
                assignment.EntryId,
                out List<StatisticsDeckCategoryInput>? categories))
            {
                categories = [];
                categoriesByEntry.Add(assignment.EntryId, categories);
            }

            categories.Add(new StatisticsDeckCategoryInput(category.CategoryId, category.Name));
        }

        StatisticsDeckEntryInput[] entries = selection.Selected
            .Select(entry => new StatisticsDeckEntryInput(
                entry.EntryId,
                entry.Quantity,
                entry.CardName,
                entry.OracleId,
                entry.PrintingId,
                entry.SetCode,
                entry.CollectorNumber,
                entry.Language,
                entry.Zone,
                categoriesByEntry.GetValueOrDefault(entry.EntryId) ?? []))
            .ToArray();
        return new DeckSummaryRequest(
            selection.Deck.DeckId,
            selection.Deck.Revision,
            entries,
            selection.Excluded.Select(ToEntryEvidence).ToArray(),
            input.NumericSeries,
            input.Percentiles,
            input.ZonePartition);
    }

    /// <summary>
    /// Resolves one selector collection into exact existing entry IDs.
    /// </summary>
    private static bool TrySelectEntryIds(
        DeckDocument deck,
        IReadOnlyList<DeckStatisticsSelectorInput>? selectors,
        out HashSet<Guid> selected,
        out string? error)
    {
        selected = [];
        error = null;
        if (selectors is null || selectors.Count is < 1 or > 32)
        {
            error = "Deck selectors must contain between one and thirty-two typed terms.";
            return false;
        }

        HashSet<Guid> entryIds = deck.Entries.Select(value => value.EntryId).ToHashSet();
        HashSet<Guid> categoryIds = deck.Categories.Select(value => value.CategoryId).ToHashSet();
        Dictionary<Guid, HashSet<Guid>> entriesByCategory = [];
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (!entriesByCategory.TryGetValue(assignment.CategoryId, out HashSet<Guid>? ids))
            {
                ids = [];
                entriesByCategory.Add(assignment.CategoryId, ids);
            }

            ids.Add(assignment.EntryId);
        }

        foreach (DeckStatisticsSelectorInput? selector in selectors)
        {
            if (!ApplySelector(
                    deck,
                    selector,
                    entryIds,
                    categoryIds,
                    entriesByCategory,
                    selected))
            {
                error = "A deck selector contains invalid, duplicate, or unknown exact values.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies one closed selector variant to an entry-ID union.
    /// </summary>
    private static bool ApplySelector(
        DeckDocument deck,
        DeckStatisticsSelectorInput? selector,
        IReadOnlySet<Guid> entryIds,
        IReadOnlySet<Guid> categoryIds,
        IReadOnlyDictionary<Guid, HashSet<Guid>> entriesByCategory,
        ISet<Guid> selected)
    {
        switch (selector)
        {
            case DeckEntryIdsSelectorInput entries
                when TryValidateDistinct(entries.EntryIds, entryIds):
                selected.UnionWith(entries.EntryIds);
                return true;
            case DeckZoneNamesSelectorInput zones when TryValidateZones(zones.ZoneNames):
                HashSet<string> zoneNames = zones.ZoneNames.ToHashSet(StringComparer.Ordinal);
                selected.UnionWith(deck.Entries
                    .Where(entry => zoneNames.Contains(entry.Zone))
                    .Select(entry => entry.EntryId));
                return true;
            case DeckCategoryIdsSelectorInput categories
                when TryValidateDistinct(categories.CategoryIds, categoryIds):
                foreach (Guid categoryId in categories.CategoryIds)
                {
                    if (entriesByCategory.TryGetValue(categoryId, out HashSet<Guid>? ids))
                    {
                        selected.UnionWith(ids);
                    }
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Validates one nonempty distinct ID collection against known stored IDs.
    /// </summary>
    private static bool TryValidateDistinct(
        IReadOnlyList<Guid>? values,
        IReadOnlySet<Guid> known)
    {
        return values is not null &&
            values.Count > 0 &&
            values.All(value => value != Guid.Empty && known.Contains(value)) &&
            values.Distinct().Count() == values.Count;
    }

    /// <summary>
    /// Validates one nonempty distinct exact zone-name collection.
    /// </summary>
    private static bool TryValidateZones(IReadOnlyList<string>? values)
    {
        return values is not null &&
            values.Count > 0 &&
            values.All(IsExactText) &&
            values.Distinct(StringComparer.Ordinal).Count() == values.Count;
    }

    /// <summary>
    /// Converts one stored entry to stable selection evidence.
    /// </summary>
    private static StatisticsEntryEvidence ToEntryEvidence(DeckEntry entry)
    {
        return new StatisticsEntryEvidence(entry.EntryId, entry.Quantity);
    }

    /// <summary>
    /// Creates one sanitized selector-resolution failure.
    /// </summary>
    private static OperationInvalidInput Invalid<T>(string message, string path)
    {
        return new OperationInvalidInput("invalid-statistics-selection", message, path);
    }

    /// <summary>
    /// Reports whether text is nonblank and already exactly trimmed.
    /// </summary>
    private static bool IsExactText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Carries one canonical selected/excluded local deck partition in stored entry order.
/// </summary>
internal sealed record ResolvedDeckSelection(
    DeckDocument Deck,
    IReadOnlyList<DeckEntry> Selected,
    IReadOnlyList<DeckEntry> Excluded);
