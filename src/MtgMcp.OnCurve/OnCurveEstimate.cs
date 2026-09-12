using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Provides the public entry point and fixed limits for one on-curve estimate.
/// </summary>
public static class OnCurveEstimate
{
    /// <summary>
    /// Defines the largest number of distinct mainboard entries supported by one estimate.
    /// </summary>
    public const int MaximumMainboardEntries = 150;

    /// <summary>
    /// Defines the first turn supported by the fixed model.
    /// </summary>
    public const int MinimumTurnLimit = 1;

    /// <summary>
    /// Defines the last turn supported by the fixed model.
    /// </summary>
    public const int MaximumTurnLimit = 12;

    /// <summary>
    /// Defines the smallest supported number of sampled hands.
    /// </summary>
    public const int MinimumSampleCount = 100;

    /// <summary>
    /// Defines the largest supported number of sampled hands.
    /// </summary>
    public const int MaximumSampleCount = 100_000;

    /// <summary>
    /// Reports whether the turn and sample-count settings are within this model's fixed limits.
    /// </summary>
    public static bool HasSupportedRunSettings(int turnLimit, int sampleCount)
    {
        return turnLimit is >= MinimumTurnLimit and <= MaximumTurnLimit &&
            sampleCount is >= MinimumSampleCount and <= MaximumSampleCount;
    }

    /// <summary>
    /// Reports whether text is a valid fixed-width replay seed.
    /// </summary>
    public static bool IsValidReplaySeed(string? seed)
    {
        return seed is not null && OnCurveSeed.TryParse(seed, out _);
    }

    /// <summary>
    /// Calculates one repeatable estimate from fully resolved deck and card facts.
    /// </summary>
    public static OperationResult<OnCurveRunReport> Calculate(
        OnCurveRequest? request,
        CancellationToken cancellationToken = default)
    {
        return OnCurveCalculator.Calculate(request, cancellationToken);
    }
}
