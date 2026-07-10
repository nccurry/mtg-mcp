using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MtgMcp.Statistics;

/// <summary>
/// Defines the closed univariate success events accepted by exact engines.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HypergeometricExactlyEvent), "exactly")]
[JsonDerivedType(typeof(HypergeometricZeroEvent), "zero")]
[JsonDerivedType(typeof(HypergeometricAtLeastEvent), "at-least")]
[JsonDerivedType(typeof(HypergeometricAtMostEvent), "at-most")]
[JsonDerivedType(typeof(HypergeometricRangeEvent), "range")]
public abstract record HypergeometricEventInput;

/// <summary>
/// Selects exactly one nonnegative success count.
/// </summary>
public sealed record HypergeometricExactlyEvent(
    [property: Description("Exact nonnegative number of successes in the draw.")]
    int Count) : HypergeometricEventInput;

/// <summary>
/// Selects exactly zero successes.
/// </summary>
public sealed record HypergeometricZeroEvent : HypergeometricEventInput;

/// <summary>
/// Selects draws with at least one nonnegative success count.
/// </summary>
public sealed record HypergeometricAtLeastEvent(
    [property: Description("Inclusive nonnegative minimum number of successes.")]
    int Count) : HypergeometricEventInput;

/// <summary>
/// Selects draws with at most one nonnegative success count.
/// </summary>
public sealed record HypergeometricAtMostEvent(
    [property: Description("Inclusive nonnegative maximum number of successes.")]
    int Count) : HypergeometricEventInput;

/// <summary>
/// Selects one inclusive nonnegative success-count range.
/// </summary>
public sealed record HypergeometricRangeEvent(
    [property: Description("Inclusive nonnegative minimum number of successes.")]
    int Minimum,
    [property: Description("Inclusive maximum number of successes, not less than minimum.")]
    int Maximum) : HypergeometricEventInput;

/// <summary>
/// Requests one exact univariate probability over a resolved population.
/// </summary>
public sealed record HypergeometricRequest(
    [property: Description("Caller-supplied raw or exactly resolved deck population.")]
    StatisticsPopulation Population,
    [property: Description("Exact declared group whose members count as successes.")]
    string SuccessGroup,
    [property: Description("Number of cards drawn without replacement.")]
    int DrawCount,
    [property: Description("Closed exact success event to evaluate.")]
    HypergeometricEventInput Event);

/// <summary>
/// Records the normalized inclusive success range evaluated by the engine.
/// </summary>
public sealed record HypergeometricEventSnapshot(
    string Kind,
    int Minimum,
    int Maximum);

/// <summary>
/// Carries one complete univariate exact analysis and replay inputs.
/// </summary>
public sealed record HypergeometricResult(
    StatisticsDerivation Derivation,
    StatisticsPopulationSnapshot Population,
    string SuccessGroup,
    int SuccessCount,
    int DrawCount,
    HypergeometricEventSnapshot Event,
    ExactProbability Probability,
    ExactProbability Complement,
    ExactRationalValue Expectation,
    ExactRationalValue Variance);
