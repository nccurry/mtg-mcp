using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Exposes the one Archidekt workflow that mutates only revisioned local state.
/// </summary>
internal sealed class ArchidektLocalWriteTools
{
    /// <summary>
    /// Provides App-owned provider/local composition.
    /// </summary>
    private readonly ArchidektCoordinator coordinator;

    /// <summary>
    /// Stores effective authority for invocation-time defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the local-write surface.
    /// </summary>
    internal ArchidektLocalWriteTools(ArchidektCoordinator coordinator, OperationMode mode)
    {
        this.coordinator = coordinator;
        this.mode = mode;
    }

    /// <summary>
    /// Applies one unchanged pull preview transactionally to local storage.
    /// </summary>
    [McpServerTool(Name = "archidekt_pull_apply", Title = "Apply Archidekt Pull", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: refetches the remote deck, verifies all preview guards, then creates or replaces one local " +
        "deck in a single transaction. The guards are checked in order: a changed local deck is refused with " +
        "stale-deck-revision, a changed Archidekt deck with remote-deck-changed, and any other change to the " +
        "preview with preview-changed. Cards and categories added locally since the baseline are kept; any other " +
        "local change is refused (pull-conflict). Set discardLocalChanges to true, only with a pull preview made " +
        "with discardLocalChanges true, to replace the local deck with the Archidekt state and discard those " +
        "changes; that also needs confirmation exactly \"discard local changes <localDeckId>\".")]
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        [Description("The Archidekt deck ID, as in the preview.")] string remoteDeckId,
        [Description("The remoteFingerprint from the preview.")] string expectedRemoteFingerprint,
        [Description("The previewFingerprint from the preview.")] string previewFingerprint,
        [Description("The local deck ID, as in the preview; omit when the preview creates a local deck.")]
        Guid? localDeckId = null,
        [Description("The revision from the preview; omit when the preview creates a local deck.")]
        long? expectedRevision = null,
        [Description("Must equal the value the preview was made with; true takes Archidekt over local edits.")]
        bool discardLocalChanges = false,
        [Description(
            "Only with discardLocalChanges true: the exact text \"discard local changes <localDeckId>\" with the " +
            "local deck ID filled in.")]
        string? confirmation = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<ArchidektApplyResult>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        if (discardLocalChanges && localDeckId is { } discarded)
        {
            string expected = $"discard local changes {discarded:D}";
            if (!string.Equals(confirmation, expected, StringComparison.Ordinal))
            {
                return Task.FromResult<OperationResult<ArchidektApplyResult>>(new OperationInvalidInput(
                    SharedReasonCodes.ConfirmationRequired,
                    $"Pass confirmation exactly as: {expected}",
                    "confirmation"));
            }
        }

        return coordinator.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                remoteDeckId,
                localDeckId,
                expectedRevision,
                expectedRemoteFingerprint,
                previewFingerprint,
                discardLocalChanges),
            cancellationToken);
    }
}
