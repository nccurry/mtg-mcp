using System.ComponentModel;

namespace MtgMcp.Statistics;

/// <summary>
/// Declares one independent reshuffled mulligan attempt.
/// </summary>
public sealed record MulliganAttemptInput(
    [property: Description("Number of cards drawn for this independent attempt.")]
    int DrawCount,
    [property: Description("Number of cards removed after this attempt is kept.")]
    int BottomCount,
    [property: Description("Whether this final scheduled attempt is kept regardless of keep constraints.")]
    bool Forced);

/// <summary>
/// Requests exact independent mulligan attempts with caller-owned keep and bottom behavior.
/// </summary>
public sealed record MulliganRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("One through eight ordered independent attempt definitions.")]
    IReadOnlyList<MulliganAttemptInput> Attempts,
    [property: Description("Caller-owned conjunction of group ranges used by non-forced attempts.")]
    IReadOnlyList<StatisticsGroupConditionInput> KeepConditions,
    [property: Description("Ordered exact group names bottomed first; canonical bucket order breaks ties.")]
    IReadOnlyList<string> BottomPriority,
    [property: Description("Optional caller-owned conjunction evaluated after bottoming a kept hand.")]
    IReadOnlyList<StatisticsGroupConditionInput>? FinalConditions = null);

/// <summary>
/// Carries exact reach and keep probabilities for one scheduled attempt.
/// </summary>
public sealed record MulliganAttemptResult(
    int Attempt,
    int DrawCount,
    int BottomCount,
    bool Forced,
    ExactProbability ReachProbability,
    ExactProbability ConditionalKeepProbability,
    ExactProbability KeepProbability);

/// <summary>
/// Carries one complete exact mulligan analysis.
/// </summary>
public sealed record MulliganResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    IReadOnlyList<StatisticsGroupConditionInput> KeepConditions,
    IReadOnlyList<string> BottomPriority,
    IReadOnlyList<StatisticsGroupConditionInput>? FinalConditions,
    IReadOnlyList<MulliganAttemptResult> Attempts,
    ExactProbability NoKeepProbability,
    ExactProbability? FinalEventProbability)
{
    /// <summary>
    /// Gets canonical keep conditions ordered by exact group name.
    /// </summary>
    public IReadOnlyList<StatisticsGroupConditionInput> KeepConditions { get; init; } =
        Array.AsReadOnly(KeepConditions.ToArray());

    /// <summary>
    /// Gets an immutable copy of caller bottom priorities.
    /// </summary>
    public IReadOnlyList<string> BottomPriority { get; init; } =
        Array.AsReadOnly(BottomPriority.ToArray());

    /// <summary>
    /// Gets canonical final conditions when the caller requested a final event.
    /// </summary>
    public IReadOnlyList<StatisticsGroupConditionInput>? FinalConditions { get; init; } =
        FinalConditions is null ? null : Array.AsReadOnly(FinalConditions.ToArray());

    /// <summary>
    /// Gets immutable attempt results in caller schedule order.
    /// </summary>
    public IReadOnlyList<MulliganAttemptResult> Attempts { get; init; } =
        Array.AsReadOnly(Attempts.ToArray());
}
