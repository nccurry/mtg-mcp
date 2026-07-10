using System.ComponentModel;

namespace MtgMcp.Statistics;

/// <summary>
/// Defines one inclusive observed-group count condition.
/// </summary>
public sealed record StatisticsGroupConditionInput(
    [property: Description("Exact declared group name to observe.")]
    string Group,
    [property: Description("Inclusive nonnegative minimum observed copies.")]
    int Minimum,
    [property: Description("Optional inclusive maximum observed copies; null has no upper bound.")]
    int? Maximum = null);

/// <summary>
/// Requests one exact conjunction over overlapping observed groups.
/// </summary>
public sealed record MultivariateRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("Number of cards drawn without replacement.")]
    int DrawCount,
    [property: Description("One through eight caller-owned group minimum/maximum conditions.")]
    IReadOnlyList<StatisticsGroupConditionInput> Conditions);

/// <summary>
/// Carries one complete exact multivariate observation result.
/// </summary>
public sealed record MultivariateResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    int DrawCount,
    IReadOnlyList<StatisticsGroupConditionInput> Conditions,
    ExactProbability Probability,
    ExactProbability Complement)
{
    /// <summary>
    /// Gets canonical conditions ordered by exact group name.
    /// </summary>
    public IReadOnlyList<StatisticsGroupConditionInput> Conditions { get; init; } =
        Array.AsReadOnly(Conditions.ToArray());
}

/// <summary>
/// Defines one required package slot family and the caller groups able to satisfy it.
/// </summary>
public sealed record PackageRequirementInput(
    [property: Description("Unique caller-owned requirement name.")]
    string Name,
    [property: Description("Positive number of physical slots required.")]
    int Count,
    [Description("Exact declared card or tutor groups that may satisfy one slot.")]
    IReadOnlyList<string> EligibleGroups)
{
    /// <summary>
    /// Gets an immutable copy of eligible group names.
    /// </summary>
    public IReadOnlyList<string> EligibleGroups { get; init; } = EligibleGroups is null
        ? null!
        : Array.AsReadOnly(EligibleGroups.ToArray());
}

/// <summary>
/// Requests exact one-use allocation of drawn card copies to package requirements.
/// </summary>
public sealed record PackageAssemblyRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("Number of cards drawn without replacement.")]
    int DrawCount,
    [property: Description("One through eight package requirement families.")]
    IReadOnlyList<PackageRequirementInput> Requirements);

/// <summary>
/// Carries one complete exact package-assembly result.
/// </summary>
public sealed record PackageAssemblyResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    int DrawCount,
    IReadOnlyList<PackageRequirementInput> Requirements,
    ExactProbability Probability,
    ExactProbability Complement)
{
    /// <summary>
    /// Gets canonical requirements ordered by exact caller-owned name.
    /// </summary>
    public IReadOnlyList<PackageRequirementInput> Requirements { get; init; } =
        Array.AsReadOnly(Requirements.ToArray());
}
