using MtgMcp.Archidekt;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Stops a push that would remove a large part of the Archidekt deck unless the caller confirms the exact count. A
/// push removes every Archidekt entry the local deck lacks, so a local deck that holds only part of the Archidekt deck
/// would otherwise delete the rest.
/// </summary>
internal static class ArchidektRemovalGuard
{
    /// <summary>
    /// Gives the most entries a push may remove without confirmation.
    /// </summary>
    internal const int MaximumUnconfirmedRemovals = 10;

    /// <summary>
    /// Counts the entries a plan removes from the Archidekt deck.
    /// </summary>
    internal static int CountRemovals(IReadOnlyList<ArchidektRemoteOperation> operations)
    {
        int removals = 0;
        foreach (ArchidektRemoteOperation operation in operations)
        {
            if (operation.Kind == "entry-remove")
            {
                removals++;
            }
        }

        return removals;
    }

    /// <summary>
    /// Explains why the apply would refuse the push, or returns null when it removes few enough entries or the caller
    /// confirmed the exact removal count. A push removes too many when it removes more than 10 entries or more than 10
    /// percent of the Archidekt entries.
    /// </summary>
    internal static string? Describe(int removals, int remoteEntryCount, int? confirmRemovals)
    {
        bool tooMany = removals > MaximumUnconfirmedRemovals || removals * 10 > remoteEntryCount;
        if (!tooMany || confirmRemovals == removals)
        {
            return null;
        }

        string passed = confirmRemovals is { } given
            ? $"confirmRemovals {given} does not match the {removals} planned removals. "
            : string.Empty;
        return $"archidekt_push_apply would refuse this push: it removes {removals} of {remoteEntryCount} Archidekt " +
            $"entries. {passed}If that is intended, pass confirmRemovals: {removals} to archidekt_push_preview and " +
            "archidekt_push_apply. If the local deck is only part of the Archidekt deck, run archidekt_pull_preview " +
            "first.";
    }
}
