using System.Globalization;
using System.Text;

namespace MtgMcp.Core;

/// <summary>
/// Builds a developer-facing Markdown summary for one Stats Lab performance analysis.
/// </summary>
internal static class DeckPerformanceMarkdownSummary
{
    /// <summary>
    /// Creates a compact Markdown summary without implying an objective deck power ranking.
    /// </summary>
    public static string Build(DeckPerformanceAnalysis analysis)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Stats Lab Performance Summary");
        builder.AppendLine();
        builder.AppendLine(
            "Stats Lab metrics are deterministic heuristic estimates for the supplied deck, "
                + "profile, seed, simulation count, and turn horizon. They are not an objective "
                + "deck power score or a full-rules game result.");
        builder.AppendLine();
        builder.AppendLine("## Replay Metadata");
        builder.AppendLine();
        AppendInvariant(builder, $"- Workspace: `{analysis.WorkspaceId}`");
        AppendInvariant(builder, $"- Model version: `{analysis.ModelVersion}`");
        AppendInvariant(builder, $"- Schema version: {analysis.SchemaVersion}");
        AppendInvariant(builder, $"- Profile: `{analysis.Profile}` ({analysis.ProfileResolution.Source})");
        AppendInvariant(builder, $"- Profile fingerprint: `{analysis.ProfileFingerprint}`");
        AppendInvariant(builder, $"- Deck fingerprint: `{analysis.DeckFingerprint}`");
        AppendInvariant(builder, $"- Card data fingerprint: `{analysis.CardDataFingerprint}`");
        AppendInvariant(builder, $"- RNG: `{analysis.RngKind}`");
        AppendInvariant(builder, $"- Seed: {analysis.Seed}");
        AppendInvariant(builder, $"- Simulations: {analysis.Simulations}");
        AppendInvariant(builder, $"- Max turn: {analysis.MaxTurn}");
        AppendInvariant(builder, $"- Mulligans: {analysis.IncludeMulligans}");

        AppendScorecard(builder, analysis);
        AppendScenarios(builder, analysis);
        AppendTraceSummary(builder, analysis);
        AppendWarnings(builder, analysis);
        return builder.ToString();
    }

    /// <summary>
    /// Appends scorecard dimensions as individual metric signals.
    /// </summary>
    private static void AppendScorecard(StringBuilder builder, DeckPerformanceAnalysis analysis)
    {
        builder.AppendLine();
        builder.AppendLine("## Scorecard Dimensions");
        builder.AppendLine();
        builder.AppendLine("| Dimension | Score | Source metric | Rationale |");
        builder.AppendLine("|---|---:|---|---|");
        foreach (PerformanceScorecardDimension dimension in analysis.Scorecard.Dimensions)
        {
            string[] columns =
            [
                $"`{dimension.Name}`",
                dimension.Score.ToString("0.000", CultureInfo.InvariantCulture),
                $"`{dimension.SourceMetric}`",
                dimension.Rationale,
            ];
            AppendMarkdownRow(builder, columns);
        }
    }

    /// <summary>
    /// Appends named scenario rates and common failure drivers.
    /// </summary>
    private static void AppendScenarios(StringBuilder builder, DeckPerformanceAnalysis analysis)
    {
        builder.AppendLine();
        builder.AppendLine("## Key Scenarios");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Turn | Rate | 95% CI | Relevant cards | Failure drivers |");
        builder.AppendLine("|---|---:|---:|---|---|---|");
        foreach (ScenarioPerformance scenario in analysis.Scenarios)
        {
            string interval =
                $"{scenario.LowConfidenceInterval.ToString("0.000", CultureInfo.InvariantCulture)}-"
                + $"{scenario.HighConfidenceInterval.ToString("0.000", CultureInfo.InvariantCulture)}";
            string[] columns =
            [
                $"`{scenario.Name}`",
                scenario.TargetTurn.ToString(CultureInfo.InvariantCulture),
                scenario.SuccessRate.ToString("0.000", CultureInfo.InvariantCulture),
                interval,
                JoinFirst(scenario.RelevantCards, 5),
                JoinFirst(scenario.FailureDrivers, 3),
            ];
            AppendMarkdownRow(builder, columns);
        }
    }

    /// <summary>
    /// Appends bounded trace-summary context.
    /// </summary>
    private static void AppendTraceSummary(StringBuilder builder, DeckPerformanceAnalysis analysis)
    {
        builder.AppendLine();
        builder.AppendLine("## Trace Summary");
        builder.AppendLine();
        AppendInvariant(builder, $"- Sampled runs: {analysis.TraceSummary.SampledRuns.Count}");
        foreach (KeyValuePair<string, int> counter in analysis.TraceSummary.AggregateCounters)
        {
            AppendInvariant(builder, $"- `{counter.Key}`: {counter.Value}");
        }

        foreach (string note in analysis.TraceSummary.Notes)
        {
            AppendInvariant(builder, $"- {note}");
        }
    }

    /// <summary>
    /// Appends analyzer warnings or an explicit none marker.
    /// </summary>
    private static void AppendWarnings(StringBuilder builder, DeckPerformanceAnalysis analysis)
    {
        builder.AppendLine();
        builder.AppendLine("## Warnings");
        builder.AppendLine();
        if (analysis.Warnings.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        foreach (string warning in analysis.Warnings)
        {
            AppendInvariant(builder, $"- {warning}");
        }
    }

    /// <summary>
    /// Appends a Markdown table row after escaping cell delimiters.
    /// </summary>
    private static void AppendMarkdownRow(StringBuilder builder, IEnumerable<string> columns)
    {
        List<string> escaped = [];
        foreach (string column in columns)
        {
            escaped.Add(EscapeMarkdownCell(column));
        }

        builder.Append("| ");
        builder.Append(string.Join(" | ", escaped));
        builder.AppendLine(" |");
    }

    /// <summary>
    /// Escapes Markdown table control characters.
    /// </summary>
    private static string EscapeMarkdownCell(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    /// <summary>
    /// Joins a bounded number of values for compact table cells.
    /// </summary>
    private static string JoinFirst(IReadOnlyList<string> values, int limit)
    {
        if (values.Count == 0)
        {
            return "";
        }

        List<string> selected = [];
        int count = Math.Min(values.Count, limit);
        for (int index = 0; index < count; index++)
        {
            selected.Add(values[index]);
        }

        if (values.Count > limit)
        {
            selected.Add($"and {values.Count - limit} more");
        }

        return string.Join(", ", selected);
    }

    /// <summary>
    /// Appends an interpolated line using invariant formatting.
    /// </summary>
    private static void AppendInvariant(StringBuilder builder, FormattableString value)
    {
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }
}
