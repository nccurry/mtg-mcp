namespace MtgMcp.Core;

/// <summary>
/// Provides shared one-stop deckbuilding helpers.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Finds a commander name.
    /// </summary>
    private static string? FindCommanderName(DeckWorkspace workspace)
    {
        return workspace.Cards.FirstOrDefault(IsCommanderCard)?.Name;
    }

    /// <summary>
    /// Finds a dominant theme from tags.
    /// </summary>
    private static string? DominantTheme(DeckWorkspace workspace)
    {
        Dictionary<string, int> tags = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in IncludedCards(workspace))
        {
            foreach (string tag in DeckRoleClassifier.Classify(card).Tags)
            {
                AddCount(tags, tag, card.Quantity);
            }
        }

        return tags.OrderByDescending(pair => pair.Value).FirstOrDefault().Key;
    }

    /// <summary>
    /// Parses an optional date value.
    /// </summary>
    private static DateOnly? ParseDateOnly(string? value)
    {
        return DateOnly.TryParse(value, out DateOnly date) ? date : null;
    }

    /// <summary>
    /// Gets the current UTC date or the test override.
    /// </summary>
    private DateOnly CurrentDate()
    {
        return currentDateOverride ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    /// <summary>
    /// Gets the default recent-release lower bound.
    /// </summary>
    private DateOnly DefaultRecentReleaseDate()
    {
        return CurrentDate().AddYears(-1);
    }

    /// <summary>
    /// Checks whether an exception represents cooperative cancellation.
    /// </summary>
    private static bool IsCancellation(Exception exception)
    {
        return exception is OperationCanceledException;
    }

}
