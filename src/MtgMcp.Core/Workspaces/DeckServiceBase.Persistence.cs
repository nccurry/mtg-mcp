namespace MtgMcp.Core;

/// <summary>
/// Loads and persists workspaces for feature services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    protected async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace? workspace = await Repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    /// <summary>
    /// Loads a workspace and refreshes Archidekt-bound state before mutation.
    /// </summary>
    protected async Task<DeckWorkspace> LoadForMutationAsync(
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
        return await Repository.SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists local card edits and writes them back to Archidekt when enabled.
    /// </summary>
    protected async Task PersistCardsAsync(
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

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists a category locally and to Archidekt when enabled.
    /// </summary>
    protected async Task PersistCategoryAsync(
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

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a category in Archidekt when the workspace is writeback-enabled.
    /// </summary>
    protected async Task DeleteCategoryInAdapterAsync(
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

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a standard deck change response.
    /// </summary>
    protected static DeckChangeResult Change(
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
    /// Requires the Archidekt gateway for an operation that cannot run locally.
    /// </summary>
    protected IArchidektGateway RequireArchidektGateway()
    {
        return ArchidektGateway
            ?? throw new InvalidOperationException("Archidekt support is not configured.");
    }
}
