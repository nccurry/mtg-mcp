namespace MtgMcp.Core;

/// <summary>
/// Analyzes deck composition, card snapshots, draw odds, costs, mana, brackets, combos, and heuristic health.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Creates an analysis service backed by workspace storage and card-data providers.
    /// </summary>
    public DeckAnalysisService(
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
