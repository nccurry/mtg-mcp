namespace MtgMcp.Core;

/// <summary>
/// Builds shared probability, interval, and percentile rows for Stats Lab reports.
/// </summary>
internal static class PerformanceStatistics
{
    /// <summary>
    /// Creates one named probability row with confidence bounds.
    /// </summary>
    public static PerformanceProbability BuildProbability(
        string name,
        int turn,
        int successes,
        int sampleSize)
    {
        (double low, double high) = ConfidenceInterval(successes, sampleSize);
        return new PerformanceProbability
        {
            Name = name,
            Turn = turn,
            Probability = Rate(successes, sampleSize),
            LowConfidenceInterval = low,
            HighConfidenceInterval = high,
            SampleSize = sampleSize,
        };
    }

    /// <summary>
    /// Creates one named average row with percentile bands.
    /// </summary>
    public static PerformanceAverage BuildAverage(
        string name,
        int turn,
        IReadOnlyList<double> values)
    {
        return new PerformanceAverage
        {
            Name = name,
            Turn = turn,
            Average = values.Count == 0 ? 0 : values.Average(),
            P25 = Percentile(values, 0.25),
            P50 = Percentile(values, 0.50),
            P75 = Percentile(values, 0.75),
            SampleSize = values.Count,
        };
    }

    /// <summary>
    /// Calculates a rate while guarding zero samples.
    /// </summary>
    public static double Rate(int successes, int sampleSize)
    {
        return sampleSize <= 0 ? 0 : successes / (double)sampleSize;
    }

    /// <summary>
    /// Calculates an approximate 95 percent Wilson confidence interval.
    /// </summary>
    public static (double Low, double High) ConfidenceInterval(int successes, int sampleSize)
    {
        if (sampleSize <= 0)
        {
            return (0, 0);
        }

        const double z = 1.96;
        double proportion = successes / (double)sampleSize;
        double denominator = 1 + (z * z / sampleSize);
        double center = proportion + (z * z / (2 * sampleSize));
        double margin = z * Math.Sqrt(
            ((proportion * (1 - proportion)) + (z * z / (4 * sampleSize))) / sampleSize);
        return (
            Math.Clamp((center - margin) / denominator, 0, 1),
            Math.Clamp((center + margin) / denominator, 0, 1));
    }

    /// <summary>
    /// Calculates a nearest-rank percentile from numeric samples.
    /// </summary>
    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(value => value).ToList();
        int index = Math.Clamp((int)Math.Round((sorted.Count - 1) * percentile), 0, sorted.Count - 1);
        return sorted[index];
    }
}
