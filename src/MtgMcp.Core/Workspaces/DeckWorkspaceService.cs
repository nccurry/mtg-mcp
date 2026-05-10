namespace MtgMcp.Core;

/// <summary>
/// Manages deck workspaces, local mutations, Archidekt writeback, checkpoints, and intent metadata.
/// </summary>
public sealed partial class DeckWorkspaceService : DeckServiceBase
{
    /// <summary>
    /// Creates a workspace service backed by the configured repositories and adapters.
    /// </summary>
    public DeckWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null
    )
        : base(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride)
    {
    }
}
