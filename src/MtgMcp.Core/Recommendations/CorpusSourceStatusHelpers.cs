namespace MtgMcp.Core;

/// <summary>
/// Provides shared source-status rules for corpus-backed recommendation workflows.
/// </summary>
internal static class CorpusSourceStatusHelpers
{
    /// <summary>
    /// Merges duplicate source rows by key while preserving the most actionable status.
    /// </summary>
    public static List<CorpusSourceStatus> MergeSourceStatuses(IEnumerable<CorpusSourceStatus> sources)
    {
        return sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Key) || !string.IsNullOrWhiteSpace(source.Name))
            .GroupBy(source => string.IsNullOrWhiteSpace(source.Key) ? source.Name : source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(source => SourceStatusPriority(source.Status))
                .First())
            .OrderBy(source => source.Enabled ? 0 : 1)
            .ThenBy(source => source.Name)
            .ToList();
    }

    /// <summary>
    /// Checks whether a source row matches a requested source key or display name.
    /// </summary>
    public static bool MatchesSourceFilter(CorpusSourceStatus source, string? sourceKey)
    {
        return string.IsNullOrWhiteSpace(sourceKey)
            || source.Key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase)
            || source.Name.Equals(sourceKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ranks source statuses so blocked or failed query statuses are not hidden by an initial available row.
    /// </summary>
    private static int SourceStatusPriority(CorpusSourceStatusKind status)
    {
        return status switch
        {
            CorpusSourceStatusKind.AccessBlocked => 0,
            CorpusSourceStatusKind.Failed => 1,
            CorpusSourceStatusKind.MissingConfig => 2,
            CorpusSourceStatusKind.NeedsOAuth => 2,
            CorpusSourceStatusKind.Disabled => 3,
            CorpusSourceStatusKind.Available => 4,
            _ => 5
        };
    }
}
