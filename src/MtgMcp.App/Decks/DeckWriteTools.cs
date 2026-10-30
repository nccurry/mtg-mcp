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
    /// Stores the lookup that fills in the card identity of new entries.
    /// </summary>
    private readonly DeckEntryResolver resolver;

    /// <summary>
    /// Creates write tools around one store, validated operation mode, and the entry resolver.
    /// </summary>
    internal DeckWriteTools(SqliteDeckStore store, OperationMode mode, DeckEntryResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.store = store;
        this.mode = mode;
        this.resolver = resolver;
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
        "without provider calls, legality inference, or entry coalescing. An entry that has only a card name " +
        "(or a set code and collector number) gets its card identity from installed card data; a name that is " +
        "not found is still added and listed in issues. The result is a summary (deck ID, revision, counts, " +
        "entry IDs, issues); pass returnDeck true to also get the whole deck.")]
    internal Task<OperationResult<DeckWriteResult>> CreateAsync(
        [Description("Complete caller-supplied initial local deck graph.")] DeckCreateRequest request,
        [Description("Set true to also return the whole deck after the write. The default summary is much smaller.")]
        bool returnDeck = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => CreateResolvedAsync(request, returnDeck, cancellationToken));
    }

    /// <summary>
    /// Looks up the identity of the request's entries, creates the deck, and summarizes it.
    /// </summary>
    private async Task<OperationResult<DeckWriteResult>> CreateResolvedAsync(
        DeckCreateRequest request,
        bool returnDeck,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DeckWriteIssue> issues = [];
        if (request is { Entries: { Count: > 0 } entries })
        {
            DeckDraftResolution resolution = await resolver.ResolveAsync(entries, cancellationToken)
                .ConfigureAwait(false);
            request = request with { Entries = resolution.Drafts };
            issues = resolution.Issues;
        }

        OperationResult<DeckDocument> created = await store.CreateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return created is OperationSuccess<DeckDocument> success
            ? new OperationSuccess<DeckWriteResult>(DeckWriteResults.Created(success.Data, issues, returnDeck))
            : OperationFailure.Forward<DeckDocument, DeckWriteResult>(created);
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
