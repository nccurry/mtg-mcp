using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes local deck mutations only when the effective mode grants local writes.
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
        Name = "deck_create",
        Title = "Create Local Deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates one local deck without provider calls, legality inference, or entry coalescing.")]
    internal Task<OperationResult<DeckDocument>> CreateAsync(
        [Description("Complete caller-supplied initial local deck graph.")] DeckCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.CreateAsync(request, cancellationToken));
    }

    /// <summary>
    /// Replaces caller-editable deck metadata through the shared change transaction.
    /// </summary>
    [McpServerTool(
        Name = "deck_update",
        Title = "Update Local Deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Updates local deck name, description, and format when expectedRevision is current.")]
    internal Task<OperationResult<DeckDocument>> UpdateAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Complete replacement deck name.")] string name,
        [Description("Complete replacement description, or null to clear it.")] string? description,
        [Description("Complete replacement format label; legality is not evaluated.")] string format,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new UpdateDeckMetadataChange(name, description, format),
            cancellationToken);
    }

    /// <summary>
    /// Deletes one revision-guarded deck.
    /// </summary>
    [McpServerTool(
        Name = "deck_delete",
        Title = "Delete Local Deck",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Deletes one local deck and dependent rows when expectedRevision is current.")]
    internal Task<OperationResult<DeckDeleteResult>> DeleteAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.DeleteAsync(deckId, expectedRevision, cancellationToken));
    }

    /// <summary>
    /// Adds one independently addressable entry.
    /// </summary>
    [McpServerTool(
        Name = "deck_entry_add",
        Title = "Add Local Deck Entry",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Adds one entry without merging cards that share a name or Oracle identity.")]
    internal Task<OperationResult<DeckDocument>> AddEntryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Complete draft for the new independently addressable entry.")] DeckEntryDraft entry,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new AddDeckEntryChange(entry),
            cancellationToken);
    }

    /// <summary>
    /// Replaces editable fields for one stable entry ID.
    /// </summary>
    [McpServerTool(
        Name = "deck_entry_update",
        Title = "Update Local Deck Entry",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Updates one local entry, including quantity, printing, zone, and order.")]
    internal Task<OperationResult<DeckDocument>> UpdateEntryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Complete replacement entry including its existing entryId.")] DeckEntry entry,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new UpdateDeckEntryChange(entry),
            cancellationToken);
    }

    /// <summary>
    /// Removes one stable entry and its category assignments.
    /// </summary>
    [McpServerTool(
        Name = "deck_entry_remove",
        Title = "Remove Local Deck Entry",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Removes one entry by stable ID without affecting other equivalent card rows.")]
    internal Task<OperationResult<DeckDocument>> RemoveEntryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Stable identifier of the entry to remove.")] Guid entryId,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new RemoveDeckEntryChange(entryId),
            cancellationToken);
    }

    /// <summary>
    /// Creates one functional category independently of zones.
    /// </summary>
    [McpServerTool(
        Name = "deck_category_create",
        Title = "Create Local Deck Category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates one functional category without changing entry inclusion or zones.")]
    internal Task<OperationResult<DeckDocument>> CreateCategoryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Complete draft for the new ordered functional category.")] DeckCategoryDraft category,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new AddDeckCategoryChange(category),
            cancellationToken);
    }

    /// <summary>
    /// Replaces editable fields for one category.
    /// </summary>
    [McpServerTool(
        Name = "deck_category_update",
        Title = "Update Local Deck Category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Updates one category name, color, and order without changing entry zones.")]
    internal Task<OperationResult<DeckDocument>> UpdateCategoryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Complete replacement category including its existing categoryId.")] DeckCategory category,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new UpdateDeckCategoryChange(category),
            cancellationToken);
    }

    /// <summary>
    /// Removes one category and its assignments without deleting entries.
    /// </summary>
    [McpServerTool(
        Name = "deck_category_delete",
        Title = "Delete Local Deck Category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Deletes one functional category while preserving every card row and zone.")]
    internal Task<OperationResult<DeckDocument>> DeleteCategoryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Stable identifier of the category to delete.")] Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new RemoveDeckCategoryChange(categoryId),
            cancellationToken);
    }

    /// <summary>
    /// Assigns one entry to one category with optional primary designation.
    /// </summary>
    [McpServerTool(
        Name = "deck_category_assign",
        Title = "Assign Local Deck Category",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates or updates one entry-category assignment with at most one primary per entry.")]
    internal Task<OperationResult<DeckDocument>> AssignCategoryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Stable identifier of the entry receiving the category.")] Guid entryId,
        [Description("Stable identifier of the category to assign.")] Guid categoryId,
        [Description("Whether this assignment becomes the entry's primary category.")] bool isPrimary = false,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new AssignDeckCategoryChange(entryId, categoryId, isPrimary),
            cancellationToken);
    }

    /// <summary>
    /// Removes one entry-category assignment without changing the category or entry.
    /// </summary>
    [McpServerTool(
        Name = "deck_category_unassign",
        Title = "Unassign Local Deck Category",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Removes one functional category assignment without changing deck zones.")]
    internal Task<OperationResult<DeckDocument>> UnassignCategoryAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Stable identifier of the entry losing the category.")] Guid entryId,
        [Description("Stable identifier of the category to unassign.")] Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        return ApplyOneAsync(
            deckId,
            expectedRevision,
            new UnassignDeckCategoryChange(entryId, categoryId),
            cancellationToken);
    }

    /// <summary>
    /// Creates one verified opaque backup.
    /// </summary>
    [McpServerTool(
        Name = "deck_backup_create",
        Title = "Create Local Deck Backup",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Creates one verified local decks.db snapshot and returns opaque metadata only.")]
    internal Task<OperationResult<DeckBackup>> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.Backups.CreateAsync(cancellationToken));
    }

    /// <summary>
    /// Restores one backup with a current-database fingerprint guard.
    /// </summary>
    [McpServerTool(
        Name = "deck_backup_restore",
        Title = "Restore Local Deck Backup",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Restores a verified backup only when the supplied current database fingerprint still matches.")]
    internal Task<OperationResult<DeckRestoreResult>> RestoreBackupAsync(
        [Description("Opaque backup UUID returned by deck_backup_list or deck_backup_create.")] Guid backupId,
        [Description("Current database fingerprint returned by deck_backup_list.")]
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
        Name = "deck_backup_delete",
        Title = "Delete Local Deck Backup",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Deletes one local deck backup by opaque ID without accepting a filesystem path.")]
    internal Task<OperationResult<DeckDeleteResult>> DeleteBackupAsync(
        [Description("Opaque backup UUID returned by deck_backup_list or deck_backup_create.")] Guid backupId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => store.Backups.DeleteAsync(backupId, cancellationToken));
    }

    /// <summary>
    /// Routes one granular change through the exact batch transaction service.
    /// </summary>
    private Task<OperationResult<DeckDocument>> ApplyOneAsync(
        Guid deckId,
        long expectedRevision,
        DeckChange change,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
            store.ApplyChangesAsync(deckId, expectedRevision, [change], cancellationToken));
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
