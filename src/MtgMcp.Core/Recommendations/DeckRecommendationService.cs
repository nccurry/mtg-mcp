namespace MtgMcp.Core;

/// <summary>
/// Creates deck improvement plans and card recommendations from catalog, trend, and meta signals.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
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
    /// Builds read-only card evaluation reports.
    /// </summary>
    private readonly DeckCardEvaluationService cardEvaluation;

    /// <summary>
    /// Finds recently released cards that fit a saved deck.
    /// </summary>
    private readonly DeckNewCardService newCards;

    /// <summary>
    /// Reviews recent cards against deterministic cut evidence.
    /// </summary>
    private readonly DeckNewCardSwapReviewService newCardSwaps;

    /// <summary>
    /// Finds deterministic payoff candidates for win-condition routes.
    /// </summary>
    private readonly DeckWinconPayoffSearchService payoffSearch;

    /// <summary>
    /// Builds source-backed Commander aggregate, tag, and win-condition evidence.
    /// </summary>
    private readonly DeckCommanderEvidenceService commanderEvidence;

    /// <summary>
    /// Compares decks against Commander metagame context and plans missing popular cards.
    /// </summary>
    private readonly DeckCommanderMetaService commanderMeta;

    /// <summary>
    /// Scores candidate cards against Playgroup-derived local meta pressure.
    /// </summary>
    private readonly DeckPlaygroupMetaScoringService playgroupMeta;

    /// <summary>
    /// Resolves Commander theme hints against deterministic corpus source tags.
    /// </summary>
    private readonly CommanderThemeResolver commanderThemes;

    /// <summary>
    /// Builds reusable analysis metrics used by recommendation scoring heuristics.
    /// </summary>
    private readonly DeckAnalysisMetrics analysisMetrics;

    /// <summary>
    /// Provides simulation workflows used by combined recommendation reports.
    /// </summary>
    private readonly DeckSimulationService simulation;

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
        DeckCategorySuggestionService? categories = null,
        DeckCardEvaluationService? cardEvaluation = null,
        DeckNewCardService? newCards = null,
        DeckNewCardSwapReviewService? newCardSwaps = null,
        DeckWinconPayoffSearchService? payoffSearch = null,
        DeckCommanderMetaService? commanderMeta = null,
        DeckPlaygroupMetaScoringService? playgroupMeta = null,
        CommanderThemeResolver? commanderThemes = null,
        DeckCommanderEvidenceService? commanderEvidence = null
    )
        : base(
            repository,
            cardCatalog,
            planRepository,
            currentDateOverride)
    {
        this.corpusSignalProviders = corpusSignalProviders?.ToList() ?? [];
        this.analysis = analysis;
        this.analysisMetrics = analysisMetrics ?? new DeckAnalysisMetrics(cardCatalog, CurrentDate);
        SimulationProfileCatalog resolvedSimulationProfiles = simulationProfiles ?? SimulationProfileCatalog.CreateDefault();
        this.batchTuning = batchTuning ?? new DeckBatchTuningService(repository, analysis, simulation);
        this.queries = queries ?? new DeckQueryService(repository, cardCatalog, planRepository);
        this.goalPackages = goalPackages ?? new DeckGoalPackageService(repository, this.queries);
        this.replacements = replacements ?? new DeckReplacementService(repository, cardCatalog, this.analysisMetrics, planRepository);
        this.categories = categories ?? new DeckCategorySuggestionService(repository, planRepository);
        this.cardEvaluation = cardEvaluation ?? new DeckCardEvaluationService(repository, cardCatalog);
        this.newCards = newCards ?? new DeckNewCardService(repository, cardCatalog, cardTrendProvider, currentDateOverride);
        this.newCardSwaps = newCardSwaps ?? new DeckNewCardSwapReviewService(repository, cardCatalog, this.newCards);
        this.payoffSearch = payoffSearch ?? new DeckWinconPayoffSearchService(cardCatalog);
        this.commanderMeta = commanderMeta ?? new DeckCommanderMetaService(repository, cardCatalog, commanderMetaProvider, planRepository);
        this.playgroupMeta = playgroupMeta ?? new DeckPlaygroupMetaScoringService(
            repository,
            cardCatalog,
            archidektGateway,
            this.analysisMetrics,
            resolvedSimulationProfiles,
            playgroups);
        this.commanderThemes = commanderThemes ?? new CommanderThemeResolver(this.corpusSignalProviders);
        this.commanderEvidence = commanderEvidence ?? new DeckCommanderEvidenceService(
            cardCatalog,
            analysis,
            this.corpusSignalProviders,
            this.commanderThemes,
            this.payoffSearch);
        this.simulation = simulation;
    }
}
