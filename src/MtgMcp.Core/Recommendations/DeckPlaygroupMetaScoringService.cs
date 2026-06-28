namespace MtgMcp.Core;

/// <summary>
/// Scores candidate cards against Playgroup-derived local-meta pressure.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Loads local workspaces for candidate and baseline scoring.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Resolves candidate card facts before scoring.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Imports Archidekt decks when Playgroup decks expose decklist URLs.
    /// </summary>
    private readonly IArchidektGateway? archidektGateway;

    /// <summary>
    /// Supplies card fact and Game Changer metadata used by scoring.
    /// </summary>
    private readonly DeckAnalysisMetrics analysisMetrics;

    /// <summary>
    /// Resolves simulation profiles for deterministic candidate scoring.
    /// </summary>
    private readonly SimulationProfileCatalog simulationProfiles;

    /// <summary>
    /// Supplies Playgroup-derived local meta context when configured.
    /// </summary>
    private readonly PlaygroupService? playgroups;

    /// <summary>
    /// Identifies meta pressure from decks that can assemble early wins.
    /// </summary>
    private const string FastComboPressure = "fast-combo";

    /// <summary>
    /// Identifies meta pressure from creature-forward combat decks.
    /// </summary>
    private const string CreatureCombatPressure = "creature-combat";

    /// <summary>
    /// Identifies meta pressure from token-heavy boards.
    /// </summary>
    private const string GoWideTokensPressure = "go-wide-tokens";

    /// <summary>
    /// Identifies meta pressure from graveyard recursion or sacrifice loops.
    /// </summary>
    private const string GraveyardRecursionPressure = "graveyard-recursion";

    /// <summary>
    /// Identifies meta pressure from stack interaction and draw-control decks.
    /// </summary>
    private const string StackControlPressure = "stack-control";

    /// <summary>
    /// Identifies meta pressure from artifact engines.
    /// </summary>
    private const string ArtifactEnginePressure = "artifact-engine";

    /// <summary>
    /// Identifies meta pressure from enchantment engines.
    /// </summary>
    private const string EnchantmentEnginePressure = "enchantment-engine";

    /// <summary>
    /// Identifies meta pressure from burn, slug, and other life-total attack decks.
    /// </summary>
    private const string LifePressure = "life-total-pressure";

    /// <summary>
    /// Identifies meta pressure from stax, prison, and tax effects.
    /// </summary>
    private const string StaxPressure = "stax-control";

    /// <summary>
    /// Caps aggregate candidate performance simulations so large batches stay responsive.
    /// </summary>
    private const int CandidatePerformanceSimulationBudget = 4_000;

    /// <summary>
    /// Keeps each candidate performance sample above the analyzer's minimum useful size.
    /// </summary>
    private const int CandidatePerformanceMinimumSimulations = 100;

    /// <summary>
    /// Limits concurrent read-only Archidekt imports while collecting local-meta deck evidence.
    /// </summary>
    private const int MetaDeckEvidenceParallelism = 4;

    /// <summary>
    /// Creates a local-meta scoring collaborator with explicit storage, card, profile, and Playgroup dependencies.
    /// </summary>
    public DeckPlaygroupMetaScoringService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IArchidektGateway? archidektGateway = null,
        DeckAnalysisMetrics? analysisMetrics = null,
        SimulationProfileCatalog? simulationProfiles = null,
        PlaygroupService? playgroups = null)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.archidektGateway = archidektGateway;
        this.analysisMetrics = analysisMetrics ?? new DeckAnalysisMetrics(
            cardCatalog,
            () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
        this.simulationProfiles = simulationProfiles ?? SimulationProfileCatalog.CreateDefault();
        this.playgroups = playgroups;
    }

    /// <summary>
    /// Scores candidate cards using deterministic deck-plan, performance, meta, budget, and confidence factors.
    /// </summary>
    public async Task<PlaygroupMetaScoringResult> ScoreCardsForPlaygroupMetaAsync(
        string workspaceId,
        string playgroupIdOrUrl,
        IReadOnlyList<string>? candidateCards,
        int maxGames,
        int metaDeckLimit,
        int simulations,
        int maxTurn,
        int seed,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        if (playgroups is null)
        {
            throw new InvalidOperationException("Playgroup service is not configured, so local-meta scoring cannot run.");
        }

        int safeMetaDeckLimit = Math.Clamp(metaDeckLimit, 1, 10);
        int safeSimulations = Math.Clamp(simulations, 100, 2_000);
        int safeMaxTurn = Math.Clamp(maxTurn, 1, 10);
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        decimal? effectiveMaxPrice = maxPrice ?? intent?.Budget.MaxCardPrice;
        PlaygroupDeckRankingResult rankings = await playgroups
            .RankDecksAsync(
                playgroupIdOrUrl,
                PlaygroupDeckRankingMetrics.EstimatedPower,
                minGames: 0,
                includeLowConfidence: true,
                maxGames,
                safeMetaDeckLimit,
                cancellationToken)
            .ConfigureAwait(false);

        ResolvedSimulationProfile profileResolution = simulationProfiles.Resolve(workspace, SimulationProfileIds.Auto, intent);
        List<string> candidateNames = CandidateNames(workspace, candidateCards);
        IReadOnlyDictionary<string, CardInfo> candidateDetails = await cardCatalog
            .GetCardsByNamesAsync(candidateNames, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlySet<string> gameChangers = await analysisMetrics.FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        List<string> warnings = [.. rankings.Warnings, .. intentResult.Warnings, .. profileResolution.Warnings];
        List<PlaygroupDeckRanking> rankedMetaDecks = rankings.Rankings.Take(safeMetaDeckLimit).ToList();
        List<PlaygroupMetaDeckEvidence> metaDecks = await BuildMetaDeckEvidenceBatchAsync(
                rankedMetaDecks,
                rankings.Rankings.Count,
                cancellationToken)
            .ConfigureAwait(false);

        List<PlaygroupMetaPressureEvidence> pressures = AggregatePressures(metaDecks);
        int candidatePerformanceSimulations = BudgetCandidatePerformanceSimulations(
            candidateNames.Count,
            safeSimulations);
        if (candidatePerformanceSimulations < safeSimulations)
        {
            warnings.Add(
                $"Candidate performance simulations were capped at {candidatePerformanceSimulations} per card from requested {safeSimulations} to keep local-meta scoring responsive for {candidateNames.Count} candidates; baseline analysis still used {safeSimulations} simulations.");
        }

        if (candidateNames.Count == 0)
        {
            warnings.Add("No candidate cards were supplied and no cards were found in excluded workspace categories.");
            return new PlaygroupMetaScoringResult
            {
                WorkspaceId = workspace.Id,
                PlaygroupId = rankings.PlaygroupId,
                CandidateSource = "none",
                ProfileResolution = profileResolution,
                MetaDecks = metaDecks,
                MetaPressures = pressures,
                Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Notes =
                [
                    "Playgroup deck selection is derived from fetched game participations, not a direct full playgroup deck list.",
                    "Scores are deterministic heuristics; no candidates were available to score.",
                ],
            };
        }

        DeckPerformanceAnalysis baseline = DeckPerformanceAnalyzer.Analyze(
            workspace,
            SimulationProfileIds.Auto,
            safeSimulations,
            safeMaxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);
        (bool colorKnown, HashSet<string> colors) = DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
        List<PlaygroupMetaCandidateScore> scores = [];

        foreach (string candidateName in candidateNames)
        {
            CardInfo? card = candidateDetails.Values.FirstOrDefault(value =>
                value.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
            if (card is null)
            {
                DeckCard? workspaceCard = workspace.Cards.FirstOrDefault(value =>
                    value.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
                if (workspaceCard?.Snapshot is null)
                {
                    warnings.Add($"Candidate '{candidateName}' could not be resolved from the card catalog.");
                    continue;
                }

                card = CardInfoFromWorkspaceCard(workspaceCard);
            }

            scores.Add(ScoreMetaCandidate(
                workspace,
                card,
                baseline,
                pressures,
                profileResolution,
                intent,
                effectiveMaxPrice,
                gameChangers,
                colorKnown,
                colors,
                candidatePerformanceSimulations,
                safeMaxTurn,
                seed,
                cancellationToken));
        }

        PlaygroupMetaScoringResult result = new()
        {
            WorkspaceId = workspace.Id,
            PlaygroupId = rankings.PlaygroupId,
            CandidateSource = candidateCards is { Count: > 0 }
                ? "explicit-card-list"
                : "excluded-workspace-categories",
            ProfileResolution = profileResolution,
            MetaDecks = metaDecks,
            MetaPressures = pressures,
            CandidateScores = scores
                .OrderByDescending(score => score.OverallScore)
                .ThenBy(score => score.CardName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        result.Notes.Add("Playgroup deck selection is derived from fetched game participations, not a direct full playgroup deck list.");
        result.Notes.Add("Archidekt decklists are imported read-only when a Playgroup deck exposes an Archidekt URL.");
        result.Notes.Add("Scores are deterministic heuristics; meta coverage and self-harm are evidence factors, not full matchup simulations.");
        return result;
    }
}
