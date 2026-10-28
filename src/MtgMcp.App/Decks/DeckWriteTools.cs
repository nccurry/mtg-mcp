using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes local deck creation, deletion, and backups only when the effective mode grants local writes.
/// Edits to an existing deck go through the batch tool, <c>local_deck_apply_changes</c>.
/// </summary>
internal sealed class DeckWriteTools
{
    /// <summary>
    /// Stores the local deck transaction boundary.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Stores the effective process authority for defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates write tools around one store and validated operation mode.
    /// </summary>
    internal DeckWriteTools(SqliteDeckStore store, OperationMode mode)
    {
        this.store = store;
        this.mode = mode;
    }

    /// <summary>
    /// Creates one local deck from explicit caller-owned content.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_create",
        Title = "Create Local Deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: creates one local deck from a complete caller-supplied deck graph, " +
        "without provider calls, legality inference, or entry coalescing.")]
    internal Task<OperationResult<DeckDocument>> CreateAsync(
        [Description("Complete caller-supplied initial local deck graph.")] DeckCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.CreateAsync(request, cancellationToken));
    }

    /// <summary>
    /// Deletes one revision-guarded deck.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_delete",
        Title = "Delete Local Deck",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: deletes one local deck and dependent rows when expectedRevision is current.")]
    internal Task<OperationResult<DeckDeleteResult>> DeleteAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.DeleteAsync(deckId, expectedRevision, cancellationToken));
    }

    /// <summary>
    /// Creates one verified opaque backup.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_backup_create",
        Title = "Create Local Deck Backup",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: creates one verified local decks.db snapshot and returns opaque metadata only.")]
    internal Task<OperationResult<DeckBackup>> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.Backups.CreateAsync(cancellationToken));
    }

    /// <summary>
    /// Restores one backup with a current-database fingerprint guard.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_backup_restore",
        Title = "Restore Local Deck Backup",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: restores a verified backup only when the supplied current database " +
        "fingerprint still matches.")]
    internal Task<OperationResult<DeckRestoreResult>> RestoreBackupAsync(
        [Description("Opaque backup UUID returned by local_deck_backup_list or local_deck_backup_create.")]
        Guid backupId,
        [Description("Current database fingerprint returned by local_deck_backup_list.")]
        string expectedDatabaseFingerprint,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.Backups.RestoreAsync(
            backupId,
            expectedDatabaseFingerprint,
            cancellationToken));
    }

    /// <summary>
    /// Deletes one opaque backup pair.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_backup_delete",
        Title = "Delete Local Deck Backup",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: deletes one local deck backup by opaque ID without accepting a filesystem path.")]
    internal Task<OperationResult<DeckDeleteResult>> DeleteBackupAsync(
        [Description("Opaque backup UUID returned by local_deck_backup_list or local_deck_backup_create.")]
        Guid backupId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.Backups.DeleteAsync(backupId, cancellationToken));
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

        return operation();
    }
}
