namespace MtgMcp.Core;

public sealed partial class DeckWorkspaceService
{
    private readonly IDeckWorkspaceRepository repository;
    private readonly IDeckPlanRepository? planRepository;
    private readonly ICardCatalog cardCatalog;
    private readonly IArchidektGateway? archidektGateway;

    public DeckWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null)
    {
        this.repository = repository;
        this.planRepository = planRepository;
        this.cardCatalog = cardCatalog;
        this.archidektGateway = archidektGateway;
    }
}
