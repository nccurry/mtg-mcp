using MtgMcp.App.Archidekt;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Lets workflow tests describe the arguments of the push and pull apply tools as one guard record, and calls the
/// flat tool method with the same values.
/// </summary>
internal static class ArchidektToolRequestExtensions
{
    /// <summary>
    /// Calls the push apply tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken)
    {
        return tools.ApplyPushAsync(
            request.LocalDeckId,
            request.ExpectedRevision,
            request.ExpectedRemoteFingerprint,
            request.PreviewFingerprint,
            request.ConfirmRemovals,
            cancellationToken);
    }

    /// <summary>
    /// Calls the pull apply tool with the fields of a request, adding the confirmation a discarding pull needs.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        this ArchidektLocalWriteTools tools,
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        return tools.ApplyPullAsync(
            request.RemoteDeckId,
            request.ExpectedRemoteFingerprint,
            request.PreviewFingerprint,
            request.LocalDeckId,
            request.ExpectedRevision,
            request.DiscardLocalChanges,
            request.DiscardLocalChanges && request.LocalDeckId is { } local ? $"discard local changes {local:D}" : null,
            cancellationToken);
    }
}
