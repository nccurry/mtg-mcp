namespace MtgMcp.Core;

/// <summary>
/// Runs deterministic goldfish simulations, board projections, and win-turn estimates.
/// </summary>
public sealed partial class DeckSimulationService : DeckServiceBase
{
    /// <summary>
    /// Creates a simulation service backed by workspace storage and card metadata.
    /// </summary>
    public DeckSimulationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
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
    }
}
