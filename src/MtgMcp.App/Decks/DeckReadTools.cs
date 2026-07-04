using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes deterministic local deck reads in every operation mode.
/// </summary>
internal sealed class DeckReadTools
{
    /// <summary>
    /// Stores the local deck boundary used by every read tool.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Creates read tools around one process-local deck store.
    /// </summary>
    internal DeckReadTools(SqliteDeckStore store)
    {
        this.store = store;
    }

    /// <summary>
    /// Lists one canonical page of local decks.
    /// </summary>
    [McpServerTool(
        Name = "deck_list",
        Title = "List Local Decks",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists canonically ordered local deck summaries using an opaque stable cursor.")]
    internal Task<OperationResult<DeckPage>> ListAsync(
        [Description("Opaque cursor returned by a prior deck_list call.")] string? cursor = null,
        [Description("Number of summaries to return, from 1 through 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return store.ListAsync(cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Gets one complete canonical local deck by stable ID.
    /// </summary>
    [McpServerTool(
        Name = "deck_get",
        Title = "Get Local Deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Gets one local deck with entries, zones, categories, assignments, and provider-neutral bindings.")]
    internal Task<OperationResult<DeckDocument>> GetAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        CancellationToken cancellationToken = default)
    {
        return store.GetAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Reports only local structural invariants for one deck.
    /// </summary>
    [McpServerTool(
        Name = "deck_validate",
        Title = "Validate Local Deck Structure",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Checks local references, quantities, zones, primary categories, and Commander fixture " +
        "structure without legality or quality judgments.")]
    internal Task<OperationResult<DeckValidationReport>> ValidateAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        CancellationToken cancellationToken = default)
    {
        return store.ValidateAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Lists opaque local deck backups and the current guarded database fingerprint.
    /// </summary>
    [McpServerTool(
        Name = "deck_backup_list",
        Title = "List Local Deck Backups",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists opaque deck backup metadata without exposing local filesystem paths.")]
    internal Task<OperationResult<DeckBackupPage>> ListBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        return store.Backups.ListAsync(cancellationToken);
    }
}
