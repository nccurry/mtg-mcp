using System.ComponentModel;
using System.Text.Json.Serialization;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Defines raw or explicitly selected local-deck population inputs for MCP statistics tools.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RawStatisticsPopulationInput), "raw")]
[JsonDerivedType(typeof(DeckStatisticsPopulationInput), "deck")]
internal abstract record StatisticsPopulationInput;

/// <summary>
/// Supplies already-disjoint raw population buckets and exact group vocabulary.
/// </summary>
internal sealed record RawStatisticsPopulationInput(
    [property: Description("Positive already-disjoint population buckets.")]
    IReadOnlyList<StatisticsPopulationBucket> Buckets,
    [property: Description("Complete exact caller-owned group vocabulary.")]
    IReadOnlyList<string> DeclaredGroups)
    : StatisticsPopulationInput;

/// <summary>
/// Selects one revisioned local deck population and explicit named groups.
/// </summary>
internal sealed record DeckStatisticsPopulationInput(
    [property: Description("Stable local deck UUID.")] Guid DeckId,
    [property: Description("Exact current local deck revision required for this calculation.")]
    long ExpectedRevision,
    [property: Description("One or more typed selector terms combined by entry-ID set union.")]
    IReadOnlyList<DeckStatisticsSelectorInput> PopulationSelectors,
    [property: Description("Zero through eight caller-owned named group selections.")]
    IReadOnlyList<DeckStatisticsGroupInput> Groups)
    : StatisticsPopulationInput;

/// <summary>
/// Defines the closed local-deck selector terms accepted by Statistics.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DeckEntryIdsSelectorInput), "entry-ids")]
[JsonDerivedType(typeof(DeckZoneNamesSelectorInput), "zone-names")]
[JsonDerivedType(typeof(DeckCategoryIdsSelectorInput), "category-ids")]
internal abstract record DeckStatisticsSelectorInput;

/// <summary>
/// Selects exact stable local entry IDs.
/// </summary>
internal sealed record DeckEntryIdsSelectorInput(
    [property: Description("One or more exact local entry UUIDs.")]
    IReadOnlyList<Guid> EntryIds)
    : DeckStatisticsSelectorInput;

/// <summary>
/// Selects entries whose stored zone equals one exact case-sensitive name.
/// </summary>
internal sealed record DeckZoneNamesSelectorInput(
    [property: Description("One or more exact case-sensitive stored zone names.")]
    IReadOnlyList<string> ZoneNames)
    : DeckStatisticsSelectorInput;

/// <summary>
/// Selects entries assigned to exact stable local category IDs.
/// </summary>
internal sealed record DeckCategoryIdsSelectorInput(
    [property: Description("One or more exact local category UUIDs.")]
    IReadOnlyList<Guid> CategoryIds)
    : DeckStatisticsSelectorInput;

/// <summary>
/// Defines one exact caller-owned group as a union of typed deck selector terms.
/// </summary>
internal sealed record DeckStatisticsGroupInput(
    [property: Description("Unique exact caller-owned group name.")]
    string Name,
    [property: Description("One or more typed selector terms combined by entry-ID set union.")]
    IReadOnlyList<DeckStatisticsSelectorInput> Selectors);

/// <summary>
/// Supplies resolved-deck summary options before App freezes stored entry fields.
/// </summary>
internal sealed record DeckSummarySourceInput(
    [property: Description("Stable local deck UUID.")] Guid DeckId,
    [property: Description("Exact current local deck revision required for this calculation.")]
    long ExpectedRevision,
    [property: Description("One or more typed selector terms combined by entry-ID set union.")]
    IReadOnlyList<DeckStatisticsSelectorInput> PopulationSelectors,
    [property: Description("Zero through eight caller-owned exact numeric series keyed by entry ID.")]
    IReadOnlyList<DeckNumericSeriesInput> NumericSeries,
    [property: Description("Up to twenty unique integer percentiles from 1 through 100.")]
    IReadOnlyList<int> Percentiles,
    [property: Description("Optional disjoint exact stored zone-name partition.")]
    DeckZonePartitionInput? ZonePartition = null);
