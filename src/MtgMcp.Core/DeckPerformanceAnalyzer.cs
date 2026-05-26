namespace MtgMcp.Core;

/// <summary>
/// Analyzes deck performance without repository, MCP, or backend concerns.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Builds the complete performance report from repeated deterministic runs.
    /// </summary>
    public static DeckPerformanceAnalysis Analyze(
        DeckWorkspace workspace,
        string profile,
        int simulations,
        int maxTurn,
        int seed,
        bool includeMulligans,
        CancellationToken cancellationToken,
        SimulationProfileCatalog? simulationProfiles = null)
    {
        int safeSimulations = Math.Clamp(simulations, 100, 100_000);
        int safeMaxTurn = Math.Clamp(maxTurn, 1, 20);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        ResolvedSimulationProfile profileResolution = (simulationProfiles ?? SimulationProfileCatalog.CreateDefault())
            .Resolve(workspace, profile, intent);
        SimulationProfile resolvedProfile = profileResolution.Profile;
        List<DeckCard> included = IncludedCards(workspace).ToList();
        PerformanceCardFactsCache cardFacts = new(included);
        int deckSize = included.Sum(card => Math.Max(0, card.Quantity));
        (bool colorIdentityKnown, HashSet<string> deckColors) = GetDeckColorIdentity(included, cardFacts);
        List<DeckCard> libraryTemplate = ExpandPerformanceLibrary(included, cardFacts);
        CommandZonePlan commandZonePlan = CommandZonePlanner.Build(included, resolvedProfile);
        PerformanceMulliganContext mulliganContext = BuildPerformanceMulliganContext(
            workspace,
            commandZonePlan,
            deckColors,
            resolvedProfile);
        List<PerformanceRun> runs = [];

        for (int index = 0; index < safeSimulations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.Add(RunPerformanceGame(
                libraryTemplate,
                commandZonePlan,
                mulliganContext,
                cardFacts,
                deckColors,
                safeMaxTurn,
                seed + index,
                includeMulligans,
                resolvedProfile));
        }

        DeckPerformanceAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            Profile = resolvedProfile.Id,
            ProfileResolution = profileResolution,
            Simulations = safeSimulations,
            MaxTurn = safeMaxTurn,
            Seed = seed,
            IncludeMulligans = includeMulligans,
            DeckSize = deckSize,
            OpeningHands = BuildOpeningHandPerformance(runs),
            Castability = BuildCastabilityPerformance(runs, deckColors, colorIdentityKnown, safeMaxTurn),
            Commander = BuildCommanderPerformance(runs, safeMaxTurn, commandZonePlan),
            CommandZone = BuildCommandZonePerformance(runs, safeMaxTurn, commandZonePlan),
            ComboAssembly = BuildComboAssemblyPerformance(included, runs, safeMaxTurn, cardFacts),
            StrandedCards = BuildStrandedCardPerformance(runs),
        };

        AddTurnPerformanceMetrics(analysis, runs, deckColors, colorIdentityKnown, safeMaxTurn);
        analysis.Scenarios = BuildScenarioPerformance(
            included,
            runs,
            deckColors,
            colorIdentityKnown,
            safeMaxTurn,
            resolvedProfile,
            intent,
            cardFacts);
        AddPerformanceNotes(analysis, workspace, included, colorIdentityKnown, profileResolution, intent, cardFacts);
        analysis.Warnings.AddRange(profileResolution.Warnings);
        return analysis;
    }

}
