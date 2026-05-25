namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Creates an Archidekt checkpoint for a workspace.
    /// </summary>
    public async Task<DeckCheckpoint> CheckpointDeckAsync(
        string workspaceId,
        string name,
        string? description,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return await RequireArchidektGateway()
            .CreateCheckpointAsync(workspace, name, description, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the deck checkpoints.
    /// </summary>
    public async Task<IReadOnlyList<DeckCheckpoint>> ListDeckCheckpointsAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return await RequireArchidektGateway()
            .ListCheckpointsAsync(workspace, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches one Archidekt checkpoint for a workspace.
    /// </summary>
    public async Task<DeckCheckpoint> GetDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return await RequireArchidektGateway()
            .GetCheckpointAsync(workspace, checkpointId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Renames the deck checkpoint.
    /// </summary>
    public async Task<DeckCheckpoint> RenameDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        string name,
        string? description,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return await RequireArchidektGateway()
            .RenameCheckpointAsync(workspace, checkpointId, name, description, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the deck checkpoint.
    /// </summary>
    public async Task DeleteDeckCheckpointAsync(
        string workspaceId,
        string checkpointId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        await RequireArchidektGateway()
            .DeleteCheckpointAsync(workspace, checkpointId, cancellationToken)
            .ConfigureAwait(false);
    }
}
