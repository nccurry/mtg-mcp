namespace MtgMcp.Core;

/// <summary>
/// Manages deck workspaces, local mutations, Archidekt writeback, checkpoints, and intent metadata.
/// </summary>
public sealed partial class DeckWorkspaceService : DeckMutationServiceBase
{
    /// <summary>
    /// Creates a workspace service backed by the configured repositories and adapters.
    /// </summary>
    public DeckWorkspaceService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        DateOnly? currentDateOverride = null,
        IMoxfieldGateway? moxfieldGateway = null
    )
        : base(
            repository,
            cardCatalog,
            archidektGateway,
            moxfieldGateway,
            planRepository,
            currentDateOverride)
    {
    }
}
