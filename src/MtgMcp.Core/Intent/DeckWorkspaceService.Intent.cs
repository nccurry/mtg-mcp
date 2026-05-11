namespace MtgMcp.Core;

/// <summary>
/// Provides deck intent workspace behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Reads deck intent from the workspace description.
    /// </summary>
    public async Task<DeckIntentResult> GetDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return DeckIntentText.Extract(workspace.Description, workspace.Id);
    }

    /// <summary>
    /// Writes deck intent to the workspace description.
    /// </summary>
    public async Task<DeckIntentChangeResult> SetDeckIntentAsync(
        string workspaceId,
        string intentText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(intentText))
        {
            throw new ArgumentException("Intent text is required.", nameof(intentText));
        }

        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        workspace.Description = DeckIntentText.UpsertDescription(workspace.Description, intentText);
        await PersistIntentMetadataAsync(workspace, cancellationToken).ConfigureAwait(false);

        DeckIntentResult result = DeckIntentText.Extract(workspace.Description, workspace.Id);
        return new DeckIntentChangeResult
        {
            Workspace = workspace,
            Intent = result,
            Persistence = DeckPersistence.For(workspace),
            Message = "Updated deck intent."
        };
    }

    /// <summary>
    /// Removes deck intent from the workspace description.
    /// </summary>
    public async Task<DeckIntentChangeResult> ClearDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        workspace.Description = DeckIntentText.ClearDescription(workspace.Description);
        await PersistIntentMetadataAsync(workspace, cancellationToken).ConfigureAwait(false);

        DeckIntentResult result = DeckIntentText.Extract(workspace.Description, workspace.Id);
        return new DeckIntentChangeResult
        {
            Workspace = workspace,
            Intent = result,
            Persistence = DeckPersistence.For(workspace),
            Message = "Cleared deck intent."
        };
    }

    /// <summary>
    /// Suggests deck intent from the current workspace.
    /// </summary>
    public async Task<DeckIntentResult> SuggestDeckIntentAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent intent = DeckIntentText.Suggest(workspace);
        string intentText = DeckIntentText.Format(intent);
        return new DeckIntentResult
        {
            WorkspaceId = workspace.Id,
            Found = true,
            Intent = intent,
            IntentText = intentText,
            Source = "suggested"
        };
    }

    /// <summary>
    /// Persists intent metadata through the correct backing store.
    /// </summary>
    private async Task PersistIntentMetadataAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistMetadataAsync(workspace, cancellationToken)
                .ConfigureAwait(false);
        }

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }
}
