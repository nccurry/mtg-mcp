using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes metadata refresh results so agents can request bounded or full workspace output.
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
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel);
        if (normalized == DetailLevel.Full)
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
        if (normalized == DetailLevel.Summary)
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

}
