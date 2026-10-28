using System.Text.Json.Serialization;
using MtgMcp.App.CardLists;
using MtgMcp.App.Decks;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Lists a card that was not checked and why.
/// </summary>
internal sealed record CastabilitySkipped(Guid EntryId, string Name, string Reason, string? ManaCost);

/// <summary>
/// Describes one source group as it was used.
/// </summary>
internal sealed record CastabilityGroupView(string Id, IReadOnlyList<string> Colors, int Cards);

/// <summary>
/// Gives the exact chance of paying one cost by its turn, and the cards that share the cost.
/// </summary>
internal sealed record CastabilityRowView(
    string Cost,
    int Turn,
    int CardsSeen,
    ExactProbability? Probability,
    IReadOnlyList<RuleEntryRef> Cards);

/// <summary>
/// Returns exact castability rows sorted by probability, then by card name. It states no target and no pass.
/// </summary>
internal sealed record CastabilityToolResult(
    IReadOnlyList<CardListSourceInfo> Sources,
    StatisticsDerivation Derivation,
    bool OnThePlay,
    bool RequireLandDrops,
    IReadOnlyList<CastabilityGroupView> Groups,
    IReadOnlyList<CastabilityRowView> Rows,
    IReadOnlyList<CastabilitySkipped> Skipped,
    IReadOnlyList<string> Limits)
{
    /// <summary>
    /// Gets how many skipped cards the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("skippedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedOmitted { get; init; }

    /// <summary>
    /// Gets true when rows were left out to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to see every row when the result was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}
