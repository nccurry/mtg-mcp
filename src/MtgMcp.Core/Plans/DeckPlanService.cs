namespace MtgMcp.Core;

/// <summary>
/// Persists, previews, and applies generated deck edit plans.
/// </summary>
public sealed partial class DeckPlanService : DeckServiceBase
{
    /// <summary>
    /// Applies plan operations through the same workspace mutation path used by MCP tools.
    /// </summary>
    private readonly DeckWorkspaceService workspaces;

    /// <summary>
    /// Creates a plan service backed by an explicit workspace mutation collaborator.
    /// </summary>
    public DeckPlanService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        DeckWorkspaceService workspaces,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null
    )
        : base(
            repository,
            cardCatalog,
            archidektGateway,
            planRepository,
            commanderMetaProvider,
            cardTrendProvider,
            comboCatalog,
            currentDateOverride,
            corpusSignalProviders)
    {
        this.workspaces = workspaces;
    }
}
