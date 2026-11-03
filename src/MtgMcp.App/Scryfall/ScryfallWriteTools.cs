using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Exposes explicit guarded mutations of local Scryfall evidence.
/// </summary>
internal sealed class ScryfallWriteTools
{
    /// <summary>
    /// Stores the unified Scryfall boundary.
    /// </summary>
    private readonly ScryfallService service;

    /// <summary>
    /// Stores the effective process authority for defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates local mutation tools around one service and validated operation mode.
    /// </summary>
    internal ScryfallWriteTools(ScryfallService service, OperationMode mode)
    {
        this.service = service;
        this.mode = mode;
    }

    /// <summary>
    /// Synchronizes and atomically activates the fixed official corpus profile.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_data_sync",
        Title = "Synchronize Scryfall Card Data",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: explicitly streams All Cards, Rulings, Oracle Tags, and Art Tags, validates a " +
        "complete generation, and atomically activates it. freshnessPolicy default reuses cached bulk metadata; " +
        "refresh checks Scryfall again.")]
    internal Task<OperationResult<ScryfallCorpusSyncResult>> SyncCorpusAsync(
        [Description("Bulk metadata cache policy: default or refresh."), AllowedValues("default", "refresh")]
        string freshnessPolicy = "default",
        [Description("Optional active generation guard checked before synchronization.")] Guid? expectedActiveGeneration = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.SyncCorpusAsync(freshnessPolicy, expectedActiveGeneration, cancellationToken));
    }

    /// <summary>
    /// Swaps active and previous complete card-data generations.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_data_rollback",
        Title = "Roll Back Scryfall Card Data",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: guardedly swaps the active and previous complete card-data generations without " +
        "changing deck or request snapshot data. Rolling back again swaps them back. confirmation must be exactly " +
        "\"rollback card-data \" followed by expectedPreviousGeneration, the generation that becomes active.")]
    internal Task<OperationResult<ScryfallCorpusMutationResult>> RollbackCorpusAsync(
        [Description("Current active card-data generation UUID used as a stale-write guard.")] Guid expectedActiveGeneration,
        [Description("Current previous card-data generation UUID that should become active.")] Guid expectedPreviousGeneration,
        [Description("Exact text \"rollback card-data \" followed by expectedPreviousGeneration.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.RollbackCorpusAsync(expectedActiveGeneration, expectedPreviousGeneration,
            confirmation, cancellationToken));
    }

    /// <summary>
    /// Deletes installed card-data generations under an active-generation guard.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_data_delete",
        Title = "Delete Scryfall Card Data",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: deletes every installed card-data generation, active and previous, only with the " +
        "current active generation. A new sync reinstalls them. confirmation must be exactly \"delete card-data \" " +
        "followed by expectedActiveGeneration.")]
    internal Task<OperationResult<ScryfallCorpusMutationResult>> DeleteCorpusAsync(
        [Description("Current active card-data generation UUID used as a stale-write guard.")] Guid expectedActiveGeneration,
        [Description("Exact text \"delete card-data \" followed by expectedActiveGeneration.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.DeleteCorpusAsync(expectedActiveGeneration, confirmation, cancellationToken));
    }

    /// <summary>
    /// Deletes one immutable exact-request snapshot under its checksum guard.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_request_snapshot_delete",
        Title = "Delete Saved Scryfall Request",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Saved Scryfall requests: deletes one local snapshot of a Scryfall request (any request type) only when " +
        "its expected checksum matches and confirmation is exact. confirmation must be \"delete request-snapshot \" " +
        "followed by requestSnapshotId.")]
    internal Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteSnapshotAsync(
        [Description("Immutable request snapshot UUID to delete.")] Guid requestSnapshotId,
        [Description("Current snapshot checksum used as a stale-write guard.")] string expectedChecksum,
        [Description("Exact text \"delete request-snapshot \" followed by requestSnapshotId.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.DeleteSnapshotAsync(requestSnapshotId, expectedChecksum,
            confirmation, cancellationToken));
    }

    /// <summary>
    /// Enforces local-write authority at invocation time in addition to registration filtering.
    /// </summary>
    private Task<OperationResult<T>> ExecuteAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<T>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        return ScryfallToolExecution.RunAsync(operation);
    }
}
