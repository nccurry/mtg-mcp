namespace MtgMcp.Core;

/// <summary>
/// Adds adapter writeback dependencies for services that mutate workspaces.
/// </summary>
public abstract partial class DeckMutationServiceBase : DeckServiceBase
{
    /// <summary>
    /// Applies Archidekt-specific read and writeback operations when a workspace is bound.
    /// </summary>
    private readonly IArchidektGateway? archidektGateway;

    /// <summary>
    /// Imports Moxfield decks into provider-neutral local workspaces.
    /// </summary>
    private readonly IMoxfieldGateway? moxfieldGateway;

    /// <summary>
    /// Captures writeback adapters for workspace mutation services.
    /// </summary>
    protected DeckMutationServiceBase(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        IMoxfieldGateway? moxfieldGateway = null,
        IDeckPlanRepository? planRepository = null,
        DateOnly? currentDateOverride = null)
        : base(repository, cardCatalog, planRepository, currentDateOverride)
    {
        this.archidektGateway = archidektGateway;
        this.moxfieldGateway = moxfieldGateway;
    }

    /// <summary>
    /// Requires the Archidekt gateway for an operation that cannot run locally.
    /// </summary>
    protected IArchidektGateway RequireArchidektGateway()
    {
        return DeckServiceHelpers.RequireArchidektGateway(archidektGateway);
    }

    /// <summary>
    /// Requires the Moxfield gateway for read-only import operations.
    /// </summary>
    protected IMoxfieldGateway RequireMoxfieldGateway()
    {
        return moxfieldGateway
            ?? throw new InvalidOperationException("Moxfield support is not configured.");
    }
}
