namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Loads the workspace.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    /// <summary>
    /// Loads the for mutation.
    /// </summary>
    private async Task<DeckWorkspace> LoadForMutationAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        if (workspace.Mode != WorkspaceMode.Archidekt || !workspace.WriteBack)
        {
            return workspace;
        }

        if (string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            throw new InvalidOperationException(
                "Archidekt workspace is missing an Archidekt deck id."
            );
        }

        // Writeback workspaces are refreshed before each mutation so local edits
        // apply on top of Archidekt's latest deck state instead of stale cache.
        DeckWorkspace fresh = await RequireArchidektGateway()
            .ImportDeckAsync(workspace.ArchidektDeckId, writeBack: true, cancellationToken)
            .ConfigureAwait(false);

        fresh.Id = workspace.Id;
        fresh.WriteBack = true;
        return await repository.SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the cards.
    /// </summary>
    private async Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken
    )
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistCardsAsync(workspace, upsertedCards, removedCards, cancellationToken)
                .ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the category.
    /// </summary>
    private async Task PersistCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistCategoryAsync(workspace, category, cancellationToken)
                .ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the category in adapter.
    /// </summary>
    private async Task DeleteCategoryInAdapterAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .DeleteCategoryAsync(workspace, category, cancellationToken)
                .ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles change.
    /// </summary>
    private static DeckChangeResult Change(
        DeckWorkspace workspace,
        DeckMutationKind kind,
        string message
    )
    {
        return new DeckChangeResult
        {
            WorkspaceId = workspace.Id,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace),
            Message = message,
            Workspace = workspace,
        };
    }

    /// <summary>
    /// Handles require archidekt gateway.
    /// </summary>
    private IArchidektGateway RequireArchidektGateway()
    {
        return archidektGateway
            ?? throw new InvalidOperationException("Archidekt support is not configured.");
    }
}
