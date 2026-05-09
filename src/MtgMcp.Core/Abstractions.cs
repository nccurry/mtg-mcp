namespace MtgMcp.Core;

public interface ICardCatalog
{
    Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(string query, int limit, CancellationToken cancellationToken);

    Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(string prompt, string? format, int limit, CancellationToken cancellationToken);
}

public interface IDeckWorkspaceRepository
{
    Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken);

    Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken);
}

public interface IDeckPlanRepository
{
    Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken);

    Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeckEditPlan>> ListAsync(string? workspaceId, CancellationToken cancellationToken);

    Task DeleteAsync(string planId, CancellationToken cancellationToken);
}

public interface IArchidektGateway
{
    Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken);

    Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken);

    Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken);

    Task PersistCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken);

    Task DeleteCategoryAsync(DeckWorkspace workspace, DeckCategory category, CancellationToken cancellationToken);

    Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken);

    Task<DeckCheckpoint> CreateCheckpointAsync(
        DeckWorkspace workspace,
        string name,
        string? description,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(DeckWorkspace workspace, CancellationToken cancellationToken);

    Task<DeckCheckpoint> GetCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken);

    Task<DeckCheckpoint> RenameCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        string name,
        string? description,
        CancellationToken cancellationToken);

    Task DeleteCheckpointAsync(DeckWorkspace workspace, string checkpointId, CancellationToken cancellationToken);
}
