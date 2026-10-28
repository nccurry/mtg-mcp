using System.Text.Json.Serialization;
using MtgMcp.App.CardLists;
using MtgMcp.OnCurve;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Lists a card that was left out of a group and why.
/// </summary>
internal sealed record SimulationSkipped(Guid EntryId, string Name, string Group, string Reason);

/// <summary>
/// Describes one group as the engine used it.
/// </summary>
/// <param name="Id">The group's ID.</param>
/// <param name="Entries">How many entries are in the group.</param>
/// <param name="Cards">How many cards (copies) are in the group.</param>
internal sealed record SimulationGroupView(string Id, int Entries, int Cards);

/// <summary>
/// Reports how often one condition held, with a 95 percent Wilson interval.
/// </summary>
/// <param name="Id">The condition's ID.</param>
/// <param name="Hits">The number of samples in which it held.</param>
/// <param name="Rate">Hits divided by samples, rounded to six places.</param>
/// <param name="Wilson95">The lower and upper bounds of the 95 percent Wilson interval, rounded to six places.</param>
internal sealed record SimulationConditionView(string Id, int Hits, double Rate, IReadOnlyList<double> Wilson95);

/// <summary>
/// Names a card that appears in a returned sample, so entry IDs there can be read without another call.
/// </summary>
internal sealed record SimulationCardName(Guid EntryId, string Name);

/// <summary>
/// Echoes the full rule set the engine applied, including defaults, so nothing is hidden. For the on-curve preset it
/// is the rule set the preset expanded to.
/// </summary>
internal sealed record SimulationAppliedRules(
    int Turns,
    SimulationOpeningHandInput OpeningHand,
    IReadOnlyList<SimulationGroupInput> Groups,
    IReadOnlyList<SimulationRuleInput> EachTurn,
    IReadOnlyList<SimulationConditionInput> Conditions);

/// <summary>
/// Names the target card of the on-curve preset and the mana cost it was cast with.
/// </summary>
internal sealed record SimulationPresetTarget(Guid EntryId, Guid PrintingId, string Name, string ManaCost);

/// <summary>
/// Shows one land of the on-curve preset: its colors from installed card data and the caller's rule.
/// </summary>
internal sealed record SimulationPresetLand(
    Guid EntryId,
    Guid PrintingId,
    string Name,
    IReadOnlyList<string> ProducedMana,
    string Rule);

/// <summary>
/// Shows a main-zone entry that the on-curve preset leaves out of its land model.
/// </summary>
internal sealed record SimulationPresetUnmodeled(Guid EntryId, Guid PrintingId, string Name);

/// <summary>
/// Shows the facts the on-curve preset read from installed card data, so the answer states what it assumed.
/// </summary>
internal sealed record SimulationPresetFacts(
    string Kind,
    SimulationPresetTarget Target,
    IReadOnlyList<SimulationPresetLand> CandidateLands,
    IReadOnlyList<SimulationPresetUnmodeled> KnownEmptyLands,
    IReadOnlyList<SimulationPresetUnmodeled> MultiFaceCards,
    string Source)
{
    /// <summary>
    /// Gets how many candidate lands the list leaves out to stay under the size limit, or null when it lists all.
    /// </summary>
    [JsonPropertyName("candidateLandsOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CandidateLandsOmitted { get; init; }
}

/// <summary>
/// Returns the seeded sampled result of one simulation request. It states no target and no pass: what counts as a
/// good rate is the caller's judgment.
/// </summary>
internal sealed record SimulateToolResult(
    IReadOnlyList<CardListSourceInfo> Sources,
    string Seed,
    string RandomVersion,
    string ModelVersion,
    int Samples,
    string Fingerprint,
    SimulationAppliedRules AppliedRules,
    IReadOnlyList<SimulationGroupView> Groups,
    IReadOnlyList<SimulationSkipped> Skipped,
    IReadOnlyList<SimulationConditionView> Results,
    SimulationMulliganCounts? Mulligans,
    int LibraryEmpty,
    IReadOnlyList<SimulationSampleRecord> SampleHands,
    IReadOnlyList<SimulationCardName> SampleCards,
    IReadOnlyList<string> Limits)
{
    /// <summary>
    /// Gets the facts the on-curve preset read, or null for a request with explicit rules.
    /// </summary>
    [JsonPropertyName("preset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimulationPresetFacts? Preset { get; init; }

    /// <summary>
    /// Gets how many skipped cards the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("skippedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedOmitted { get; init; }

    /// <summary>
    /// Gets how many requested sample hands were left out to stay under the size limit, or null when none were.
    /// </summary>
    [JsonPropertyName("sampleHandsOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SampleHandsOmitted { get; init; }

    /// <summary>
    /// Gets true when sample hands were left out to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to see every sample hand when the result was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}
