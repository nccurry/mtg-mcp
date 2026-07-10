using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MtgMcp.Statistics;

/// <summary>
/// Declares the complete number of additional cards seen during one caller-owned turn row.
/// </summary>
public sealed record TurnDrawInput(
    [property: Description("Strictly increasing positive caller-owned turn number.")]
    int Turn,
    [property: Description("Complete nonnegative additional cards seen during this turn.")]
    int Draws);

/// <summary>
/// Requests one exact success-event table over an explicit draw schedule.
/// </summary>
public sealed record TurnTableRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("Exact declared group whose members count as successes.")]
    string SuccessGroup,
    [property: Description("Nonnegative cards seen before the first supplied turn row.")]
    int OpeningHandSize,
    [property: Description("One through fifty ordered rows of complete additional cards seen by turn.")]
    IReadOnlyList<TurnDrawInput> DrawsByTurn,
    [property: Description("Closed exact success event evaluated at every cumulative row.")]
    HypergeometricEventInput Event);

/// <summary>
/// Carries one exact turn-table row.
/// </summary>
public sealed record TurnTableRow(
    int Turn,
    int Draws,
    int CardsSeen,
    HypergeometricEventSnapshot Event,
    ExactProbability Probability,
    ExactProbability Complement);

/// <summary>
/// Carries one complete exact probability-by-turn table.
/// </summary>
public sealed record TurnTableResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    string SuccessGroup,
    int SuccessCount,
    int OpeningHandSize,
    IReadOnlyList<TurnTableRow> Rows)
{
    /// <summary>
    /// Gets immutable caller rows in strictly increasing turn order.
    /// </summary>
    public IReadOnlyList<TurnTableRow> Rows { get; init; } = Array.AsReadOnly(Rows.ToArray());
}

/// <summary>
/// Declares one exact source group and the mana symbols one copy can produce.
/// </summary>
public sealed record ManaSourceInput(
    [property: Description("Unique exact declared population group for this source type.")]
    string Group,
    [Description("One or more distinct uppercase capabilities from W, U, B, R, G, and C.")]
    IReadOnlyList<string> Capabilities)
{
    /// <summary>
    /// Gets an immutable copy of caller-declared source capabilities.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Capabilities is null
        ? null!
        : Array.AsReadOnly(Capabilities.ToArray());
}

/// <summary>
/// Declares exact colored, colorless, and generic one-unit payment requirements.
/// </summary>
public sealed record ManaRequirementInput(
    [property: Description("Required white mana units.")] int White = 0,
    [property: Description("Required blue mana units.")] int Blue = 0,
    [property: Description("Required black mana units.")] int Black = 0,
    [property: Description("Required red mana units.")] int Red = 0,
    [property: Description("Required green mana units.")] int Green = 0,
    [property: Description("Required colorless mana units.")] int Colorless = 0,
    [property: Description("Required generic units payable by any otherwise unused source.")] int Generic = 0);

/// <summary>
/// Requests exact availability of caller-declared one-unit mana sources.
/// </summary>
public sealed record ManaAvailabilityRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("Number of cards seen without replacement.")]
    int DrawCount,
    [property: Description("Distinct caller-declared source groups and production capabilities.")]
    IReadOnlyList<ManaSourceInput> Sources,
    [property: Description("Exact colored, colorless, and generic payment requirement.")]
    ManaRequirementInput Requirement,
    [property: Description("Maximum total drawn sources that may be assigned; no land meaning is inferred.")]
    int MaximumUsableSources);

/// <summary>
/// Carries one complete exact mana-availability event and complement.
/// </summary>
public sealed record ManaAvailabilityResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    int DrawCount,
    IReadOnlyList<ManaSourceInput> Sources,
    ManaRequirementInput Requirement,
    int MaximumUsableSources,
    ExactProbability Probability,
    ExactProbability Complement)
{
    /// <summary>
    /// Gets canonical sources ordered by exact group name.
    /// </summary>
    public IReadOnlyList<ManaSourceInput> Sources { get; init; } = Array.AsReadOnly(Sources.ToArray());
}

/// <summary>
/// Defines the engine-proven monotone event cases accepted by the inverse count solver.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MinimumHypergeometricCountEvent), "hypergeometric-at-least")]
[JsonDerivedType(typeof(MinimumTurnCountEvent), "turn-at-least")]
[JsonDerivedType(typeof(MinimumManaCountEvent), "mana-availability")]
public abstract record MinimumCountEventInput;

/// <summary>
/// Varies success copies for a fixed hypergeometric at-least event.
/// </summary>
public sealed record MinimumHypergeometricCountEvent(
    [property: Description("Fixed positive population size.")] int PopulationSize,
    [property: Description("Fixed number of cards drawn.")] int DrawCount,
    [property: Description("Nonnegative required successes in the draw.")] int RequiredSuccesses)
    : MinimumCountEventInput;

/// <summary>
/// Varies success copies for one explicit cumulative turn-schedule at-least event.
/// </summary>
public sealed record MinimumTurnCountEvent(
    [property: Description("Fixed positive population size.")] int PopulationSize,
    [property: Description("Nonnegative cards seen before the first turn row.")] int OpeningHandSize,
    [property: Description("Ordered explicit additional cards seen by turn.")]
    IReadOnlyList<TurnDrawInput> DrawsByTurn,
    [property: Description("Exact supplied turn row to evaluate.")] int Turn,
    [property: Description("Nonnegative required successes by that row.")] int RequiredSuccesses)
    : MinimumCountEventInput;

/// <summary>
/// Varies copies of one repeated identical source template for exact mana availability.
/// </summary>
public sealed record MinimumManaCountEvent(
    [property: Description("Fixed positive population size.")] int PopulationSize,
    [property: Description("Fixed number of cards seen.")] int DrawCount,
    [Description("One or more distinct uppercase source capabilities from W, U, B, R, G, and C.")]
    IReadOnlyList<string> SourceCapabilities,
    [property: Description("Exact colored, colorless, and generic payment requirement.")]
    ManaRequirementInput Requirement,
    [property: Description("Maximum total copies of the repeated source that may be assigned.")]
    int MaximumUsableSources)
    : MinimumCountEventInput
{
    /// <summary>
    /// Gets an immutable copy of the repeated source capabilities.
    /// </summary>
    public IReadOnlyList<string> SourceCapabilities { get; init; } = SourceCapabilities is null
        ? null!
        : Array.AsReadOnly(SourceCapabilities.ToArray());
}

/// <summary>
/// Requests the lowest bounded copy count meeting one exact target probability.
/// </summary>
public sealed record MinimumCountRequest(
    [property: Description("Closed engine-proven monotone event whose success/source count varies.")]
    MinimumCountEventInput Event,
    [property: Description("Reduced or reducible base-10 target numerator string.")]
    string TargetNumerator,
    [property: Description("Positive base-10 target denominator string.")]
    string TargetDenominator,
    [property: Description("Inclusive nonnegative minimum copy count to test.")]
    int MinimumCount,
    [property: Description("Inclusive maximum copy count to test.")]
    int MaximumCount);

/// <summary>
/// Carries the exact inverse-count result and neighboring monotonicity evidence.
/// </summary>
public sealed record MinimumCountResult(
    StatisticsDerivation Derivation,
    string EventKind,
    int MinimumCount,
    int MaximumCount,
    ExactProbability Target,
    bool Found,
    int? Count,
    ExactProbability? Probability,
    int? PreviousCount,
    ExactProbability? PreviousProbability,
    int HighestTestedCount,
    ExactProbability HighestTestedProbability);
