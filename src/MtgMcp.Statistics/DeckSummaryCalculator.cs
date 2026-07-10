using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Adds deterministic stored-field and caller-value local deck summaries.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Calculates one summary without provider lookup, format policy, or legality inference.
    /// </summary>
    public OperationResult<StatisticsCalculation<DeckSummaryResult>> CalculateDeckSummary(
        DeckSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryPrepareDeckEntries(
                request,
                out StatisticsDeckEntryInput[] entries,
                out int totalQuantity,
                out string? entryError))
        {
            return Invalid<DeckSummaryResult>(entryError!);
        }

        StatisticsDeckSelectionEvidence evidence = new(
            request.DeckId,
            request.Revision,
            entries.Select(value => new StatisticsEntryEvidence(value.EntryId, value.Quantity)).ToArray(),
            request.ExcludedEntries ?? []);
        CanonicalPopulation population = new(totalQuantity, [], [], evidence);
        if (totalQuantity > StatisticsPopulationValidator.MaximumPopulation)
        {
            StatisticsBoundedUnsupported bounded = new(
                "statistics-bound-exceeded",
                "The exact deck summary exceeds the configured population bound.",
                new StatisticsLimitDetail(
                    "population",
                    StatisticsPopulationValidator.MaximumPopulation,
                    null,
                    totalQuantity,
                    0,
                    0,
                    0,
                    ["Select 1,000 cards or fewer for one summary."]));
            return new OperationSuccess<StatisticsCalculation<DeckSummaryResult>>(bounded);
        }

        StatisticsWorkBudget budget = new(workLimit);
        if (!budget.TryConsume(Math.Max(1, entries.Length)))
        {
            return Bounded<DeckSummaryResult>(
                budget,
                entries.Length,
                population,
                "Select fewer entries or numeric series.");
        }

        if (!TryPreparePercentiles(request.Percentiles, out int[] percentiles, out string? percentileError))
        {
            return Invalid<DeckSummaryResult>(percentileError!);
        }

        if (!TrySummarizeNumericSeries(
                entries,
                request.NumericSeries,
                percentiles,
                budget,
                cancellationToken,
                out DeckNumericSeriesResult[] numericSeries,
                out string? numericError))
        {
            if (numericError is null)
            {
                return Bounded<DeckSummaryResult>(
                    budget,
                    StatisticsWorkBudget.SaturatingMultiply(entries.Length, 8),
                    population,
                    "Reduce selected entries, numeric series, or percentiles.");
            }

            return Invalid<DeckSummaryResult>(numericError);
        }

        if (!TryBuildZonePartition(
                entries,
                request.ZonePartition,
                totalQuantity,
                out DeckZonePartitionResult? partition,
                out string? partitionError))
        {
            return Invalid<DeckSummaryResult>(partitionError!);
        }

        DeckSummaryResult result = new(
            CreateDerivation(
                "local-deck-composition",
                [
                    "Entry quantities, zones, printing identities, and categories came from the selected local deck revision.",
                    "Numeric values and optional zone partitions were supplied explicitly by the caller.",
                    "Missing numeric values were excluded and never treated as zero or fetched from a provider.",
                ]),
            request.DeckId,
            request.Revision,
            evidence.SelectedEntries,
            evidence.ExcludedEntries,
            entries.Length,
            totalQuantity,
            CountZones(entries),
            CountCategories(entries),
            CountPrintings(entries),
            numericSeries,
            partition);
        return Exact(result);
    }

    /// <summary>
    /// Validates already resolved entries and their stored local fields.
    /// </summary>
    private static bool TryPrepareDeckEntries(
        DeckSummaryRequest request,
        out StatisticsDeckEntryInput[] entries,
        out int totalQuantity,
        out string? error)
    {
        entries = [];
        totalQuantity = 0;
        error = null;
        if (request.DeckId == Guid.Empty || request.Revision <= 0)
        {
            error = "request deckId and revision must identify one stored deck revision.";
            return false;
        }

        if (request.SelectedEntries is null || request.ExcludedEntries is null)
        {
            error = "request selectedEntries and excludedEntries are required.";
            return false;
        }

        HashSet<Guid> entryIds = [];
        Dictionary<Guid, string> categoryNames = [];
        List<StatisticsDeckEntryInput> validated = [];
        long quantity = 0;
        foreach (StatisticsDeckEntryInput? entry in request.SelectedEntries)
        {
            if (entry is null ||
                entry.EntryId == Guid.Empty ||
                !entryIds.Add(entry.EntryId) ||
                entry.Quantity <= 0 ||
                !IsExactText(entry.CardName) ||
                !IsExactText(entry.Language) ||
                !IsExactText(entry.Zone) ||
                entry.Categories is null ||
                !ValidateCategories(entry.Categories, categoryNames))
            {
                error = "request selectedEntries contain invalid stored fields or duplicate identifiers.";
                return false;
            }

            quantity += entry.Quantity;
            if (quantity > int.MaxValue)
            {
                error = "request selected entry quantity is too large.";
                return false;
            }

            validated.Add(entry with
            {
                Categories = entry.Categories
                    .OrderBy(value => value.CategoryId)
                    .ToArray(),
            });
        }

        HashSet<Guid> excludedIds = [];
        foreach (StatisticsEntryEvidence? excluded in request.ExcludedEntries)
        {
            if (excluded is null ||
                excluded.EntryId == Guid.Empty ||
                excluded.Quantity <= 0 ||
                entryIds.Contains(excluded.EntryId) ||
                !excludedIds.Add(excluded.EntryId))
            {
                error = "request excludedEntries contain invalid or overlapping identifiers.";
                return false;
            }
        }

        entries = [.. validated];
        totalQuantity = (int)quantity;
        return true;
    }

    /// <summary>
    /// Validates category IDs and stable names across all selected entries.
    /// </summary>
    private static bool ValidateCategories(
        IReadOnlyList<StatisticsDeckCategoryInput> categories,
        IDictionary<Guid, string> knownNames)
    {
        HashSet<Guid> entryCategories = [];
        foreach (StatisticsDeckCategoryInput? category in categories)
        {
            if (category is null ||
                category.CategoryId == Guid.Empty ||
                !IsExactText(category.Name) ||
                !entryCategories.Add(category.CategoryId))
            {
                return false;
            }

            if (knownNames.TryGetValue(category.CategoryId, out string? knownName) &&
                !string.Equals(knownName, category.Name, StringComparison.Ordinal))
            {
                return false;
            }

            knownNames[category.CategoryId] = category.Name;
        }

        return true;
    }

    /// <summary>
    /// Validates and canonically orders requested nearest-rank percentile integers.
    /// </summary>
    private static bool TryPreparePercentiles(
        IReadOnlyList<int>? inputs,
        out int[] percentiles,
        out string? error)
    {
        percentiles = [];
        error = null;
        if (inputs is null || inputs.Count > 20)
        {
            error = "request.percentiles must contain at most twenty unique integers from 1 through 100.";
            return false;
        }

        HashSet<int> unique = [];
        foreach (int percentile in inputs)
        {
            if (percentile is < 1 or > 100 || !unique.Add(percentile))
            {
                error = "request.percentiles must contain unique integers from 1 through 100.";
                return false;
            }
        }

        percentiles = [.. unique];
        Array.Sort(percentiles);
        return true;
    }

    /// <summary>
    /// Validates and calculates every caller-owned numeric series.
    /// </summary>
    private static bool TrySummarizeNumericSeries(
        StatisticsDeckEntryInput[] entries,
        IReadOnlyList<DeckNumericSeriesInput>? inputs,
        int[] percentiles,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken,
        out DeckNumericSeriesResult[] results,
        out string? error)
    {
        results = [];
        error = null;
        if (inputs is null || inputs.Count > 8)
        {
            error = "request.numericSeries must contain at most eight series.";
            return false;
        }

        HashSet<Guid> selectedIds = entries.Select(value => value.EntryId).ToHashSet();
        HashSet<string> names = new(StringComparer.Ordinal);
        List<DeckNumericSeriesResult> calculated = [];
        foreach (DeckNumericSeriesInput? input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input is null ||
                !IsExactText(input.Name) ||
                !names.Add(input.Name) ||
                input.Values is null)
            {
                error = "request.numericSeries must use unique exact names and value collections.";
                return false;
            }

            Dictionary<Guid, ExactFraction> values = [];
            foreach (DeckNumericValueInput? value in input.Values)
            {
                if (value is null ||
                    !selectedIds.Contains(value.EntryId) ||
                    !values.TryAdd(value.EntryId, ExactFraction.Zero) ||
                    !TryParseExactDecimal(value.Value, out ExactFraction parsed))
                {
                    error = "request.numericSeries values must use unique selected entry IDs and exact decimal strings.";
                    return false;
                }

                values[value.EntryId] = parsed;
            }

            long work = entries.Length + values.Count + percentiles.Length;
            if (!budget.TryConsume(Math.Max(1, work)))
            {
                error = null;
                return false;
            }

            calculated.Add(CalculateNumericSeries(input.Name, entries, values, percentiles));
        }

        calculated.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        results = [.. calculated];
        return true;
    }

    /// <summary>
    /// Calculates one quantity-weighted exact numeric distribution.
    /// </summary>
    private static DeckNumericSeriesResult CalculateNumericSeries(
        string name,
        IReadOnlyList<StatisticsDeckEntryInput> entries,
        IReadOnlyDictionary<Guid, ExactFraction> values,
        IReadOnlyList<int> percentiles)
    {
        Dictionary<ExactFraction, int> histogram = [];
        ExactFraction sum = ExactFraction.Zero;
        int includedEntries = 0;
        int includedQuantity = 0;
        int missingEntries = 0;
        int missingQuantity = 0;
        foreach (StatisticsDeckEntryInput entry in entries)
        {
            if (!values.TryGetValue(entry.EntryId, out ExactFraction value))
            {
                missingEntries++;
                missingQuantity += entry.Quantity;
                continue;
            }

            includedEntries++;
            includedQuantity += entry.Quantity;
            histogram[value] = histogram.GetValueOrDefault(value) + entry.Quantity;
            sum = sum.Add(value.Multiply(ExactFraction.FromInteger(entry.Quantity)));
        }

        ExactFraction[] orderedValues = [.. histogram.Keys];
        Array.Sort(orderedValues, static (left, right) => left.CompareTo(right));
        DeckNumericHistogramBin[] bins = orderedValues
            .Select(value => new DeckNumericHistogramBin(value.ToValue(), histogram[value]))
            .ToArray();
        DeckNumericPercentile[] percentileResults = includedQuantity == 0
            ? []
            : CalculatePercentiles(orderedValues, histogram, includedQuantity, percentiles);
        return new DeckNumericSeriesResult(
            name,
            "nearest-rank",
            includedEntries,
            includedQuantity,
            missingEntries,
            missingQuantity,
            includedQuantity == 0
                ? null
                : sum.Divide(ExactFraction.FromInteger(includedQuantity)).ToValue(),
            bins,
            percentileResults);
    }

    /// <summary>
    /// Calculates one-based nearest-rank percentiles from a quantity-weighted histogram.
    /// </summary>
    private static DeckNumericPercentile[] CalculatePercentiles(
        IReadOnlyList<ExactFraction> orderedValues,
        IReadOnlyDictionary<ExactFraction, int> histogram,
        int includedQuantity,
        IReadOnlyList<int> percentiles)
    {
        DeckNumericPercentile[] results = new DeckNumericPercentile[percentiles.Count];
        for (int index = 0; index < percentiles.Count; index++)
        {
            int percentile = percentiles[index];
            int rank = ((percentile * includedQuantity) + 99) / 100;
            int cumulative = 0;
            ExactFraction selected = orderedValues[^1];
            foreach (ExactFraction value in orderedValues)
            {
                cumulative += histogram[value];
                if (cumulative >= rank)
                {
                    selected = value;
                    break;
                }
            }

            results[index] = new DeckNumericPercentile(percentile, rank, selected.ToValue());
        }

        return results;
    }

    /// <summary>
    /// Parses an invariant non-exponent decimal string into one exact rational.
    /// </summary>
    private static bool TryParseExactDecimal(string? input, out ExactFraction value)
    {
        value = ExactFraction.Zero;
        if (!IsExactText(input) || input!.Length > 256)
        {
            return false;
        }

        bool negative = input[0] == '-';
        int start = negative || input[0] == '+' ? 1 : 0;
        if (start == input.Length)
        {
            return false;
        }

        int separator = input.IndexOf('.', start);
        if (separator >= 0 && input.IndexOf('.', separator + 1) >= 0)
        {
            return false;
        }

        string whole = separator < 0 ? input[start..] : input[start..separator];
        string fraction = separator < 0 ? string.Empty : input[(separator + 1)..];
        if (whole.Length == 0 ||
            (separator >= 0 && fraction.Length == 0) ||
            !whole.All(char.IsAsciiDigit) ||
            !fraction.All(char.IsAsciiDigit))
        {
            return false;
        }

        string digits = whole + fraction;
        BigInteger numerator = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (negative)
        {
            numerator = BigInteger.Negate(numerator);
        }

        value = new ExactFraction(numerator, BigInteger.Pow(10, fraction.Length));
        return true;
    }

    /// <summary>
    /// Counts selected quantity by exact stored zone name.
    /// </summary>
    private static DeckQuantityCount[] CountZones(IEnumerable<StatisticsDeckEntryInput> entries)
    {
        return CountKeys(entries, entry => entry.Zone);
    }

    /// <summary>
    /// Counts selected quantity by stored category assignment; totals may overlap.
    /// </summary>
    private static DeckCategoryQuantityCount[] CountCategories(
        IEnumerable<StatisticsDeckEntryInput> entries)
    {
        Dictionary<(Guid Id, string Name), int> counts = [];
        foreach (StatisticsDeckEntryInput entry in entries)
        {
            foreach (StatisticsDeckCategoryInput category in entry.Categories)
            {
                (Guid, string) key = (category.CategoryId, category.Name);
                counts[key] = counts.GetValueOrDefault(key) + entry.Quantity;
            }
        }

        return counts
            .OrderBy(value => value.Key.Id)
            .Select(value => new DeckCategoryQuantityCount(
                value.Key.Id,
                value.Key.Name,
                value.Value))
            .ToArray();
    }

    /// <summary>
    /// Counts selected quantity by exact printing identity or deterministic fallback key.
    /// </summary>
    private static DeckQuantityCount[] CountPrintings(IEnumerable<StatisticsDeckEntryInput> entries)
    {
        return CountKeys(entries, PrintingKey);
    }

    /// <summary>
    /// Counts entry quantity by one exact canonical key selector.
    /// </summary>
    private static DeckQuantityCount[] CountKeys(
        IEnumerable<StatisticsDeckEntryInput> entries,
        Func<StatisticsDeckEntryInput, string> keySelector)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (StatisticsDeckEntryInput entry in entries)
        {
            string key = keySelector(entry);
            counts[key] = counts.GetValueOrDefault(key) + entry.Quantity;
        }

        return counts
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new DeckQuantityCount(value.Key, value.Value))
            .ToArray();
    }

    /// <summary>
    /// Produces one stable printing identity key without provider lookup.
    /// </summary>
    private static string PrintingKey(StatisticsDeckEntryInput entry)
    {
        if (entry.PrintingId is Guid printingId)
        {
            return $"printing:{printingId:D}";
        }

        if (IsExactText(entry.SetCode) && IsExactText(entry.CollectorNumber))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"set:{entry.SetCode}|collector:{entry.CollectorNumber}|language:{entry.Language}");
        }

        if (entry.OracleId is Guid oracleId)
        {
            return $"oracle:{oracleId:D}";
        }

        return $"unresolved-name:{entry.CardName}";
    }

    /// <summary>
    /// Validates and calculates an optional disjoint exact zone partition.
    /// </summary>
    private static bool TryBuildZonePartition(
        IReadOnlyList<StatisticsDeckEntryInput> entries,
        DeckZonePartitionInput? input,
        int totalQuantity,
        out DeckZonePartitionResult? result,
        out string? error)
    {
        result = null;
        error = null;
        if (input is null)
        {
            return true;
        }

        if (!TryPrepareZoneNames(input.IncludedZones, out string[] included) ||
            !TryPrepareZoneNames(input.ExcludedZones, out string[] excluded) ||
            included.Intersect(excluded, StringComparer.Ordinal).Any())
        {
            error = "request.zonePartition must use disjoint unique exact zone names.";
            return false;
        }

        HashSet<string> includedSet = included.ToHashSet(StringComparer.Ordinal);
        HashSet<string> excludedSet = excluded.ToHashSet(StringComparer.Ordinal);
        int includedQuantity = 0;
        int excludedQuantity = 0;
        foreach (StatisticsDeckEntryInput entry in entries)
        {
            if (includedSet.Contains(entry.Zone))
            {
                includedQuantity += entry.Quantity;
            }
            else if (excludedSet.Contains(entry.Zone))
            {
                excludedQuantity += entry.Quantity;
            }
        }

        result = new DeckZonePartitionResult(
            included,
            excluded,
            includedQuantity,
            excludedQuantity,
            totalQuantity - includedQuantity - excludedQuantity,
            totalQuantity);
        return true;
    }

    /// <summary>
    /// Validates and ordinally sorts exact unique zone names.
    /// </summary>
    private static bool TryPrepareZoneNames(IReadOnlyList<string>? inputs, out string[] names)
    {
        names = [];
        if (inputs is null)
        {
            return false;
        }

        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string? input in inputs)
        {
            if (!IsExactText(input) || !unique.Add(input!))
            {
                return false;
            }
        }

        names = [.. unique];
        Array.Sort(names, StringComparer.Ordinal);
        return true;
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
