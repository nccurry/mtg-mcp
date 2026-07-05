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
    [Description("Refetches Archidekt, verifies all preview guards, then creates or replaces one local deck in a single transaction.")]
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<ArchidektApplyResult>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        return coordinator.ApplyPullAsync(request, cancellationToken);
    }
}
