using System.ComponentModel;

namespace MtgMcp.Statistics;

/// <summary>
/// Identifies one stored category assigned to one selected deck entry.
/// </summary>
public sealed record StatisticsDeckCategoryInput(Guid CategoryId, string Name);

/// <summary>
/// Carries only stored local fields needed for a deterministic deck summary.
/// </summary>
public sealed record StatisticsDeckEntryInput(
    Guid EntryId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Zone,
    IReadOnlyList<StatisticsDeckCategoryInput> Categories)
{
    /// <summary>
    /// Gets an immutable copy of stored category assignments.
    /// </summary>
    public IReadOnlyList<StatisticsDeckCategoryInput> Categories { get; init; } = Categories is null
        ? null!
        : Array.AsReadOnly(Categories.ToArray());
}

/// <summary>
/// Associates one caller-supplied exact decimal string with one selected entry.
/// </summary>
public sealed record DeckNumericValueInput(
    [property: Description("Selected local deck entry UUID.")] Guid EntryId,
    [property: Description("Invariant exact decimal string without exponent notation.")] string Value);

/// <summary>
/// Defines one caller-owned numeric series over selected entries.
/// </summary>
public sealed record DeckNumericSeriesInput(
    [property: Description("Unique caller-owned numeric series name.")] string Name,
    [Description("Exact values keyed by selected entry ID; omitted entries remain missing.")]
    IReadOnlyList<DeckNumericValueInput> Values)
{
    /// <summary>
    /// Gets an immutable copy of caller values.
    /// </summary>
    public IReadOnlyList<DeckNumericValueInput> Values { get; init; } = Values is null
        ? null!
        : Array.AsReadOnly(Values.ToArray());
}

/// <summary>
/// Defines optional disjoint exact zone-name sets for included/excluded reporting.
/// </summary>
public sealed record DeckZonePartitionInput(
    [Description("Exact stored zone names counted as included.")]
    IReadOnlyList<string> IncludedZones,
    [Description("Exact stored zone names counted as excluded; must be disjoint from included zones.")]
    IReadOnlyList<string> ExcludedZones)
{
    /// <summary>
    /// Gets an immutable copy of included zone names.
    /// </summary>
    public IReadOnlyList<string> IncludedZones { get; init; } = IncludedZones is null
        ? null!
        : Array.AsReadOnly(IncludedZones.ToArray());

    /// <summary>
    /// Gets an immutable copy of excluded zone names.
    /// </summary>
    public IReadOnlyList<string> ExcludedZones { get; init; } = ExcludedZones is null
        ? null!
        : Array.AsReadOnly(ExcludedZones.ToArray());
}

/// <summary>
/// Requests one deterministic summary over an already resolved local deck selection.
/// </summary>
public sealed record DeckSummaryRequest(
    Guid DeckId,
    long Revision,
    IReadOnlyList<StatisticsDeckEntryInput> SelectedEntries,
    IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries,
    IReadOnlyList<DeckNumericSeriesInput> NumericSeries,
    IReadOnlyList<int> Percentiles,
    DeckZonePartitionInput? ZonePartition = null)
{
    /// <summary>
    /// Gets an immutable selected-entry snapshot.
    /// </summary>
    public IReadOnlyList<StatisticsDeckEntryInput> SelectedEntries { get; init; } =
        SelectedEntries is null ? null! : Array.AsReadOnly(SelectedEntries.ToArray());

    /// <summary>
    /// Gets an immutable excluded-entry snapshot.
    /// </summary>
    public IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries { get; init; } =
        ExcludedEntries is null ? null! : Array.AsReadOnly(ExcludedEntries.ToArray());

    /// <summary>
    /// Gets an immutable numeric-series snapshot.
    /// </summary>
    public IReadOnlyList<DeckNumericSeriesInput> NumericSeries { get; init; } =
        NumericSeries is null ? null! : Array.AsReadOnly(NumericSeries.ToArray());

    /// <summary>
    /// Gets an immutable percentile snapshot.
    /// </summary>
    public IReadOnlyList<int> Percentiles { get; init; } =
        Percentiles is null ? null! : Array.AsReadOnly(Percentiles.ToArray());
}

/// <summary>
/// Carries one canonical exact-key quantity count.
/// </summary>
public sealed record DeckQuantityCount(string Key, int Quantity);

