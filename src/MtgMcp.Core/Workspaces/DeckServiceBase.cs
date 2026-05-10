namespace MtgMcp.Core;

/// <summary>
/// Shares deck service dependencies and cross-cutting helpers across feature services.
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
    /// Applies Archidekt-specific read and writeback operations when a workspace is bound.
    /// </summary>
    protected IArchidektGateway? ArchidektGateway { get; }

    /// <summary>
    /// Supplies optional Commander metagame context for recommendation workflows.
    /// </summary>
    protected ICommanderMetaProvider? CommanderMetaProvider { get; }

    /// <summary>
    /// Supplies optional recent-card recommendations beyond direct catalog search.
    /// </summary>
    protected ICardTrendProvider? CardTrendProvider { get; }

    /// <summary>
    /// Supplies optional combo lookups before falling back to local heuristics.
    /// </summary>
    protected IComboCatalog? ComboCatalog { get; }

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
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        ICommanderMetaProvider? commanderMetaProvider = null,
        ICardTrendProvider? cardTrendProvider = null,
        IComboCatalog? comboCatalog = null,
        DateOnly? currentDateOverride = null
    )
    {
        Repository = repository;
        PlanRepository = planRepository;
        CardCatalog = cardCatalog;
        ArchidektGateway = archidektGateway;
        CommanderMetaProvider = commanderMetaProvider;
        CardTrendProvider = cardTrendProvider;
        ComboCatalog = comboCatalog;
        CurrentDateOverride = currentDateOverride;
    }
}
