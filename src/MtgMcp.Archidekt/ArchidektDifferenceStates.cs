namespace MtgMcp.Archidekt;

/// <summary>
/// Names every state of an <see cref="ArchidektDifference"/> and decides once which of them are local changes and
/// which are remote changes, so the planner, the coordinator, and the preview summaries agree.
/// </summary>
public static class ArchidektDifferenceStates
{
    /// <summary>
    /// Names a row that only the local deck added since the baseline.
    /// </summary>
    public const string LocalAdded = "local-added";

    /// <summary>
    /// Names a row that only the local deck removed since the baseline.
    /// </summary>
    public const string LocalRemoved = "local-removed";

    /// <summary>
    /// Names a row that only the local deck changed since the baseline.
    /// </summary>
    public const string LocalChanged = "local-changed";

    /// <summary>
    /// Names a row that only Archidekt added since the baseline.
    /// </summary>
    public const string RemoteAdded = "remote-added";

    /// <summary>
    /// Names a row that only Archidekt removed since the baseline.
    /// </summary>
    public const string RemoteRemoved = "remote-removed";

    /// <summary>
    /// Names a row that only Archidekt changed since the baseline.
    /// </summary>
    public const string RemoteChanged = "remote-changed";

    /// <summary>
    /// Names a row that both sides changed to different values.
    /// </summary>
    public const string ConcurrentChanged = "concurrent-changed";

    /// <summary>
    /// Names a row that both sides changed to the same value.
    /// </summary>
    public const string Converged = "converged";

    /// <summary>
    /// Names the difference state of a schema 1 baseline whose local content differs with no row to show why.
    /// </summary>
    public const string LocalChangedUnverified = "local-changed-unverified";

    /// <summary>
    /// Tells whether the local deck changed the row since the baseline: a local add, removal, or change, or a
    /// concurrent change.
    /// </summary>
    public static bool IsLocalChange(ArchidektDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        return difference.State is LocalAdded or LocalRemoved or LocalChanged or LocalChangedUnverified
            or ConcurrentChanged;
    }

    /// <summary>
    /// Tells whether Archidekt changed the row since the baseline: a remote add, removal, or change, or a concurrent
    /// change.
    /// </summary>
    public static bool IsRemoteChange(ArchidektDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        return difference.State is RemoteAdded or RemoteRemoved or RemoteChanged or ConcurrentChanged;
    }

    /// <summary>
    /// Names the state of a row that exactly one side changed.
    /// </summary>
    /// <param name="localSide">True when the local deck changed the row, false when Archidekt did.</param>
    /// <param name="inBaseline">True when the baseline had the row.</param>
    /// <param name="inChangedSide">True when the side that changed it still has the row.</param>
    internal static string OneSided(bool localSide, bool inBaseline, bool inChangedSide)
    {
        if (!inBaseline)
        {
            return localSide ? LocalAdded : RemoteAdded;
        }

        if (!inChangedSide)
        {
            return localSide ? LocalRemoved : RemoteRemoved;
        }

        return localSide ? LocalChanged : RemoteChanged;
    }
}
