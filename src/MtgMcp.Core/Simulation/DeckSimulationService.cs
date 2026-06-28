namespace MtgMcp.Core;

/// <summary>
/// Runs deterministic goldfish simulations, board projections, and win-turn estimates.
/// </summary>
public sealed partial class DeckSimulationService : DeckServiceBase
{
    /// <summary>
    /// Imports reference Archidekt decks for goldfish comparison when configured.
    /// </summary>
    private readonly IArchidektGateway? archidektGateway;

    /// <summary>
    /// Resolves built-in, configured, and deck-local simulation profiles.
    /// </summary>
    private readonly SimulationProfileCatalog simulationProfiles;

    /// <summary>
    /// Creates a simulation service backed by workspace storage and card metadata.
    /// </summary>
    public DeckSimulationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        DateOnly? currentDateOverride = null,
        SimulationProfileCatalog? simulationProfiles = null
    )
        : base(
            repository,
            cardCatalog,
            planRepository,
            currentDateOverride)
    {
        this.archidektGateway = archidektGateway;
        this.simulationProfiles = simulationProfiles ?? SimulationProfileCatalog.CreateDefault();
    }
}
