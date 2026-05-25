namespace MtgMcp.Core;

/// <summary>
/// Shares core workspace storage, card catalog, plan storage, and date helpers across feature services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Persists local and cached Archidekt workspaces.
    /// </summary>
    protected IDeckWorkspaceRepository Repository { get; }

    /// <summary>
    /// Persists generated deck edit plans when planning tools are enabled.
    /// </summary>
    protected IDeckPlanRepository? PlanRepository { get; }

    /// <summary>
    /// Resolves cards, searches, prints, and rulings through the configured catalog adapter.
    /// </summary>
    protected ICardCatalog CardCatalog { get; }

    /// <summary>
    /// Overrides today's date for deterministic release-radar tests.
    /// </summary>
    protected DateOnly? CurrentDateOverride { get; }

    /// <summary>
    /// Captures dependencies shared by workspace, analysis, recommendation, plan, and simulation services.
    /// </summary>
    protected DeckServiceBase(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IDeckPlanRepository? planRepository = null,
        DateOnly? currentDateOverride = null)
    {
        Repository = repository;
        PlanRepository = planRepository;
        CardCatalog = cardCatalog;
        CurrentDateOverride = currentDateOverride;
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    protected async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await Repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
