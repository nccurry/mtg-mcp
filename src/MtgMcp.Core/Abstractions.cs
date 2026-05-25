namespace MtgMcp.Core;

/// <summary>
/// Defines operations for card catalog.
/// </summary>
public interface ICardCatalog
{
    /// <summary>
    /// Searches the cards.
    /// </summary>
    Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Searches cards from a provider-neutral deckbuilding request.
    /// </summary>
    Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        CardSearchRequest request,
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Looks up a single card by Scryfall id or fuzzy name.
    /// </summary>
    Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets cards by names.
    /// </summary>
    Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Looks up Scryfall rulings for a card.
    /// </summary>
    Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
        string nameOrId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Looks up known Scryfall prints for a card.
    /// </summary>
    Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
        string nameOrId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Suggests the cards.
    /// </summary>
    Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
        string prompt,
        string? format,
        int limit,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines Commander metagame lookup behavior.
/// </summary>
public interface ICommanderMetaProvider
{
    /// <summary>
    /// Gets Commander and theme popularity data from an optional external source.
    /// </summary>
    Task<CommanderMetaReport> GetCommanderMetaAsync(
        CommanderMetaQuery query,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines recent-card lookup behavior.
/// </summary>
public interface ICardTrendProvider
{
    /// <summary>
    /// Finds recently released cards that match a deck theme or format.
    /// </summary>
    Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
        CardTrendQuery query,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines combo catalog lookup behavior.
/// </summary>
public interface IComboCatalog
{
    /// <summary>
    /// Finds combos and near misses for a deck card pool.
    /// </summary>
    Task<DeckComboReport> FindCombosAsync(
        ComboCatalogQuery query,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines normalized card-signal lookup behavior for deck corpus sources.
/// </summary>
public interface ICorpusSignalProvider
{
    /// <summary>
    /// Gets source capability and attribution status.
    /// </summary>
    CorpusSourceStatus GetStatus();

    /// <summary>
    /// Gets normalized corpus signals for a deck context.
    /// </summary>
    Task<CorpusSignalReport> GetSignalsAsync(
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines source-fact caching for corpus API providers.
/// </summary>
public interface ICorpusCache
{
    /// <summary>
    /// Gets a cached source fact when it is still fresh.
    /// </summary>
    Task<T?> GetAsync<T>(
        CorpusCacheKey key,
        TimeSpan timeToLive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a source fact for reuse by later corpus lookups.
    /// </summary>
    Task SetAsync<T>(
        CorpusCacheKey key,
        T value,
        CancellationToken cancellationToken);
}

/// <summary>
/// Defines operations for deck workspace repository.
/// </summary>
public interface IDeckWorkspaceRepository
{
    /// <summary>
    /// Saves the workspace.
    /// </summary>
    Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a workspace by id.
    /// </summary>
    Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the cancellation token.
    /// </summary>
    Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines operations for deck edit plan repository.
/// </summary>
public interface IDeckPlanRepository
{
    /// <summary>
    /// Saves the plan.
    /// </summary>
    Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a persisted deck edit plan by id.
    /// </summary>
    Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the plans.
    /// </summary>
    Task<IReadOnlyList<DeckEditPlan>> ListAsync(
        string? workspaceId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes the plan and reports whether one existed.
    /// </summary>
    Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken);
}

/// <summary>
/// Defines operations for archidekt gateway.
/// </summary>
public interface IArchidektGateway
{
    /// <summary>
    /// Returns redacted Archidekt credential availability.
    /// </summary>
    Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the decks.
    /// </summary>
    Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new Archidekt deck and returns the imported writeback workspace.
    /// </summary>
    Task<DeckWorkspace> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Imports the deck.
    /// </summary>
    Task<DeckWorkspace> ImportDeckAsync(
        string deckIdOrUrl,
        bool writeBack,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Persists the cards.
    /// </summary>
    Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Persists the category.
    /// </summary>
    Task PersistCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes the category.
    /// </summary>
    Task DeleteCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Persists the metadata.
    /// </summary>
    Task PersistMetadataAsync(DeckWorkspace workspace, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the checkpoint.
    /// </summary>
    Task<DeckCheckpoint> CreateCheckpointAsync(
        DeckWorkspace workspace,
        string name,
        string? description,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the checkpoints.
    /// </summary>
    Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Loads one Archidekt checkpoint for a workspace.
    /// </summary>
    Task<DeckCheckpoint> GetCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Renames the checkpoint.
    /// </summary>
    Task<DeckCheckpoint> RenameCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        string name,
        string? description,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes the checkpoint.
    /// </summary>
    Task DeleteCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines read-only Moxfield deck import behavior.
/// </summary>
public interface IMoxfieldGateway
{
    /// <summary>
    /// Imports a public or unlisted Moxfield deck into a provider-neutral local workspace.
    /// </summary>
    Task<DeckWorkspace> ImportDeckAsync(
        string deckIdOrUrl,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Defines Playgroup.gg lookup operations.
/// </summary>
public interface IPlaygroupGateway
{
    /// <summary>
    /// Gets redacted Playgroup authentication status.
    /// </summary>
    Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the user associated with the configured API key.
    /// </summary>
    Task<PlaygroupUser> GetCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a playgroup visible to the specified user.
    /// </summary>
    Task<PlaygroupSummary> GetUserPlaygroupAsync(
        long userId,
        long playgroupId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists games recorded in a playgroup.
    /// </summary>
    Task<IReadOnlyList<PlaygroupGame>> ListPlaygroupGamesAsync(
        long playgroupId,
        int page,
        int limit,
        bool includeEvents,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets Playgroup deck details when the deck is accessible.
    /// </summary>
    Task<PlaygroupDeck> GetDeckAsync(long deckId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists accessible decks for a Playgroup user.
    /// </summary>
    Task<IReadOnlyList<PlaygroupDeck>> ListUserDecksAsync(
        long userId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets a deck's Elo history in a global, playgroup, or league scope.
    /// </summary>
    Task<PlaygroupEloHistory> GetDeckEloHistoryAsync(
        long deckId,
        long? playgroupId,
        long? leagueId,
        CancellationToken cancellationToken
    );
}
