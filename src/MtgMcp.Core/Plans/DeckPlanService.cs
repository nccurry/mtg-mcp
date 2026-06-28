namespace MtgMcp.Core;

/// <summary>
/// Persists, previews, and applies generated deck edit plans.
/// </summary>
public sealed partial class DeckPlanService : DeckMutationServiceBase
{
    /// <summary>
    /// Applies plan operations through the same workspace mutation path used by MCP tools.
    /// </summary>
    private readonly DeckWorkspaceService workspaces;

    /// <summary>
    /// Builds before/after metric snapshots for plan previews.
    /// </summary>
    private readonly DeckAnalysisMetrics analysisMetrics;

    /// <summary>
    /// Creates a plan service backed by an explicit workspace mutation collaborator.
    /// </summary>
    public DeckPlanService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        DeckWorkspaceService workspaces,
        IArchidektGateway? archidektGateway = null,
        IDeckPlanRepository? planRepository = null,
        DateOnly? currentDateOverride = null,
        DeckAnalysisMetrics? analysisMetrics = null
    )
        : base(
            repository,
            cardCatalog,
            archidektGateway: archidektGateway,
            moxfieldGateway: null,
            planRepository: planRepository,
            currentDateOverride: currentDateOverride)
    {
        this.workspaces = workspaces;
        this.analysisMetrics = analysisMetrics ?? new DeckAnalysisMetrics(cardCatalog, CurrentDate);
    }
}
