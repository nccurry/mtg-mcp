namespace MtgMcp.Archidekt;

/// <summary>
/// Names the states of an <see cref="ArchidektDifference"/> and decides once which of them are local changes and
/// which are remote changes, so the planner, the coordinator, and the preview summaries agree.
/// </summary>
public static class ArchidektDifferenceStates
{
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
        return difference.State.StartsWith("local-", StringComparison.Ordinal) ||
            difference.State == ConcurrentChanged;
    }

    /// <summary>
    /// Tells whether Archidekt changed the row since the baseline: a remote add, removal, or change, or a concurrent
    /// change.
    /// </summary>
    public static bool IsRemoteChange(ArchidektDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        return difference.State.StartsWith("remote-", StringComparison.Ordinal) ||
            difference.State == ConcurrentChanged;
    }
}
