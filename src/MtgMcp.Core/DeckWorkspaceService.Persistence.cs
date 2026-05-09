namespace MtgMcp.Core;

public sealed partial class DeckWorkspaceService
{
    private async Task<DeckWorkspace> LoadWorkspaceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository.GetAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return workspace ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    private async Task<DeckWorkspace> LoadForMutationAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (workspace.Mode != WorkspaceMode.Archidekt || !workspace.WriteBack)
        {
            return workspace;
        }

        if (string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            throw new InvalidOperationException("Archidekt workspace is missing an Archidekt deck id.");
        }

        DeckWorkspace fresh = await RequireArchidektGateway()
            .ImportDeckAsync(workspace.ArchidektDeckId, writeBack: true, cancellationToken)
            .ConfigureAwait(false);

        fresh.Id = workspace.Id;
        fresh.WriteBack = true;
        return await repository.SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway()
                .PersistCardsAsync(workspace, upsertedCards, removedCards, cancellationToken)
                .ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway().PersistCategoryAsync(workspace, category, cancellationToken).ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteCategoryInAdapterAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway().DeleteCategoryAsync(workspace, category, cancellationToken).ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    private static DeckChangeResult Change(DeckWorkspace workspace, DeckMutationKind kind, string message)
    {
        return new DeckChangeResult
        {
            WorkspaceId = workspace.Id,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace),
            Message = message,
            Workspace = workspace
        };
    }

    private IArchidektGateway RequireArchidektGateway()
    {
        return archidektGateway ?? throw new InvalidOperationException("Archidekt support is not configured.");
    }
}
