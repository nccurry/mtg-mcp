namespace MtgMcp.Core;

/// <summary>
/// Creates deck improvement plans and card recommendations from catalog, trend, and meta signals.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Provides analysis workflows used by combined recommendation reports.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Provides simulation workflows used by combined recommendation reports.
    /// </summary>
    private readonly DeckSimulationService simulation;

    /// <summary>
    /// Creates a recommendation service backed by explicit analysis and simulation collaborators.
    /// </summary>
    public DeckRecommendationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        DeckAnalysisService analysis,
        DeckSimulationService simulation,
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
        this.analysis = analysis;
        this.simulation = simulation;
    }
}
