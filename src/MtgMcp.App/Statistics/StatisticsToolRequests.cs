using System.ComponentModel;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Requests one exact univariate probability over a raw or explicitly selected deck population.
/// </summary>
internal sealed record StatisticsHypergeometricToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("Exact declared group whose members count as successes.")]
    string SuccessGroup,
    [property: Description("Number of cards drawn uniformly without replacement.")]
    int DrawCount,
    [property: Description("Closed exact success-count event to evaluate.")]
    HypergeometricEventInput Event);

/// <summary>
/// Requests one exact conjunction over caller-declared overlapping groups.
/// </summary>
internal sealed record StatisticsMultivariateToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("Number of cards drawn uniformly without replacement.")]
    int DrawCount,
    [property: Description("One through eight group count ranges evaluated as a conjunction.")]
    IReadOnlyList<StatisticsGroupConditionInput> Conditions);

/// <summary>
/// Requests exact probability rows over a completely explicit draw-by-turn schedule.
/// </summary>
internal sealed record StatisticsTurnTableToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("Exact declared group whose members count as successes.")]
    string SuccessGroup,
    [property: Description("Complete number of cards seen before the first supplied turn row.")]
    int OpeningHandSize,
    [property: Description("Ordered turn rows containing every additional card seen on that turn.")]
    IReadOnlyList<TurnDrawInput> DrawsByTurn,
    [property: Description("Closed exact success-count event evaluated at every cumulative row.")]
    HypergeometricEventInput Event);

/// <summary>
/// Requests exact availability of explicitly declared one-use mana sources.
/// </summary>
internal sealed record StatisticsManaAvailabilityToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("Number of cards seen uniformly without replacement.")]
    int DrawCount,
    [property: Description("Distinct declared source groups and their W/U/B/R/G/C capabilities.")]
    IReadOnlyList<ManaSourceInput> Sources,
    [property: Description("Exact colored, colorless, and generic one-unit payment requirement.")]
    ManaRequirementInput Requirement,
    [property: Description("Maximum total drawn sources that may be assigned; no land rule is inferred.")]
    int MaximumUsableSources);

/// <summary>
/// Requests exact one-use allocation of drawn cards to caller-owned package requirements.
/// </summary>
internal sealed record StatisticsPackageAssemblyToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("Number of cards drawn uniformly without replacement.")]
    int DrawCount,
    [property: Description("One through eight named requirement families and their eligible groups.")]
    IReadOnlyList<PackageRequirementInput> Requirements);

/// <summary>
/// Requests exact independent mulligan attempts with caller-owned keep and bottom behavior.
/// </summary>
internal sealed record StatisticsMulliganToolRequest(
    [property: Description("Raw buckets or an exact revisioned local-deck selection.")]
    StatisticsPopulationInput Population,
    [property: Description("One through eight ordered independent attempt definitions.")]
    IReadOnlyList<MulliganAttemptInput> Attempts,
    [property: Description("Caller-owned conjunction applied to every non-forced attempt.")]
    IReadOnlyList<StatisticsGroupConditionInput> KeepConditions,
    [property: Description("Ordered exact groups bottomed first; use an empty array for canonical fallback.")]
    IReadOnlyList<string> BottomPriority,
    [property: Description("Optional conjunction evaluated after deterministic bottoming.")]
    IReadOnlyList<StatisticsGroupConditionInput>? FinalConditions = null);

/// <summary>
/// Requests the lowest bounded copy count meeting one exact target probability.
/// </summary>
internal sealed record StatisticsMinimumCountToolRequest(
    [property: Description("Closed engine-proven monotone event whose copy count varies.")]
    MinimumCountEventInput Event,
    [property: Description("Base-10 integer numerator of the exact target probability.")]
    string TargetNumerator,
    [property: Description("Positive base-10 integer denominator of the exact target probability.")]
    string TargetDenominator,
    [property: Description("Inclusive nonnegative minimum copy count to test.")]
    int MinimumCount,
    [property: Description("Inclusive maximum copy count to test.")]
    int MaximumCount);
