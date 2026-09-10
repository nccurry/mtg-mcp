namespace MtgMcp.OnCurve;

/// <summary>
/// Calculates the two-sided Wilson interval used to show sampling uncertainty.
/// </summary>
internal static class OnCurveWilsonIntervalCalculator
{
    /// <summary>
    /// Defines the standard-normal value for a two-sided 95 percent interval.
    /// </summary>
    private const double WilsonZ = 1.959963984540054;

    /// <summary>
    /// Calculates an unrounded two-sided 95 percent Wilson interval.
    /// </summary>
    internal static OnCurveWilsonInterval Calculate(int successes, int samples)
    {
        if (samples <= 0 || successes < 0 || successes > samples)
        {
            throw new ArgumentOutOfRangeException(
                nameof(successes),
                "Successes must be between zero and the completed sample count.");
        }

        double sampleCount = samples;
        double rate = successes / sampleCount;
        double zSquared = WilsonZ * WilsonZ;
        double denominator = 1 + (zSquared / sampleCount);
        double center = (rate + (zSquared / (2 * sampleCount))) / denominator;
        double margin = WilsonZ * Math.Sqrt(
            ((rate * (1 - rate)) + (zSquared / (4 * sampleCount))) / sampleCount) / denominator;
        double lowerBound = successes == 0 ? 0 : Math.Max(0, center - margin);
        double upperBound = successes == samples ? 1 : Math.Min(1, center + margin);
        return new OnCurveWilsonInterval(lowerBound, upperBound);
    }
}
