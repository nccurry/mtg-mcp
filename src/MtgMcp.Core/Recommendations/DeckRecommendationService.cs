namespace MtgMcp.Core;

/// <summary>
/// Creates deck improvement plans and card recommendations from catalog, trend, and meta signals.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Imports Archidekt decks when recommendation scoring compares against Playgroup meta.
    /// </summary>
    private readonly IArchidektGateway? archidektGateway;

    /// <summary>
    /// Supplies Commander metagame context when a provider is configured.
    /// </summary>
    private readonly ICommanderMetaProvider? commanderMetaProvider;

    /// <summary>
    /// Supplies recent-card suggestions beyond direct catalog searches.
    /// </summary>
    private readonly ICardTrendProvider? cardTrendProvider;

    /// <summary>
    /// Supplies corpus-backed card evidence, exemplar decks, and discussions.
    /// </summary>
    private readonly IReadOnlyList<ICorpusSignalProvider> corpusSignalProviders;

    /// <summary>
    /// Provides analysis workflows used by combined recommendation reports.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Builds read-only tuning reports across several workspaces.
    /// </summary>
    private readonly DeckBatchTuningService batchTuning;

    /// <summary>
    /// Runs deck-aware catalog query workflows for direct query and goal-package recommendations.
    /// </summary>
    private readonly DeckQueryService queries;

    /// <summary>
    /// Builds goal-driven card package plans.
    /// </summary>
    private readonly DeckGoalPackageService goalPackages;

    /// <summary>
    /// Builds replacement, upgrade, and consistency improvement plans.
    /// </summary>
    private readonly DeckReplacementService replacements;

    /// <summary>
    /// Builds deck category cleanup plans.
    /// </summary>
    private readonly DeckCategorySuggestionService categories;

    /// <summary>
    /// Builds reusable analysis metrics used by recommendation scoring heuristics.
    /// </summary>
    private readonly DeckAnalysisMetrics analysisMetrics;

    /// <summary>
    /// Provides simulation workflows used by combined recommendation reports.
    /// </summary>
    private readonly DeckSimulationService simulation;

    /// <summary>
    /// Resolves simulation profiles for recommendation-side scoring workflows.
    /// </summary>
    private readonly SimulationProfileCatalog simulationProfiles;

    /// <summary>
    /// Supplies Playgroup-derived local meta context when configured.
    /// </summary>
    private readonly PlaygroupService? playgroups;

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
        DateOnly? currentDateOverride = null,
        IEnumerable<ICorpusSignalProvider>? corpusSignalProviders = null,
        SimulationProfileCatalog? simulationProfiles = null,
        PlaygroupService? playgroups = null,
        DeckAnalysisMetrics? analysisMetrics = null,
        DeckBatchTuningService? batchTuning = null,
        DeckQueryService? queries = null,
        DeckGoalPackageService? goalPackages = null,
        DeckReplacementService? replacements = null,
        DeckCategorySuggestionService? categories = null
    )
        : base(
            repository,
            cardCatalog,
            planRepository,
            currentDateOverride)
    {
        this.archidektGateway = archidektGateway;
        this.commanderMetaProvider = commanderMetaProvider;
        this.cardTrendProvider = cardTrendProvider;
        this.corpusSignalProviders = corpusSignalProviders?.ToList() ?? [];
        this.analysis = analysis;
        this.analysisMetrics = analysisMetrics ?? new DeckAnalysisMetrics(cardCatalog, CurrentDate);
        this.batchTuning = batchTuning ?? new DeckBatchTuningService(repository, analysis, simulation);
        this.queries = queries ?? new DeckQueryService(repository, cardCatalog, planRepository);
        this.goalPackages = goalPackages ?? new DeckGoalPackageService(repository, this.queries);
        this.replacements = replacements ?? new DeckReplacementService(repository, cardCatalog, this.analysisMetrics, planRepository);
        this.categories = categories ?? new DeckCategorySuggestionService(repository, planRepository);
        this.simulation = simulation;
        this.simulationProfiles = simulationProfiles ?? SimulationProfileCatalog.CreateDefault();
        this.playgroups = playgroups;
    }
}
