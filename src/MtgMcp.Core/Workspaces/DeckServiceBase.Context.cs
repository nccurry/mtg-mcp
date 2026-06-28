namespace MtgMcp.Core;

/// <summary>
/// Shares commander, theme, date, and cancellation helpers across feature services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Finds the active command-zone commander display name.
    /// </summary>
    protected static string? FindCommanderName(DeckWorkspace workspace)
    {
        return DeckServiceHelpers.FindCommanderName(workspace);
    }

    /// <summary>
    /// Finds the commander query name, preferring active multi-card command zones over stale saved intent.
    /// </summary>
    protected static string? FindCommanderName(DeckWorkspace workspace, DeckIntent? intent)
    {
        return DeckServiceHelpers.FindCommanderName(workspace, intent);
    }

    /// <summary>
    /// Builds active command-zone facts for workspace-aware services.
    /// </summary>
    protected static CommandZoneContext FindCommandZoneContext(DeckWorkspace workspace)
    {
        return DeckServiceHelpers.FindCommandZoneContext(workspace);
    }

    /// <summary>
    /// Finds a dominant theme from tags.
    /// </summary>
    protected static string? DominantTheme(DeckWorkspace workspace)
    {
        return DeckServiceHelpers.DominantTheme(workspace);
    }

    /// <summary>
    /// Parses an optional date value.
    /// </summary>
    protected static DateOnly? ParseDateOnly(string? value)
    {
        return DeckServiceHelpers.ParseDateOnly(value);
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
        return DeckServiceHelpers.IsCancellation(exception);
    }

}