/// <summary>
/// Carries one possibly overlapping category quantity count.
/// </summary>
public sealed record DeckCategoryQuantityCount(Guid CategoryId, string Name, int Quantity);

/// <summary>
/// Carries one exact numeric histogram bin weighted by stored quantity.
/// </summary>
public sealed record DeckNumericHistogramBin(ExactRationalValue Value, int Quantity);

/// <summary>
/// Carries one quantity-weighted nearest-rank percentile.
/// </summary>
public sealed record DeckNumericPercentile(
    int Percentile,
    int Rank,
    ExactRationalValue Value);

/// <summary>
/// Carries one caller-owned exact numeric distribution and missing-data counts.
/// </summary>
public sealed record DeckNumericSeriesResult(
    string Name,
    string PercentileMethod,
    int IncludedEntryCount,
    int IncludedQuantity,
    int MissingEntryCount,
    int MissingQuantity,
    ExactRationalValue? Average,
    IReadOnlyList<DeckNumericHistogramBin> Histogram,
    IReadOnlyList<DeckNumericPercentile> Percentiles)
{
    /// <summary>
    /// Gets immutable histogram bins in ascending exact value order.
    /// </summary>
    public IReadOnlyList<DeckNumericHistogramBin> Histogram { get; init; } =
        Array.AsReadOnly(Histogram.ToArray());

    /// <summary>
    /// Gets immutable requested percentiles in ascending order.
    /// </summary>
    public IReadOnlyList<DeckNumericPercentile> Percentiles { get; init; } =
        Array.AsReadOnly(Percentiles.ToArray());
}

/// <summary>
/// Proves one caller-supplied zone partition over selected quantity.
/// </summary>
public sealed record DeckZonePartitionResult(
    IReadOnlyList<string> IncludedZones,
    IReadOnlyList<string> ExcludedZones,
    int IncludedQuantity,
    int ExcludedQuantity,
    int UncoveredQuantity,
    int TotalQuantity)
{
    /// <summary>
    /// Gets immutable exact included zone names.
    /// </summary>
    public IReadOnlyList<string> IncludedZones { get; init; } =
        Array.AsReadOnly(IncludedZones.ToArray());

    /// <summary>
    /// Gets immutable exact excluded zone names.
    /// </summary>
    public IReadOnlyList<string> ExcludedZones { get; init; } =
        Array.AsReadOnly(ExcludedZones.ToArray());
}

/// <summary>
/// Carries one complete deterministic local-deck composition summary.
/// </summary>
public sealed record DeckSummaryResult(
    StatisticsDerivation Derivation,
    Guid DeckId,
    long Revision,
    IReadOnlyList<StatisticsEntryEvidence> SelectedEntries,
    IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries,
    int EntryCount,
    int TotalQuantity,
    IReadOnlyList<DeckQuantityCount> Zones,
    IReadOnlyList<DeckCategoryQuantityCount> Categories,
    IReadOnlyList<DeckQuantityCount> Printings,
    IReadOnlyList<DeckNumericSeriesResult> NumericSeries,
    DeckZonePartitionResult? ZonePartition)
{
    /// <summary>
    /// Gets immutable selected-entry evidence.
    /// </summary>
    public IReadOnlyList<StatisticsEntryEvidence> SelectedEntries { get; init; } =
        Array.AsReadOnly(SelectedEntries.ToArray());

    /// <summary>
    /// Gets immutable excluded-entry evidence.
    /// </summary>
    public IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries { get; init; } =
        Array.AsReadOnly(ExcludedEntries.ToArray());

    /// <summary>
    /// Gets immutable zone counts in exact key order.
    /// </summary>
    public IReadOnlyList<DeckQuantityCount> Zones { get; init; } = Array.AsReadOnly(Zones.ToArray());

    /// <summary>
    /// Gets immutable overlapping category counts.
    /// </summary>
    public IReadOnlyList<DeckCategoryQuantityCount> Categories { get; init; } =
        Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets immutable printing/fallback counts in exact key order.
    /// </summary>
    public IReadOnlyList<DeckQuantityCount> Printings { get; init; } =
        Array.AsReadOnly(Printings.ToArray());

    /// <summary>
    /// Gets immutable caller numeric-series summaries.
    /// </summary>
    public IReadOnlyList<DeckNumericSeriesResult> NumericSeries { get; init; } =
        Array.AsReadOnly(NumericSeries.ToArray());
}
