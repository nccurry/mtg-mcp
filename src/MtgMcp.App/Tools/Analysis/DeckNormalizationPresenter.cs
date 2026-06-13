using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes metadata refresh results so agents can request compact or full workspace output.
/// </summary>
internal static class DeckNormalizationPresenter
{
    /// <summary>
    /// Maximum number of card names included outside full output.
    /// </summary>
    private const int SampleLimit = 20;

    /// <summary>
    /// Presents a refresh result at the requested detail level.
    /// </summary>
    public static object Present(DeckNormalizationResult result, string? detailLevel)
    {
        string normalized = NormalizeDetailLevel(detailLevel);
        if (normalized == DetailLevels.Full)
        {
            return result;
        }

        object summary = new
        {
            result.WorkspaceId,
            result.Scope,
            RequestedCardCount = result.RequestedCards,
            UpdatedCardCount = result.UpdatedCards,
            UnchangedCardCount = result.UnchangedCards,
            MissingCardCount = result.MissingCards.Count,
            FailedCardCount = result.FailedCards.Count,
            result.SnapshotQualityBefore,
            result.SnapshotQualityAfter
        };
        if (normalized == DetailLevels.Summary)
        {
            return summary;
        }

        return new
        {
            result.WorkspaceId,
            result.Scope,
            RequestedCardCount = result.RequestedCards,
            UpdatedCardCount = result.UpdatedCards,
            UnchangedCardCount = result.UnchangedCards,
            MissingCardCount = result.MissingCards.Count,
            FailedCardCount = result.FailedCards.Count,
            MissingCardSamples = result.MissingCards.Take(SampleLimit).ToList(),
            FailedCardSamples = result.FailedCards.Take(SampleLimit).ToList(),
            result.SnapshotQualityBefore,
            result.SnapshotQualityAfter
        };
    }

    /// <summary>
    /// Normalizes public detail-level values.
    /// </summary>
    private static string NormalizeDetailLevel(string? detailLevel)
    {
        string normalized = string.IsNullOrWhiteSpace(detailLevel)
            ? DetailLevels.Summary
            : detailLevel.Trim().ToLowerInvariant();
        if (normalized is DetailLevels.Summary or DetailLevels.Normal or DetailLevels.Full)
        {
            return normalized;
        }

        throw new ArgumentException("detailLevel must be summary, normal, or full.", nameof(detailLevel));
    }

    /// <summary>
    /// Public detail-level values for metadata refresh output.
    /// </summary>
    private static class DetailLevels
    {
        /// <summary>
        /// Bounded counts without card-name arrays or workspace payload.
        /// </summary>
        public const string Summary = "summary";

        /// <summary>
        /// Bounded counts plus small missing and failed card samples.
        /// </summary>
        public const string Normal = "normal";

        /// <summary>
        /// Original full normalization result including the workspace.
        /// </summary>
        public const string Full = "full";
    }
}
