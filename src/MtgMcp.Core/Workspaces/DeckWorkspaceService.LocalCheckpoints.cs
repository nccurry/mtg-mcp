using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Manages local workspace checkpoints for non-writeback restore workflows.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Maximum number of local checkpoints retained for one workspace.
    /// </summary>
    private const int LocalCheckpointRetention = 10;

    /// <summary>
    /// Captures a local checkpoint for a workspace that can be restored without provider writeback.
    /// </summary>
    public async Task<WorkspaceCheckpointSummary> CreateWorkspaceCheckpointAsync(
        string workspaceId,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Checkpoint name is required.", nameof(name));
        }

        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        EnsureLocalCheckpointAllowed(workspace, "create");
        WorkspaceCheckpoint checkpoint = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkspaceId = workspace.Id,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Snapshot = CloneLocalCheckpointSnapshot(workspace)
        };

        workspace.LocalCheckpoints.Add(checkpoint);
        TrimLocalCheckpoints(workspace.LocalCheckpoints);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return ToLocalCheckpointSummary(checkpoint);
    }

    /// <summary>
    /// Lists local checkpoint metadata without returning saved workspace snapshots.
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceCheckpointSummary>> ListWorkspaceCheckpointsAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        List<WorkspaceCheckpointSummary> summaries = [];
        foreach (WorkspaceCheckpoint checkpoint in workspace.LocalCheckpoints)
        {
            summaries.Add(ToLocalCheckpointSummary(checkpoint));
        }

        summaries.Sort(static (left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        return summaries;
    }

    /// <summary>
    /// Gets one local checkpoint including its saved workspace snapshot.
    /// </summary>
    public async Task<WorkspaceCheckpoint> GetWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return FindLocalCheckpoint(workspace, checkpointId);
    }

    /// <summary>
    /// Restores a non-writeback workspace from a local checkpoint.
    /// </summary>
    public async Task<WorkspaceCheckpointRestoreResult> RestoreWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace current = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        EnsureLocalCheckpointAllowed(current, "restore");
        WorkspaceCheckpoint checkpoint = FindLocalCheckpoint(current, checkpointId);
        List<WorkspaceCheckpoint> existingCheckpoints = current.LocalCheckpoints;
        List<DeckImportHistoryEntry> existingImportHistory = current.ImportHistory;
        DeckWorkspace restored = CloneLocalCheckpointSnapshot(checkpoint.Snapshot);
        restored.Id = current.Id;
        restored.LocalCheckpoints = existingCheckpoints;
        restored.ImportHistory = existingImportHistory;
        restored.UpdatedAt = DateTimeOffset.UtcNow;
        DeckWorkspace saved = await Repository.SaveAsync(restored, cancellationToken)
            .ConfigureAwait(false);

        return new WorkspaceCheckpointRestoreResult
        {
            WorkspaceId = saved.Id,
            CheckpointId = checkpoint.Id,
            Status = "restored",
            Workspace = saved
        };
    }

    /// <summary>
    /// Deletes a local workspace checkpoint.
    /// </summary>
    public async Task DeleteWorkspaceCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        EnsureLocalCheckpointAllowed(workspace, "delete");
        WorkspaceCheckpoint checkpoint = FindLocalCheckpoint(workspace, checkpointId);
        workspace.LocalCheckpoints.Remove(checkpoint);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects local checkpoint mutations that would bypass Archidekt writeback safety.
    /// </summary>
    private static void EnsureLocalCheckpointAllowed(DeckWorkspace workspace, string operation)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            throw new InvalidOperationException(
                $"Local workspace checkpoint {operation} is not available for Archidekt writeback workspaces. "
                    + "Use archidekt_checkpoint_* tools instead.");
        }
    }

    /// <summary>
    /// Finds a checkpoint by id or reports a clear missing-checkpoint error.
    /// </summary>
    private static WorkspaceCheckpoint FindLocalCheckpoint(DeckWorkspace workspace, string checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            throw new ArgumentException("Checkpoint id is required.", nameof(checkpointId));
        }

        foreach (WorkspaceCheckpoint checkpoint in workspace.LocalCheckpoints)
        {
            if (checkpoint.Id.Equals(checkpointId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return checkpoint;
            }
        }

        throw new InvalidOperationException(
            $"Workspace checkpoint '{checkpointId}' was not found for workspace '{workspace.Id}'.");
    }

    /// <summary>
    /// Keeps only the newest local checkpoints for the workspace.
    /// </summary>
    private static void TrimLocalCheckpoints(List<WorkspaceCheckpoint> checkpoints)
    {
        checkpoints.Sort(static (left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        if (checkpoints.Count <= LocalCheckpointRetention)
        {
            return;
        }

        checkpoints.RemoveRange(LocalCheckpointRetention, checkpoints.Count - LocalCheckpointRetention);
    }

    /// <summary>
    /// Clones a workspace for checkpoint storage while removing recursive history payloads.
    /// </summary>
    private static DeckWorkspace CloneLocalCheckpointSnapshot(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        DeckWorkspace clone = JsonSerializer.Deserialize<DeckWorkspace>(json) ?? new DeckWorkspace();
        clone.ImportHistory = [];
        clone.LocalCheckpoints = [];
        return clone;
    }

    /// <summary>
    /// Creates compact checkpoint metadata for list and create responses.
    /// </summary>
    private static WorkspaceCheckpointSummary ToLocalCheckpointSummary(WorkspaceCheckpoint checkpoint)
    {
        return new WorkspaceCheckpointSummary
        {
            Id = checkpoint.Id,
            WorkspaceId = checkpoint.WorkspaceId,
            Name = checkpoint.Name,
            Description = checkpoint.Description,
            CreatedAt = checkpoint.CreatedAt
        };
    }
}
