using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for checkpoint.
/// </summary>
[McpServerToolType]
public sealed class CheckpointTools
{
    /// <summary>
    /// Stores the decks.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates the MCP tools that manage Archidekt checkpoints.
    /// </summary>
    public CheckpointTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Creates an Archidekt checkpoint for a bound workspace.
    /// </summary>
    [McpServerTool(
        Name = "checkpoint_deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Create an Archidekt deck checkpoint/snapshot.")]
    public Task<DeckCheckpoint> CheckpointDeckAsync(
        string workspaceId,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("checkpoint_deck");
        return decks.CheckpointDeckAsync(workspaceId, name, description, cancellationToken);
    }

    /// <summary>
    /// Lists the deck checkpoints.
    /// </summary>
    [McpServerTool(
        Name = "list_deck_checkpoints",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("List Archidekt checkpoints for a deck workspace.")]
    public Task<IReadOnlyList<DeckCheckpoint>> ListDeckCheckpointsAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.ListDeckCheckpointsAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets the deck checkpoint.
    /// </summary>
    [McpServerTool(
        Name = "get_deck_checkpoint",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Fetch one Archidekt deck checkpoint.")]
    public Task<DeckCheckpoint> GetDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.GetDeckCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }

    /// <summary>
    /// Renames the deck checkpoint.
    /// </summary>
    [McpServerTool(
        Name = "rename_deck_checkpoint",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Rename or update the description for an Archidekt checkpoint.")]
    public Task<DeckCheckpoint> RenameDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("rename_deck_checkpoint");
        return decks.RenameDeckCheckpointAsync(
            workspaceId,
            checkpointId,
            name,
            description,
            cancellationToken
        );
    }

    /// <summary>
    /// Deletes the deck checkpoint.
    /// </summary>
    [McpServerTool(
        Name = "delete_deck_checkpoint",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Delete an Archidekt deck checkpoint.")]
    public Task DeleteDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("delete_deck_checkpoint");
        return decks.DeleteDeckCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }
}
