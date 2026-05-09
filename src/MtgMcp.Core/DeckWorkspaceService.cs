namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Stores the repository.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Stores the plan repository.
    /// </summary>
    private readonly IDeckPlanRepository? planRepository;

    /// <summary>
    /// Stores the card catalog.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores the archidekt gateway.
    /// </summary>
    private readonly IArchidektGateway? archidektGateway;

    /// <summary>
    /// Handles deck workspace service.
    /// </summary>
    public DeckWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null
    )
    {
        this.repository = repository;
        this.planRepository = planRepository;
        this.cardCatalog = cardCatalog;
        this.archidektGateway = archidektGateway;
    }
}
