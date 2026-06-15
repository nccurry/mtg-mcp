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
    /// Captures a local workspace checkpoint for non-writeback restore.
    /// </summary>
    [McpServerTool(
        Name = "workspace_checkpoint_create",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description("Create a local checkpoint for a non-writeback workspace. Archidekt writeback workspaces should use archidekt_checkpoint_create.")]
    public Task<WorkspaceCheckpointSummary> CreateWorkspaceCheckpointAsync(
        string workspaceId,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("workspace_checkpoint_create");
        return decks.CreateWorkspaceCheckpointAsync(workspaceId, name, description, cancellationToken);
    }

    /// <summary>
    /// Lists local workspace checkpoints without returning snapshot payloads.
    /// </summary>
    [McpServerTool(
        Name = "workspace_checkpoint_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("List local checkpoints for a workspace without returning saved workspace snapshots.")]
    public Task<IReadOnlyList<WorkspaceCheckpointSummary>> ListWorkspaceCheckpointsAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.ListWorkspaceCheckpointsAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets one local workspace checkpoint and its saved snapshot.
    /// </summary>
    [McpServerTool(
        Name = "workspace_checkpoint_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Get one local workspace checkpoint, including its saved workspace snapshot.")]
    public Task<WorkspaceCheckpoint> GetWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        return decks.GetWorkspaceCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }

    /// <summary>
    /// Restores a local workspace checkpoint.
    /// </summary>
    [McpServerTool(
        Name = "workspace_checkpoint_restore",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description("Restore a non-writeback workspace from a local checkpoint. Archidekt writeback workspaces are refused.")]
    public Task<WorkspaceCheckpointRestoreResult> RestoreWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("workspace_checkpoint_restore");
        return decks.RestoreWorkspaceCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }

    /// <summary>
    /// Deletes a local workspace checkpoint.
    /// </summary>
    [McpServerTool(
        Name = "workspace_checkpoint_delete",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Delete a local workspace checkpoint.")]
    public Task DeleteWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("workspace_checkpoint_delete");
        return decks.DeleteWorkspaceCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }

    /// <summary>
    /// Creates an Archidekt checkpoint for a bound workspace.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_checkpoint_create",
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
        operationMode.EnsureCanMutate("archidekt_checkpoint_create");
        return decks.CheckpointDeckAsync(workspaceId, name, description, cancellationToken);
    }

    /// <summary>
    /// Lists the deck checkpoints.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_checkpoint_list",
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
        Name = "archidekt_checkpoint_get",
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
        Name = "archidekt_checkpoint_rename",
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
        operationMode.EnsureCanMutate("archidekt_checkpoint_rename");
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
        Name = "archidekt_checkpoint_delete",
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
        operationMode.EnsureCanMutate("archidekt_checkpoint_delete");
        return decks.DeleteDeckCheckpointAsync(workspaceId, checkpointId, cancellationToken);
    }
}
