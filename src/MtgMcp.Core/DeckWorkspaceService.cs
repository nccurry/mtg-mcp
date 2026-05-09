namespace MtgMcp.Core;

public sealed partial class DeckWorkspaceService
{
    private readonly IDeckWorkspaceRepository repository;
    private readonly ICardCatalog cardCatalog;
    private readonly IArchidektGateway? archidektGateway;

    public DeckWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.archidektGateway = archidektGateway;
    }
}
