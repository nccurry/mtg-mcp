using System.ComponentModel;
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
        Name = "scryfall_corpus_sync",
        Title = "Synchronize Scryfall Corpus",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Explicitly streams All Cards, Rulings, Oracle Tags, and Art Tags, validates a complete " +
        "generation, and atomically activates it.")]
    internal Task<OperationResult<ScryfallCorpusSyncResult>> SyncCorpusAsync(
        [Description("Metadata cache policy: default or refresh.")] string metadataPolicy = "default",
        [Description("Optional active generation guard checked before synchronization.")] Guid? expectedActiveGeneration = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.SyncCorpusAsync(metadataPolicy, expectedActiveGeneration, cancellationToken));
    }

    /// <summary>
    /// Swaps active and previous complete corpus generations.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_corpus_rollback",
        Title = "Roll Back Scryfall Corpus",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Guardedly swaps the active and previous complete corpus generations without changing deck or request snapshot data.")]
    internal Task<OperationResult<ScryfallCorpusMutationResult>> RollbackCorpusAsync(
        Guid expectedActiveGeneration,
        Guid expectedPreviousGeneration,
        bool acknowledgeActivationChange,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.RollbackCorpusAsync(expectedActiveGeneration, expectedPreviousGeneration,
            acknowledgeActivationChange, cancellationToken));
    }

    /// <summary>
    /// Deletes installed corpus generations under an active-generation guard.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_corpus_delete",
        Title = "Delete Scryfall Corpus",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Deletes installed corpus generations only with the current active generation and an explicit data-loss acknowledgement.")]
    internal Task<OperationResult<ScryfallCorpusMutationResult>> DeleteCorpusAsync(
        Guid expectedActiveGeneration,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.DeleteCorpusAsync(expectedActiveGeneration, acknowledgeDataLoss, cancellationToken));
    }

    /// <summary>
    /// Deletes one immutable exact-request snapshot under its checksum guard.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_snapshot_delete",
        Title = "Delete Scryfall Snapshot",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Deletes one immutable request snapshot only when its expected checksum matches and data loss is acknowledged.")]
    internal Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteSnapshotAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => service.DeleteSnapshotAsync(snapshotId, expectedChecksum,
            acknowledgeDataLoss, cancellationToken));
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
