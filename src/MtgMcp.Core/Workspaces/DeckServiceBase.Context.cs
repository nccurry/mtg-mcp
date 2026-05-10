namespace MtgMcp.Core;

/// <summary>
/// Shares commander, theme, date, and cancellation helpers across feature services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Finds a commander name.
    /// </summary>
    protected static string? FindCommanderName(DeckWorkspace workspace)
    {
        return workspace.Cards.FirstOrDefault(IsCommanderCard)?.Name;
    }

    /// <summary>
    /// Finds a dominant theme from tags.
    /// </summary>
    protected static string? DominantTheme(DeckWorkspace workspace)
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
    protected static DateOnly? ParseDateOnly(string? value)
    {
        return DateOnly.TryParse(value, out DateOnly date) ? date : null;
    }

    /// <summary>
    /// Gets the current UTC date or the test override.
    /// </summary>
    protected DateOnly CurrentDate()
    {
        return CurrentDateOverride ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    /// <summary>
    /// Gets the default recent-release lower bound.
    /// </summary>
    protected DateOnly DefaultRecentReleaseDate()
    {
        return CurrentDate().AddYears(-1);
    }

    /// <summary>
    /// Checks whether an exception represents cooperative cancellation.
    /// </summary>
    protected static bool IsCancellation(Exception exception)
    {
        return exception is OperationCanceledException;
    }

}
