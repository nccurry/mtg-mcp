namespace MtgMcp.Core;

/// <summary>
/// Provides Playgroup deck ranking behavior.
/// </summary>
public sealed partial class PlaygroupService
{
    /// <summary>
    /// Ranks decks seen in fetched playgroup games by a supported Playgroup metric.
    /// </summary>
    public async Task<PlaygroupDeckRankingResult> RankDecksAsync(
        string playgroupIdOrUrl,
        string? metric,
        int minGames,
        bool includeLowConfidence,
        int maxGames,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string normalizedMetric = NormalizeMetric(metric);
        PlaygroupDeckListResult list = await ListDecksAsync(
                playgroupIdOrUrl,
                maxGames,
                MaximumDeckLimit,
                cancellationToken
            )
            .ConfigureAwait(false);
        int normalizedLimit = Clamp(limit, min: 1, max: MaximumDeckLimit);
        int normalizedMinGames = Math.Max(0, minGames);

        int minGameFiltered = 0;
        int lowConfidenceFiltered = 0;
        int missingMetricFiltered = 0;
        List<(PlaygroupDeckSummary Deck, double Score)> scored = [];
        foreach (PlaygroupDeckSummary deck in list.Decks)
        {
            if (deck.FetchedPlaygroupGames < normalizedMinGames)
            {
                minGameFiltered++;
                continue;
            }

            if (
                normalizedMetric == PlaygroupDeckRankingMetrics.EstimatedPower
                && !includeLowConfidence
                && deck.ConfidenceFactor is < LowConfidenceThreshold
            )
            {
                lowConfidenceFiltered++;
                continue;
            }

            double? score = GetMetricScore(deck, normalizedMetric);
            if (!score.HasValue)
            {
                missingMetricFiltered++;
                continue;
            }

            scored.Add((deck, score.Value));
        }

        IEnumerable<(PlaygroupDeckSummary Deck, double Score)> ordered =
            normalizedMetric == PlaygroupDeckRankingMetrics.AverageWinTurn
                ? scored.OrderBy(item => item.Score)
                : scored.OrderByDescending(item => item.Score);

        List<PlaygroupDeckRanking> rankings = ordered
            .Take(normalizedLimit)
            .Select((item, index) => new PlaygroupDeckRanking
            {
                Rank = index + 1,
                Score = item.Score,
                Deck = item.Deck,
            })
            .ToList();

        List<string> warnings = [.. list.Warnings];
        if (normalizedMetric == PlaygroupDeckRankingMetrics.EstimatedPower)
        {
            warnings.Add("estimated_power uses Playgroup power_level and falls back to playgroup-scoped Elo when power_level is missing.");
        }

        if (minGameFiltered > 0)
        {
            warnings.Add($"{minGameFiltered} decks were excluded for having fewer than {normalizedMinGames} fetched playgroup games.");
        }

        if (lowConfidenceFiltered > 0)
        {
            warnings.Add($"{lowConfidenceFiltered} low-confidence power estimates were excluded.");
        }

        if (missingMetricFiltered > 0)
        {
            warnings.Add($"{missingMetricFiltered} decks were excluded because the metric was missing.");
        }

        return new PlaygroupDeckRankingResult
        {
            PlaygroupId = list.PlaygroupId,
            Metric = normalizedMetric,
            Rankings = rankings,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Normalizes a caller-supplied ranking metric.
    /// </summary>
    private static string NormalizeMetric(string? metric)
    {
        string normalized = string.IsNullOrWhiteSpace(metric)
            ? PlaygroupDeckRankingMetrics.EstimatedPower
            : metric.Trim().ToLowerInvariant();
        if (!PlaygroupDeckRankingMetrics.All.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unsupported Playgroup deck ranking metric '{metric}'. Supported metrics: {string.Join(", ", PlaygroupDeckRankingMetrics.All)}.",
                nameof(metric)
            );
        }

        return normalized;
    }

    /// <summary>
    /// Reads the score value used by a ranking metric.
    /// </summary>
    private static double? GetMetricScore(PlaygroupDeckSummary deck, string metric)
    {
        return metric switch
        {
            PlaygroupDeckRankingMetrics.EstimatedPower => deck.EstimatedPower ?? deck.Elo,
            PlaygroupDeckRankingMetrics.Elo => deck.Elo,
            PlaygroupDeckRankingMetrics.WinRate =>
                deck.FetchedPlaygroupWinRatePercentage ?? deck.WinRatePercentage,
            PlaygroupDeckRankingMetrics.CompetitiveRating => deck.CompetitivenessRating,
            PlaygroupDeckRankingMetrics.GamesPlayed => deck.FetchedPlaygroupGames,
            PlaygroupDeckRankingMetrics.AverageWinTurn => deck.AverageWinsByRound,
            _ => null,
        };
    }
}
