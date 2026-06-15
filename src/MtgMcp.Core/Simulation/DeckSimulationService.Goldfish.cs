namespace MtgMcp.Core;

/// <summary>
/// Provides heuristic goldfish simulation behavior.
/// </summary>
public sealed partial class DeckSimulationService : DeckServiceBase
{
    /// <summary>
    /// Labels heuristic no-interaction simulations that favor smooth sequencing.
    /// </summary>
    private const string GoldfishModelLabel = "optimistic-goldfish-model";

    /// <summary>
    /// Labels board projection output derived from heuristic goldfish snapshots.
    /// </summary>
    private const string BoardProjectionModelLabel = "heuristic-board-projection";

    /// <summary>
    /// Runs a heuristic no-interaction goldfish simulation.
    /// </summary>
    public async Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        return await SimulateGoldfishAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                targetTurn,
                simulations,
                seed,
                mulligan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a heuristic no-interaction goldfish simulation with a caller-selected simulation profile.
    /// </summary>
    public async Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return SimulateGoldfish(workspace, simulationProfile, targetTurn, simulations, seed, mulligan, simulationProfiles);
    }

    /// <summary>
    /// Projects the likely board state by a requested turn.
    /// </summary>
    public async Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        int turn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await ProjectBoardStateAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                turn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Projects the likely board state by a requested turn with a caller-selected simulation profile.
    /// </summary>
    public async Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        string simulationProfile,
        int turn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            simulationProfile,
            turn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState
            {
                Turn = Math.Max(1, turn),
                ModelLabel = BoardProjectionModelLabel,
                LikelyBoard = "No projection could be produced.",
                Notes = ["Projection is derived from the optimistic goldfish model and does not model opponent interaction."],
            };
    }

    /// <summary>
    /// Estimates the likely win turn and win routes.
    /// </summary>
    public async Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        int maxTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await EstimateWinTurnAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                maxTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Estimates likely goldfish win turns and routes with a caller-selected simulation profile.
    /// </summary>
    public async Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        string simulationProfile,
        int maxTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            simulationProfile,
            maxTurn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.WinEstimate;
    }

    /// <summary>
    /// Runs the goldfish simulator for a workspace.
    /// </summary>
    private static GoldfishSimulationResult SimulateGoldfish(
        DeckWorkspace workspace,
        string? requestedProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        SimulationProfileCatalog? simulationProfiles = null)
    {
        int safeTurn = Math.Clamp(targetTurn, 1, 20);
        int safeSimulations = Math.Clamp(simulations, 100, 10_000);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        ResolvedSimulationProfile profileResolution = (simulationProfiles ?? SimulationProfileCatalog.CreateDefault())
            .Resolve(workspace, requestedProfile, intent);
        CommandZonePlan commandZonePlan = CommandZonePlanner.Build(
            IncludedCards(workspace),
            profileResolution.Profile);
        CommanderSpecificSimulationRules commanderRules = CommanderSpecificSimulationRules.Build(
            IncludedCards(workspace));
        List<GoldfishRun> runs = [];
        for (int index = 0; index < safeSimulations; index++)
        {
            runs.Add(RunGoldfishGame(
                workspace,
                safeTurn,
                seed + index,
                mulligan,
                profileResolution,
                commandZonePlan,
                commanderRules));
        }

        GoldfishSimulationResult result = new()
        {
            WorkspaceId = workspace.Id,
            ModelLabel = GoldfishModelLabel,
            Simulations = safeSimulations,
            TargetTurn = safeTurn,
            Mulligans = runs.Count(run => run.Mulliganed),
            ProfileResolution = profileResolution,
            CommandZone = BuildCommandZonePerformance(runs, safeTurn, commandZonePlan),
            WinEstimate = BuildWinEstimate(workspace, runs, safeTurn)
        };
        for (int turn = 1; turn <= safeTurn; turn++)
        {
            result.TurnSummaries.Add(BuildProjectedTurnState(turn, runs));
        }

        AddGoldfishSummaryMetrics(result, runs, safeTurn);

        IEnumerable<GoldfishRun> representativeCandidates = runs;
        if (commandZonePlan.HasBackgroundPair && runs.Any(run => run.CommanderWithBackgroundOnlineTurn.HasValue))
        {
            representativeCandidates = runs.Where(run => run.CommanderWithBackgroundOnlineTurn.HasValue);
        }
        else if (commandZonePlan.HasCommander && runs.Any(run => run.CommanderCastTurn.HasValue))
        {
            representativeCandidates = runs.Where(run => run.CommanderCastTurn.HasValue);
        }

        GoldfishRun representative = representativeCandidates
            .OrderBy(run => Math.Abs((run.WinTurn ?? safeTurn + 4) - (result.WinEstimate.MedianObservedWinTurn ?? safeTurn + 4)))
            .First();
        result.RepresentativeLines = representative.Line.Take(16).ToList();
        result.Notes.Add("Goldfish projection assumes no opponent interaction and uses role/tag heuristics rather than a full Magic rules engine.");
        result.Notes.Add(
            "Model label optimistic-goldfish-model: this tool projects board development and fallback win pressure, "
                + "so commander timing can differ from deck_analyze_performance's strict-sequencing-model scenarios.");
        result.Notes.Add("Commander is treated as available from the command zone when the deck has a Commander category.");
        result.Notes.Add($"Resolved simulation profile '{profileResolution.Profile.Id}' from {profileResolution.Source}.");
        result.Notes.AddRange(commanderRules.Assumptions);
        result.WinEstimate.Notes.AddRange(commanderRules.Assumptions);
        foreach (ProjectedTurnState summary in result.TurnSummaries)
        {
            summary.Notes.AddRange(commanderRules.Assumptions);
        }

        if (BuildPartialCommanderDeckWarning(workspace) is string partialDeckWarning)
        {
            result.Warnings.Add(partialDeckWarning);
        }

        result.Warnings.AddRange(profileResolution.Warnings);
        return result;
    }

    /// <summary>
    /// Runs one goldfish game.
    /// </summary>
    private static GoldfishRun RunGoldfishGame(
        DeckWorkspace workspace,
        int targetTurn,
        int seed,
        bool mulligan,
        ResolvedSimulationProfile profileResolution,
        CommandZonePlan commandZonePlan,
        CommanderSpecificSimulationRules commanderRules)
    {
        Random random = new(seed);
        GoldfishOpeningHand opening = DrawGoldfishOpeningHand(
            workspace,
            random,
            mulligan,
            profileResolution.Profile,
            commandZonePlan);
        List<DeckCard> hand = opening.Hand;
        List<DeckCard> deck = opening.Library;

        CommandZoneRunState commandZone = new(commandZonePlan);
        List<DeckCard> battlefield = [];
        List<DeckCard> graveyard = [];
        GoldfishRun run = new() { Mulliganed = opening.Mulligans > 0 };
        int tokens = 0;
        int artifactTokens = 0;
        int foodTokens = 0;
        int lifeGainEvents = 0;
        int winPressure = 0;
        int dungeonProgress = 0;

        for (int turn = 1; turn <= targetTurn; turn++)
        {
            if (deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
            }

            DeckCard? land = hand.FirstOrDefault(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase));
            if (land is not null)
            {
                hand.Remove(land);
                battlefield.Add(land);
                run.Line.Add($"T{turn}: played {land.Name}.");
            }

            int availableMana = CountManaSources(battlefield);
            int restrictedCreatureMana = 0;
            bool restrictedCreatureManaInitialized = false;
            List<DeckCard> castThisTurn = [];
            if (profileResolution.Profile.Sequencing.PreferCommanderOnCurve)
            {
                CastGoldfishCommandZoneCards(commandZone, turn, battlefield, run, tokens, artifactTokens, ref availableMana);
                RefreshIngaGrantedCreatureMana(
                    battlefield,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureManaInitialized,
                    ref availableMana,
                    ref restrictedCreatureMana);
            }

            if (profileResolution.Profile.Sequencing.PreferCommanderOnCurve)
            {
                CastGoldfishHandSpells(
                    hand,
                    deck,
                    battlefield,
                    graveyard,
                    castThisTurn,
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.All,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
                    ref artifactTokens,
                    ref foodTokens,
                    ref lifeGainEvents,
                    ref winPressure,
                    ref dungeonProgress);
            }
            else
            {
                RefreshIngaGrantedCreatureMana(
                    battlefield,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureManaInitialized,
                    ref availableMana,
                    ref restrictedCreatureMana);
                CastGoldfishHandSpells(
                    hand,
                    deck,
                    battlefield,
                    graveyard,
                    castThisTurn,
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.SetupOnly,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
                    ref artifactTokens,
                    ref foodTokens,
                    ref lifeGainEvents,
                    ref winPressure,
                    ref dungeonProgress);
                CastGoldfishCommandZoneCards(commandZone, turn, battlefield, run, tokens, artifactTokens, ref availableMana);
                RefreshIngaGrantedCreatureMana(
                    battlefield,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureManaInitialized,
                    ref availableMana,
                    ref restrictedCreatureMana);
                CastGoldfishHandSpells(
                    hand,
                    deck,
                    battlefield,
                    graveyard,
                    castThisTurn,
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.NonSetup,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
                    ref artifactTokens,
                    ref foodTokens,
                    ref lifeGainEvents,
                    ref winPressure,
                    ref dungeonProgress);
            }

            int power = EstimateBattlefieldPower(battlefield, tokens);
            int lifeGainAvailable = EstimateLifeGainAvailable(
                foodTokens,
                lifeGainEvents,
                availableMana,
                commanderRules.HasSamLoyalAttendant && battlefield.Any(IsSamLoyalAttendant));
            int pressureScore = EstimateThreatPressure(
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                lifeGainAvailable,
                power,
                winPressure,
                commandZone.CommanderOnline);
            bool engineOnline = HasGoldfishEngineOnline(battlefield);
            ActivatedCommanderEnginePressure enginePressure = BuildActivatedCommanderEnginePressure(
                workspace,
                battlefield,
                hand,
                availableMana,
                commandZone.CommanderOnline);
            SorceryFinisherPressure sorceryFinisherPressure = BuildSorceryFinisherPressure(
                hand,
                castThisTurn,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                availableMana,
                commandZone.CommanderOnline,
                power);
            pressureScore = Math.Clamp(
                pressureScore + (enginePressure.Pressure / 3) + (sorceryFinisherPressure.Pressure / 2),
                0,
                100);
            int comboPieces = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler));
            if (!run.WinTurn.HasValue)
            {
                List<SimulationRouteEvidence> routeEvidence = SimulationRouteEvaluator.EvaluateRoutes(
                    profileResolution.Profile.WinRoutes,
                    new SimulationRouteState
                    {
                        Turn = turn,
                        Battlefield = battlefield,
                        Hand = hand,
                        Graveyard = graveyard,
                        CommanderOnBattlefield = commandZone.CommanderOnline,
                        Tokens = tokens,
                        ArtifactTokens = artifactTokens,
                        FoodTokens = foodTokens,
                        LifeGainAvailable = lifeGainAvailable,
                        AvailableMana = availableMana,
                        InteractionHeld = CountHeldGoldfishInteraction(hand, availableMana),
                        DungeonProgress = dungeonProgress,
                    });
                SimulationRouteEvidence? matchedRoute = routeEvidence
                    .Where(route => route.Matched)
                    .OrderBy(route => route.EarliestTurn)
                    .ThenBy(route => route.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (matchedRoute is not null)
                {
                    run.WinTurn = Math.Max(turn, matchedRoute.EarliestTurn);
                    run.WinRoute = matchedRoute.Kind;
                    run.RouteEvidence.Add(matchedRoute);
                }
                else if (profileResolution.Profile.WinDetection.AllowFallbackComboWins && comboPieces >= 2)
                {
                    run.WinTurn = Math.Max(turn, profileResolution.Profile.WinDetection.FallbackComboEarliestTurn);
                    run.WinRoute = "combo";
                    run.RouteEvidence.Add(FallbackRouteEvidence(
                        "fallback combo tags",
                        "combo",
                        "fallback",
                        run.WinTurn.Value,
                        $"battlefield has {comboPieces} broad combo-piece/enabler tags"));
                }
                else if (winPressure >= profileResolution.Profile.WinDetection.FinisherPressureThreshold
                    && power >= profileResolution.Profile.WinDetection.FinisherPowerThreshold)
                {
                    run.WinTurn = Math.Max(turn, profileResolution.Profile.WinDetection.FinisherEarliestTurn);
                    run.WinRoute = "finisher";
                    run.RouteEvidence.Add(FallbackRouteEvidence(
                        "fallback finisher pressure",
                        "finisher",
                        "fallback",
                        run.WinTurn.Value,
                        BuildFallbackPressureEvidence(
                            battlefield,
                            tokens,
                            power,
                            winPressure,
                            profileResolution.Profile.WinDetection.FinisherPowerThreshold,
                            "finisher")));
                }
                else if (power >= profileResolution.Profile.WinDetection.CombatPowerThreshold)
                {
                    run.WinTurn = Math.Max(turn, profileResolution.Profile.WinDetection.CombatEarliestTurn);
                    run.WinRoute = "combat";
                    run.RouteEvidence.Add(FallbackRouteEvidence(
                        "fallback combat pressure",
                        "combat",
                        "fallback",
                        run.WinTurn.Value,
                        BuildFallbackPressureEvidence(
                            battlefield,
                            tokens,
                            power,
                            winPressure,
                            profileResolution.Profile.WinDetection.CombatPowerThreshold,
                            "combat")));
                }
            }

            run.Turns.Add(new GoldfishTurnSnapshot
            {
                Turn = turn,
                Lands = battlefield.Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)),
                ManaSources = CountManaSources(battlefield),
                NonlandPermanents = battlefield.Count(card => !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)),
                CardsInHand = hand.Count,
                Power = power,
                Tokens = tokens,
                ThreatPressure = pressureScore,
                EngineOnline = engineOnline,
                EnginePressure = enginePressure,
                SorceryFinisherPressure = sorceryFinisherPressure,
                CommanderCastByTurn = commandZone.CommanderOnline,
                BackgroundCastByTurn = commandZone.BackgroundOnline,
                CommanderWithBackgroundOnlineByTurn = commandZone.CommanderWithBackgroundOnlineTurn.HasValue,
            });
        }

        run.CommanderCastTurn = commandZone.CommanderCastTurn;
        run.BackgroundCastTurn = commandZone.BackgroundCastTurn;
        run.CommanderWithBackgroundOnlineTurn = commandZone.CommanderWithBackgroundOnlineTurn;
        return run;
    }

    /// <summary>
    /// Expands the workspace into a shuffled library candidate.
    /// </summary>
    private static List<DeckCard> ExpandLibrary(DeckWorkspace workspace)
    {
        List<DeckCard> cards = [];
        foreach (DeckCard card in IncludedCards(workspace).Where(card => !IsCommanderCard(card)))
        {
            for (int copy = 0; copy < Math.Max(0, card.Quantity); copy++)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    /// <summary>
    /// Shuffles a list in place.
    /// </summary>
    private static void Shuffle(List<DeckCard> cards, Random random)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    /// <summary>
    /// Checks whether an opening hand is keepable.
    /// </summary>
    private static GoldfishOpeningHand DrawGoldfishOpeningHand(
        DeckWorkspace workspace,
        Random random,
        bool includeMulligans,
        SimulationProfile profile,
        CommandZonePlan commandZonePlan)
    {
        int mulligans = 0;
        int maximumMulligans = MulliganHeuristics.MaximumMulligans(workspace.Format);
        while (mulligans <= maximumMulligans)
        {
            int targetHandSize = MulliganHeuristics.TargetHandSize(mulligans, workspace.Format);
            List<DeckCard> library = ExpandLibrary(workspace);
            Shuffle(library, random);
            List<DeckCard> hand = library.Take(Math.Min(7, library.Count)).ToList();
            library = library.Skip(hand.Count).ToList();
            bool keep = !includeMulligans
                || IsKeepableGoldfishHand(hand, targetHandSize, mulligans, workspace, profile, commandZonePlan)
                || targetHandSize <= 5;
            if (keep)
            {
                BottomGoldfishCards(hand, targetHandSize);
                return new GoldfishOpeningHand
                {
                    Hand = hand,
                    Library = library,
                    Mulligans = mulligans,
                };
            }

            mulligans++;
        }

        throw new InvalidOperationException("Goldfish mulligan heuristic failed to keep a hand by five cards.");
    }

    /// <summary>
    /// Determines whether a candidate goldfish opening hand should be kept.
    /// </summary>
    private static bool IsKeepableGoldfishHand(
        IReadOnlyList<DeckCard> hand,
        int targetHandSize,
        int mulligans,
        DeckWorkspace workspace,
        SimulationProfile profile,
        CommandZonePlan commandZonePlan)
    {
        int lands = CountGoldfishRole(hand, DeckRoles.Lands);
        if (lands == 0 || (targetHandSize >= 6 && lands >= 6))
        {
            return false;
        }

        double score = ScoreGoldfishHand(hand, profile, commandZonePlan);
        double keepScore = targetHandSize <= 5
            ? profile.Mulligan.FiveCardKeepScore
            : targetHandSize == 6
                ? profile.Mulligan.SixCardKeepScore
                : MulliganHeuristics.UsesFreeFirstMulligan(workspace.Format) && mulligans == 0
                    ? profile.Mulligan.SevenCardFreeKeepScore
                    : profile.Mulligan.SevenCardKeepScore;
        return score >= keepScore;
    }

    /// <summary>
    /// Scores a goldfish hand with the resolved profile's mulligan weights.
    /// </summary>
    private static double ScoreGoldfishHand(
        IReadOnlyList<DeckCard> hand,
        SimulationProfile profile,
        CommandZonePlan commandZonePlan)
    {
        int lands = CountGoldfishRole(hand, DeckRoles.Lands);
        int ramp = CountCheapGoldfishRole(hand, DeckRoles.Ramp, 2);
        int draw = CountCheapGoldfishRole(hand, DeckRoles.Draw, 3);
        int interaction = CountCheapGoldfishRole(hand, DeckRoles.Interaction, 2)
            + CountCheapGoldfishRole(hand, DeckRoles.Protection, 2);
        int cheapPlays = hand.Count(card => !IsGoldfishRole(card, DeckRoles.Lands) && GoldfishManaValue(card) <= 2);
        double score = lands switch
        {
            1 => ramp >= 2 ? 2 : -4,
            2 => 4,
            3 => 5,
            4 => 3,
            5 => 1,
            _ => -4,
        };
        score += Math.Min(ramp, 2) * profile.Mulligan.EarlyRampWeight;
        score += Math.Min(draw, 2) * profile.Mulligan.EarlyDrawWeight;
        score += Math.Min(interaction, 2) * profile.Mulligan.EarlyInteractionWeight;
        score += Math.Min(cheapPlays, 3) * profile.Mulligan.CheapPlayWeight;
        if (HasGoldfishCommanderPlan(hand, commandZonePlan, profile))
        {
            score += profile.Mulligan.CommanderPlanWeight;
        }

        return score;
    }

    /// <summary>
    /// Bottoms lower-priority cards after London mulligans.
    /// </summary>
    private static void BottomGoldfishCards(List<DeckCard> hand, int targetHandSize)
    {
        while (hand.Count > targetHandSize)
        {
            DeckCard card = hand
                .OrderByDescending(GoldfishBottomPriority)
                .ThenByDescending(GoldfishManaValue)
                .First();
            hand.Remove(card);
        }
    }

    /// <summary>
    /// Scores how attractive a card is to bottom after a mulligan.
    /// </summary>
    private static int GoldfishBottomPriority(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || GoldfishManaValue(card) >= 6)
        {
            return 5;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return 3;
    }

    /// <summary>
    /// Builds a warning when a Commander goldfish samples fewer or more than 100 included cards.
    /// </summary>
    private static string? BuildPartialCommanderDeckWarning(DeckWorkspace workspace)
    {
        int includedCount = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity));
        if (!MulliganHeuristics.UsesCommanderDeckConstruction(workspace.Format) || includedCount == 100)
        {
            return null;
        }

        return $"Commander workspace has {includedCount} included cards instead of 100; excluded categories such as Sideboard and Maybeboard are not sampled, so goldfish probabilities reflect a partial active deck.";
    }

    /// <summary>
    /// Counts cards with a requested primary role.
    /// </summary>
    private static int CountGoldfishRole(IEnumerable<DeckCard> cards, string role)
    {
        return cards.Count(card => IsGoldfishRole(card, role));
    }

    /// <summary>
    /// Counts cheap cards with a requested primary role.
    /// </summary>
    private static int CountCheapGoldfishRole(IEnumerable<DeckCard> cards, string role, int maxManaValue)
    {
        return cards.Count(card => IsGoldfishRole(card, role) && GoldfishManaValue(card) <= maxManaValue);
    }

    /// <summary>
    /// Checks whether a card's primary role matches a requested role.
    /// </summary>
    private static bool IsGoldfishRole(DeckCard card, string role)
    {
        return DeckRoleClassifier.Classify(card).PrimaryRole.Equals(role, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a nonnegative mana value for goldfish heuristics.
    /// </summary>
    private static int GoldfishManaValue(DeckCard card)
    {
        return Math.Max(0, (int)Math.Ceiling(GetSnapshot(card).ManaValue ?? 2));
    }

    /// <summary>
    /// Estimates what the goldfish sequencer must spend to cast a spell right now.
    /// </summary>
    private static GoldfishCastCost EstimateGoldfishCastCost(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int availableMana,
        bool commanderOnline)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        string text = snapshot.OracleText ?? "";
        int printedCost = GoldfishManaValue(card);
        int requiredMana = printedCost;
        if (HasConvoke(text))
        {
            requiredMana = Math.Max(0, requiredMana - ConvokeCreatureCount(battlefield, tokens));
        }

        int affinityReduction = EstimateAffinityReduction(card, battlefield, tokens, artifactTokens);
        if (affinityReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - affinityReduction);
        }

        int dynamicReduction = EstimateDynamicCostReduction(card, battlefield, tokens, artifactTokens, foodTokens);
        if (dynamicReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - dynamicReduction);
        }

        int activeReduction = EstimateActiveCostReduction(card, battlefield);
        if (activeReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - activeReduction);
        }

        int commanderReduction = EstimateCommanderConditionReduction(card, commanderOnline);
        if (commanderReduction > 0)
        {
            requiredMana = Math.Max(MinimumReducedCost(snapshot.ManaCost), requiredMana - commanderReduction);
        }

        int xValue = 0;
        if (HasGoldfishXCost(card) && UsesXAsScalingPayoff(card) && availableMana > requiredMana)
        {
            xValue = Math.Min(8, availableMana - requiredMana);
        }

        return new GoldfishCastCost(
            RequiredMana: Math.Max(0, requiredMana),
            XValue: xValue);
    }

    /// <summary>
    /// Counts creatures that can safely pay convoke costs in a goldfish board.
    /// </summary>
    private static int ConvokeCreatureCount(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int creatures = tokens;
        foreach (DeckCard card in battlefield)
        {
            if (IsCreatureSpell(card))
            {
                creatures++;
            }
        }

        return creatures;
    }

    /// <summary>
    /// Checks whether Oracle text contains the convoke keyword.
    /// </summary>
    private static bool HasConvoke(string oracleText)
    {
        return oracleText.Contains("convoke", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Estimates card-text reductions such as Blasphemous Act without full rules parsing.
    /// </summary>
    private static int EstimateDynamicCostReduction(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        if (!ContainsAny(text, "costs {1} less", "cost {1} less", "costs one less", "cost one less"))
        {
            return 0;
        }

        if (ContainsAny(text, "for each creature"))
        {
            return ConvokeCreatureCount(battlefield, tokens);
        }

        if (ContainsAny(text, "for each token"))
        {
            return tokens;
        }

        if (ContainsAny(text, "for each artifact"))
        {
            return CountArtifactPermanents(battlefield) + artifactTokens;
        }

        if (ContainsAny(text, "for each food"))
        {
            return foodTokens;
        }

        if (ContainsAny(text, "for each enchantment"))
        {
            return battlefield.Count(permanent => ContainsAny(GetSnapshot(permanent).TypeLine ?? "", "Enchantment"));
        }

        return 0;
    }

    /// <summary>
    /// Estimates affinity reductions from the current battlefield and token bank.
    /// </summary>
    private static int EstimateAffinityReduction(
        DeckCard card,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        if (!ContainsAny(text, "affinity for"))
        {
            return 0;
        }

        if (ContainsAny(text, "affinity for artifacts"))
        {
            return CountArtifactPermanents(battlefield) + artifactTokens;
        }

        if (ContainsAny(text, "affinity for creatures"))
        {
            return ConvokeCreatureCount(battlefield, tokens);
        }

        if (ContainsAny(text, "affinity for tokens"))
        {
            return tokens;
        }

        if (ContainsAny(text, "affinity for enchantments"))
        {
            return battlefield.Count(permanent => ContainsAny(GetSnapshot(permanent).TypeLine ?? "", "Enchantment"));
        }

        return 0;
    }

    /// <summary>
    /// Counts artifact permanents already represented as cards on the battlefield.
    /// </summary>
    private static int CountArtifactPermanents(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Count(permanent => ContainsAny(GetSnapshot(permanent).TypeLine ?? "", "Artifact"));
    }

    /// <summary>
    /// Estimates simple reductions gated on controlling a commander.
    /// </summary>
    private static int EstimateCommanderConditionReduction(DeckCard card, bool commanderOnline)
    {
        if (!commanderOnline)
        {
            return 0;
        }

        string text = GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "if you control your commander", "if you control a commander", "as long as you control your commander")
            && ContainsAny(text, "costs {1} less", "cost {1} less", "costs one less", "cost one less")
            ? 1
            : 0;
    }

    /// <summary>
    /// Estimates cost reduction from permanents already deployed in the goldfish board.
    /// </summary>
    private static int EstimateActiveCostReduction(DeckCard spell, IReadOnlyList<DeckCard> battlefield)
    {
        int reduction = 0;
        foreach (DeckCard permanent in battlefield)
        {
            if (CostReducerApplies(permanent, spell))
            {
                reduction++;
            }
        }

        return Math.Min(3, reduction);
    }

    /// <summary>
    /// Checks whether one battlefield permanent reduces the candidate spell's cost.
    /// </summary>
    private static bool CostReducerApplies(DeckCard reducer, DeckCard spell)
    {
        string text = GetSnapshot(reducer).OracleText ?? "";
        if (!ContainsAny(text, "cost {1} less", "costs {1} less", "cost one less", "costs one less", "cost less to cast"))
        {
            return false;
        }

        string typeLine = GetSnapshot(spell).TypeLine ?? "";
        if (ContainsAny(text, "commander spells") && !IsCommanderCard(spell))
        {
            return false;
        }

        if (ContainsAny(text, "creature spells") && !ContainsAny(typeLine, "Creature"))
        {
            return false;
        }

        if (ContainsAny(text, "instant and sorcery spells")
            && !ContainsAny(typeLine, "Instant", "Sorcery"))
        {
            return false;
        }

        if (ContainsAny(text, "artifact spells") && !ContainsAny(typeLine, "Artifact"))
        {
            return false;
        }

        if (ContainsAny(text, "enchantment spells") && !ContainsAny(typeLine, "Enchantment"))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Keeps generic cost reduction from erasing colored mana that still has to be paid.
    /// </summary>
    private static int MinimumReducedCost(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return 0;
        }

        int coloredSymbols = 0;
        for (int index = 0; index < manaCost.Length; index++)
        {
            if (manaCost[index] != '{')
            {
                continue;
            }

            int close = manaCost.IndexOf('}', index + 1);
            if (close < 0)
            {
                break;
            }

            string symbol = manaCost[(index + 1)..close];
            if (!int.TryParse(symbol, out _)
                && !symbol.Equals("X", StringComparison.OrdinalIgnoreCase))
            {
                coloredSymbols++;
            }

            index = close;
        }

        return coloredSymbols;
    }

    /// <summary>
    /// Checks whether the card can spend extra mana through an X cost.
    /// </summary>
    private static bool HasGoldfishXCost(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        return ContainsAny(snapshot.ManaCost ?? "", "{X}", "{x}");
    }

    /// <summary>
    /// Checks whether spending extra mana on X changes a board or damage outcome.
    /// </summary>
    private static bool UsesXAsScalingPayoff(DeckCard card)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "create X", "draw X", "deals X", "get +X/+X", "gets +X/+X", "lose X life");
    }

    /// <summary>
    /// Estimates token production while preserving artifact and Food subcounts.
    /// </summary>
    private static GoldfishTokenProduction EstimateTokenProduction(
        DeckCard spell,
        CardRoleAssignment role,
        int xValue)
    {
        string text = GetSnapshot(spell).OracleText ?? "";
        int food = EstimateNamedTokenCount(text, "Food");
        int artifact = food;
        artifact += EstimateNamedTokenCount(text, "Treasure");
        artifact += EstimateNamedTokenCount(text, "Clue");
        artifact += EstimateNamedTokenCount(text, "Blood");
        artifact += EstimateNamedTokenCount(text, "Map");
        artifact += EstimateArtifactTokenCount(text);

        int total = Math.Max(food, artifact);
        if (role.Tags.Contains(DeckTags.Tokens, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.SacrificeFodder, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.ArtifactTokens, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Food, StringComparer.OrdinalIgnoreCase))
        {
            total = Math.Max(total, 2 + EstimateTokenScaling(spell, xValue));
        }

        if (xValue > 0 && ContainsAny(text, "create X"))
        {
            total = Math.Max(total, Math.Min(8, xValue));
            if (ContainsAny(text, "artifact token", "artifact tokens", "Food", "Treasure", "Clue", "Blood", "Map"))
            {
                artifact = Math.Max(artifact, Math.Min(8, xValue));
            }
        }

        return new GoldfishTokenProduction(
            Total: Math.Clamp(total, 0, 12),
            ArtifactTokens: Math.Clamp(artifact, 0, 12),
            FoodTokens: Math.Clamp(food, 0, 12));
    }

    /// <summary>
    /// Estimates explicit named-token counts from common English number words.
    /// </summary>
    private static int EstimateNamedTokenCount(string text, string tokenName)
    {
        if (!ContainsAny(text, tokenName))
        {
            return 0;
        }

        string singular = $"{tokenName} token";
        string plural = $"{tokenName} tokens";
        if (ContainsAny(text, $"three {singular}", $"three {plural}"))
        {
            return 3;
        }

        if (ContainsAny(text, $"two {singular}", $"two {plural}"))
        {
            return 2;
        }

        if (ContainsAny(text, $"a {singular}", $"one {singular}", singular, plural))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Estimates artifact-token counts when text names artifact tokens generically.
    /// </summary>
    private static int EstimateArtifactTokenCount(string text)
    {
        if (ContainsAny(text, "three artifact tokens"))
        {
            return 3;
        }

        if (ContainsAny(text, "two artifact tokens"))
        {
            return 2;
        }

        return ContainsAny(text, "an artifact token", "a artifact token", "artifact tokens") ? 1 : 0;
    }

    /// <summary>
    /// Estimates lifegain that was explicitly created by the resolved spell.
    /// </summary>
    private static int EstimateImmediateLifeGain(DeckCard spell, GoldfishTokenProduction tokenProduction)
    {
        string text = GetSnapshot(spell).OracleText ?? "";
        int life = 0;
        if (ContainsAny(text, "gain 3 life", "gain three life"))
        {
            life += 3;
        }
        else if (ContainsAny(text, "gain 2 life", "gain two life"))
        {
            life += 2;
        }
        else if (ContainsAny(text, "gain 1 life", "gain one life", "gain life"))
        {
            life += 1;
        }

        if (tokenProduction.FoodTokens > 0 && ContainsAny(text, "you gain life", "gain 3 life"))
        {
            life += tokenProduction.FoodTokens;
        }

        return Math.Clamp(life, 0, 12);
    }

    /// <summary>
    /// Estimates extra tokens produced by a cast X or token-scaling spell.
    /// </summary>
    private static int EstimateTokenScaling(DeckCard spell, int xValue)
    {
        string text = GetSnapshot(spell).OracleText ?? "";
        if (xValue > 0 && ContainsAny(text, "create X"))
        {
            return Math.Min(8, xValue);
        }

        if (ContainsAny(text, "for each creature you control", "for each token you control"))
        {
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// Estimates extra win pressure supplied by a resolved X payoff.
    /// </summary>
    private static int EstimateXSpellPressure(DeckCard spell, int xValue)
    {
        if (xValue <= 0)
        {
            return 0;
        }

        string text = GetSnapshot(spell).OracleText ?? "";
        return ContainsAny(text, "deals X", "lose X life", "get +X/+X", "gets +X/+X")
            ? Math.Min(8, Math.Max(2, xValue / 2))
            : 0;
    }

    /// <summary>
    /// Checks whether an opening hand plausibly casts the commander by the profile target turn.
    /// </summary>
    private static bool HasGoldfishCommanderPlan(
        IReadOnlyList<DeckCard> hand,
        CommandZonePlan commandZonePlan,
        SimulationProfile profile)
    {
        DeckCard? commander = commandZonePlan.PrimaryCommander;
        if (commander is null)
        {
            return false;
        }

        int lands = CountGoldfishRole(hand, DeckRoles.Lands);
        int ramp = CountCheapGoldfishRole(hand, DeckRoles.Ramp, 2);
        int targetTurn = Math.Max(1, profile.Sequencing.PreferredCommanderTurn ?? profile.Scenarios.CommanderTurn);
        int expectedLandDrops = lands >= 2 ? Math.Min(targetTurn, lands + 1) : lands;
        int expectedMana = expectedLandDrops + Math.Min(ramp, 2);
        return expectedMana >= GoldfishManaValue(commander);
    }

    /// <summary>
    /// Counts interaction or protection that could be held with available mana.
    /// </summary>
    private static int CountHeldGoldfishInteraction(IEnumerable<DeckCard> hand, int availableMana)
    {
        return hand.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return GoldfishManaValue(card) <= availableMana
                && IsGoldfishInteraction(role);
        });
    }

    /// <summary>
    /// Casts command-zone cards in plan order while mana and target turns allow.
    /// </summary>
    private static void CastGoldfishCommandZoneCards(
        CommandZoneRunState commandZone,
        int turn,
        List<DeckCard> battlefield,
        GoldfishRun run,
        int tokens,
        int artifactTokens,
        ref int availableMana)
    {
        while (true)
        {
            CommandZoneCardPlan? next = commandZone.NextPending();
            if (next is null || turn < next.TargetTurn)
            {
                return;
            }

            int cost = EstimateGoldfishCastCost(
                next.Card,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens: 0,
                availableMana,
                commanderOnline: commandZone.CommanderOnline).TotalManaSpent;
            if (cost > availableMana)
            {
                return;
            }

            availableMana -= cost;
            battlefield.Add(next.Card);
            commandZone.MarkCast(next, turn);
            run.Line.Add($"T{turn}: cast {CommandZoneLabel(next)} {next.Card.Name}.");
        }
    }

    /// <summary>
    /// Casts hand spells for one sequencing window.
    /// </summary>
    private static void CastGoldfishHandSpells(
        List<DeckCard> hand,
        List<DeckCard> deck,
        List<DeckCard> battlefield,
        List<DeckCard> graveyard,
        List<DeckCard> castThisTurn,
        GoldfishRun run,
        int turn,
        SimulationProfile profile,
        GoldfishSpellWindow window,
        bool commanderOnline,
        CommanderSpecificSimulationRules commanderRules,
        ref int restrictedCreatureMana,
        ref int availableMana,
        ref int tokens,
        ref int artifactTokens,
        ref int foodTokens,
        ref int lifeGainEvents,
        ref int winPressure,
        ref int dungeonProgress)
    {
        int orderingTokens = tokens;
        int orderingArtifactTokens = artifactTokens;
        int orderingFoodTokens = foodTokens;
        int orderingMana = availableMana;
        foreach (DeckCard spell in hand
            .OrderBy(card => CastPriority(card, turn, profile))
            .ThenBy(card => EstimateGoldfishCastCost(
                card,
                battlefield,
                orderingTokens,
                orderingArtifactTokens,
                orderingFoodTokens,
                orderingMana,
                commanderOnline).TotalManaSpent)
            .ToList())
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(spell);
            if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || IsCommanderCard(spell)
                || !UseGoldfishSpellInWindow(role, window))
            {
                continue;
            }

            GoldfishCastCost castCost = EstimateGoldfishCastCost(
                spell,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                availableMana,
                commanderOnline);
            int cost = castCost.TotalManaSpent;
            if (ShouldHoldGoldfishInteraction(spell, role, hand, availableMana, turn, profile))
            {
                continue;
            }

            bool creatureSpell = IsCreatureSpell(spell);
            int generalMana = Math.Max(0, availableMana - restrictedCreatureMana);
            if (!creatureSpell && cost > generalMana)
            {
                continue;
            }

            if (cost > availableMana)
            {
                continue;
            }

            int creatureManaSpent = 0;
            if (creatureSpell && restrictedCreatureMana > 0)
            {
                creatureManaSpent = Math.Min(cost, restrictedCreatureMana);
                restrictedCreatureMana -= creatureManaSpent;
            }

            availableMana -= cost;
            hand.Remove(spell);
            castThisTurn.Add(spell);
            if (IsPermanent(spell))
            {
                battlefield.Add(spell);
                run.Line.Add($"T{turn}: cast {spell.Name} ({role.PrimaryRole}).");
            }
            else
            {
                graveyard.Add(spell);
                run.Line.Add($"T{turn}: used {spell.Name} ({role.PrimaryRole}).");
            }

            ApplyGoldfishGraveyardSetup(spell, deck, graveyard, run, turn);

            GoldfishTokenProduction tokenProduction = EstimateTokenProduction(spell, role, castCost.XValue);
            if (tokenProduction.Total > 0)
            {
                tokens += tokenProduction.Total;
                artifactTokens += tokenProduction.ArtifactTokens;
                foodTokens += tokenProduction.FoodTokens;
            }

            lifeGainEvents += EstimateImmediateLifeGain(spell, tokenProduction);

            if (ContainsAny(GetSnapshot(spell).OracleText ?? "", "venture into the dungeon", "take the initiative"))
            {
                dungeonProgress++;
            }

            if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) && deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
            }

            if (commanderOnline
                && commanderRules.HasIngaAndEsika
                && creatureSpell
                && creatureManaSpent >= 3
                && deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
                run.Line.Add($"T{turn}: drew a card from Inga and Esika after spending {creatureManaSpent} creature mana.");
            }

            if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
            {
                winPressure += 4 + EstimateXSpellPressure(spell, castCost.XValue);
            }
        }
    }

    /// <summary>
    /// Keeps the configured minimum amount of interaction available instead of spending it proactively.
    /// </summary>
    private static bool ShouldHoldGoldfishInteraction(
        DeckCard spell,
        CardRoleAssignment role,
        IReadOnlyList<DeckCard> hand,
        int availableMana,
        int turn,
        SimulationProfile profile)
    {
        if (turn < profile.Sequencing.HoldInteractionFromTurn
            || profile.Sequencing.MinimumInteractionHeld <= 0
            || !IsGoldfishInteraction(role))
        {
            return false;
        }

        return GoldfishManaValue(spell) <= availableMana
            && CountHeldGoldfishInteraction(hand, availableMana) <= profile.Sequencing.MinimumInteractionHeld;
    }

    /// <summary>
    /// Checks whether a role assignment represents instant-speed or protective interaction for goldfish holding.
    /// </summary>
    private static bool IsGoldfishInteraction(CardRoleAssignment role)
    {
        return role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Models simple self-mill or Entomb-style setup for graveyard route predicates.
    /// </summary>
    private static void ApplyGoldfishGraveyardSetup(
        DeckCard spell,
        List<DeckCard> deck,
        List<DeckCard> graveyard,
        GoldfishRun run,
        int turn)
    {
        if (deck.Count == 0 || !SetsUpGoldfishGraveyard(spell))
        {
            return;
        }

        DeckCard target = ChooseGoldfishGraveyardTarget(deck);
        deck.Remove(target);
        graveyard.Add(target);
        run.Line.Add($"T{turn}: put {target.Name} into the graveyard for graveyard setup.");
    }

    /// <summary>
    /// Checks whether a cast spell plausibly fills the graveyard in goldfish.
    /// </summary>
    private static bool SetsUpGoldfishGraveyard(DeckCard spell)
    {
        string text = GetSnapshot(spell).OracleText ?? "";
        return ContainsAny(
            text,
            "put it into your graveyard",
            "put that card into your graveyard",
            "put a card from your library into your graveyard",
            "mill",
            "surveil");
    }

    /// <summary>
    /// Chooses the most plausible graveyard target from the remaining library.
    /// </summary>
    private static DeckCard ChooseGoldfishGraveyardTarget(List<DeckCard> deck)
    {
        DeckCard? target = deck
            .Where(IsGoldfishReanimationTarget)
            .OrderByDescending(GoldfishManaValue)
            .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return target ?? deck[0];
    }

    /// <summary>
    /// Checks whether a card is a meaningful reanimation target.
    /// </summary>
    private static bool IsGoldfishReanimationTarget(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        string text = GetSnapshot(card).OracleText ?? "";
        bool meaningfulCreature = typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase)
            && (GoldfishManaValue(card) >= 4
                || role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase));
        bool meaningfulEnchantment = typeLine.Contains("Enchantment", StringComparison.OrdinalIgnoreCase)
            && (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Payoffs, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Synergy, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
                || ContainsAny(text, "whenever", "at the beginning", "each opponent loses", "you win"));
        return meaningfulCreature || meaningfulEnchantment;
    }

    /// <summary>
    /// Checks whether a hand spell belongs in the current delayed-command-zone sequencing window.
    /// </summary>
    private static bool UseGoldfishSpellInWindow(
        CardRoleAssignment role,
        GoldfishSpellWindow window)
    {
        return window switch
        {
            GoldfishSpellWindow.All => true,
            GoldfishSpellWindow.SetupOnly => IsGoldfishSetupSpell(role),
            GoldfishSpellWindow.NonSetup => !IsGoldfishSetupSpell(role),
            _ => true,
        };
    }

    /// <summary>
    /// Checks whether a hand spell should be sequenced before delayed command-zone deployment.
    /// </summary>
    private static bool IsGoldfishSetupSpell(CardRoleAssignment role)
    {
        return role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Engines)
            || role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler);
    }

    /// <summary>
    /// Gets a human-readable command-zone role label for representative lines.
    /// </summary>
    private static string CommandZoneLabel(CommandZoneCardPlan card)
    {
        return card.Kind == CommandZoneCardKind.Background ? "background" : "commander";
    }

    /// <summary>
    /// Creates low-confidence evidence for a fallback heuristic win.
    /// </summary>
    private static SimulationRouteEvidence FallbackRouteEvidence(
        string name,
        string kind,
        string source,
        int earliestTurn,
        params string[] evidence)
    {
        List<string> evidenceLines = [];
        foreach (string line in evidence)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                evidenceLines.Add(line);
            }
        }

        return new SimulationRouteEvidence
        {
            Name = name,
            Kind = kind,
            Source = source,
            Matched = true,
            EarliestTurn = earliestTurn,
            Confidence = 0.35,
            Evidence = evidenceLines,
        };
    }

    /// <summary>
    /// Builds human-readable combat or finisher evidence without listing incidental utility creatures as closers.
    /// </summary>
    private static string[] BuildFallbackPressureEvidence(
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int power,
        int winPressure,
        int threshold,
        string route)
    {
        List<string> evidence =
        [
            $"battlefield pressure {power} met fallback {route} threshold {threshold}",
            $"token count {tokens}",
        ];
        if (winPressure > 0)
        {
            evidence.Add($"finisher pressure score {winPressure}");
        }

        AddNamedCardEvidence(evidence, "closers", battlefield.Where(IsFinisherRouteCard));
        AddNamedCardEvidence(evidence, "trample or evasion sources", battlefield.Where(IsEvasionRouteCard));
        AddNamedCardEvidence(evidence, "pump or overrun sources", battlefield.Where(IsPumpRouteCard));
        evidence.Add($"lethal pressure threshold used by this heuristic: {threshold}");
        return evidence.ToArray();
    }

    /// <summary>
    /// Adds a labeled card-name evidence row when matching cards exist.
    /// </summary>
    private static void AddNamedCardEvidence(List<string> evidence, string label, IEnumerable<DeckCard> cards)
    {
        List<string> names = cards
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (names.Count > 0)
        {
            evidence.Add($"{label}: {string.Join(", ", names)}");
        }
    }

    /// <summary>
    /// Checks whether a card should be named as a likely closer for fallback win evidence.
    /// </summary>
    private static bool IsFinisherRouteCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a card supplies combat evasion or trample-like reach.
    /// </summary>
    private static bool IsEvasionRouteCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(text, "trample", "flying", "menace", "can't be blocked", "unblockable");
    }

    /// <summary>
    /// Checks whether a card looks like an anthem, pump, or overrun effect.
    /// </summary>
    private static bool IsPumpRouteCard(DeckCard card)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        return ContainsAny(
            text,
            "creatures you control get",
            "gets +",
            "get +",
            "+1/+1",
            "+2/+2",
            "double strike",
            "until end of turn and gains trample",
            "gain trample",
            "gains trample");
    }

    /// <summary>
    /// Checks whether a card is specific enough to represent a combat route.
    /// </summary>
    private static bool IsCombatRouteCard(DeckCard card)
    {
        return IsFinisherRouteCard(card)
            || IsEvasionRouteCard(card)
            || IsPumpRouteCard(card)
            || (ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature")
                && GoldfishManaValue(card) >= 5);
    }

    /// <summary>
    /// Calculates a simple cast priority.
    /// </summary>
    private static int CastPriority(DeckCard card, int turn, SimulationProfile profile)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return CastPriorityFromRole(role, turn, profile);
    }

    /// <summary>
    /// Calculates a simple cast priority from a cached role assignment.
    /// </summary>
    private static int CastPriorityFromRole(CardRoleAssignment role, int turn, SimulationProfile profile)
    {
        if (turn <= 3 && role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            return profile.Sequencing.EarlyRampPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Engines))
        {
            return profile.Sequencing.DrawPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
        {
            return profile.Sequencing.TutorPriority;
        }

        if (role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler))
        {
            return profile.Sequencing.ComboPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
        {
            return profile.Sequencing.WinconPriority;
        }

        return profile.Sequencing.DefaultPriority;
    }

    /// <summary>
    /// Checks whether a card stays on the battlefield.
    /// </summary>
    private static bool IsPermanent(DeckCard card)
    {
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        return ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker", "Battle", "Land");
    }

    /// <summary>
    /// Checks whether a card is a creature spell for commander-specific mana rules.
    /// </summary>
    private static bool IsCreatureSpell(DeckCard card)
    {
        return ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature");
    }

    /// <summary>
    /// Checks whether the card is Sam, Loyal Attendant.
    /// </summary>
    private static bool IsSamLoyalAttendant(DeckCard card)
    {
        return card.Name.Equals("Sam, Loyal Attendant", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts battlefield mana sources.
    /// </summary>
    private static int CountManaSources(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Adds newly available Inga-granted creature mana to the restricted pool for the current turn.
    /// </summary>
    private static void RefreshIngaGrantedCreatureMana(
        IReadOnlyList<DeckCard> battlefield,
        bool commanderOnline,
        CommanderSpecificSimulationRules commanderRules,
        ref bool initialized,
        ref int availableMana,
        ref int restrictedCreatureMana)
    {
        if (initialized || !commanderOnline || !commanderRules.HasIngaAndEsika)
        {
            return;
        }

        int detectedCreatureMana = CountIngaGrantedCreatureManaSources(battlefield);
        availableMana += detectedCreatureMana;
        restrictedCreatureMana += detectedCreatureMana;
        initialized = true;
    }

    /// <summary>
    /// Counts creature permanents that become creature-spell-only mana sources from Inga and Esika.
    /// </summary>
    private static int CountIngaGrantedCreatureManaSources(IReadOnlyList<DeckCard> battlefield)
    {
        int count = 0;
        foreach (DeckCard card in battlefield)
        {
            if (!IsCreatureSpell(card))
            {
                continue;
            }

            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            if (role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Estimates battlefield power.
    /// </summary>
    private static int EstimateBattlefieldPower(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int permanentPower = 0;
        foreach (DeckCard card in battlefield)
        {
            if (!ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"))
            {
                continue;
            }

            permanentPower += Math.Max(1, (int)Math.Ceiling(GetSnapshot(card).ManaValue ?? 2));
            if (IsEvasionRouteCard(card))
            {
                permanentPower += 1;
            }
        }

        int finisherBoost = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Finishers)) * 4;
        int pumpBoost = battlefield.Where(IsPumpRouteCard).Sum(EstimatePumpPressure);
        int drainBoost = EstimateDrainPressure(battlefield, tokens);
        int commanderBoost = battlefield.Any(IsCommanderCard) ? 3 : 0;
        return permanentPower + tokens + finisherBoost + pumpBoost + drainBoost + commanderBoost;
    }

    /// <summary>
    /// Estimates a 0-100 pressure score from board power and route-specific reach.
    /// </summary>
    private static int EstimateThreatPressure(
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int lifeGainAvailable,
        int power,
        int winPressure,
        bool commanderOnline)
    {
        int evasion = battlefield.Count(IsEvasionRouteCard) * 4;
        int pump = battlefield.Where(IsPumpRouteCard).Sum(EstimatePumpPressure) * 3;
        int drain = EstimateDrainPressure(battlefield, tokens) * 4;
        int foodDrain = EstimateFoodLifegainPressure(battlefield, artifactTokens, foodTokens, lifeGainAvailable);
        int commander = EstimateCommanderPressure(battlefield, commanderOnline);
        return Math.Clamp(power * 2 + winPressure * 5 + evasion + pump + drain + foodDrain + commander, 0, 100);
    }

    /// <summary>
    /// Estimates lifegain that can still be converted from banked Food this turn.
    /// </summary>
    private static int EstimateLifeGainAvailable(
        int foodTokens,
        int lifeGainEvents,
        int availableMana,
        bool samLoyalAttendantOnline)
    {
        int activationCost = samLoyalAttendantOnline ? 1 : 2;
        int spendableFood = activationCost <= 0 ? foodTokens : Math.Min(foodTokens, Math.Max(0, availableMana) / activationCost);
        return Math.Clamp(lifeGainEvents + (spendableFood * 3), 0, 30);
    }

    /// <summary>
    /// Estimates drain pressure from banked Food, lifegain, and artifact-token death payoffs.
    /// </summary>
    private static int EstimateFoodLifegainPressure(
        IReadOnlyList<DeckCard> battlefield,
        int artifactTokens,
        int foodTokens,
        int lifeGainAvailable)
    {
        if (foodTokens == 0 && artifactTokens == 0 && lifeGainAvailable == 0)
        {
            return 0;
        }

        int pressure = 0;
        if (lifeGainAvailable >= 3 && battlefield.Any(IsLifegainDrainPayoff))
        {
            pressure += Math.Min(18, lifeGainAvailable * 2);
        }

        if ((foodTokens > 0 || artifactTokens > 0) && battlefield.Any(IsArtifactLeavesDrainPayoff))
        {
            pressure += Math.Min(18, Math.Max(foodTokens, artifactTokens) * 4);
        }

        if (foodTokens >= 3 && battlefield.Any(IsFoodCombatPayoff))
        {
            pressure += Math.Min(12, foodTokens * 2);
        }

        return pressure;
    }

    /// <summary>
    /// Separates commander presence pressure from actual commander-damage support.
    /// </summary>
    private static int EstimateCommanderPressure(IReadOnlyList<DeckCard> battlefield, bool commanderOnline)
    {
        if (!commanderOnline)
        {
            return 0;
        }

        int support = battlefield.Count(card => !IsCommanderCard(card) && IsCommanderDamageSupport(card));
        return Math.Clamp(3 + (support * 5), 0, 18);
    }

    /// <summary>
    /// Identifies pump, evasion, or Voltron text that can turn commander presence into pressure.
    /// </summary>
    private static bool IsCommanderDamageSupport(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Voltron, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(
                text,
                "equipped creature gets",
                "enchanted creature gets",
                "commander creatures you own have",
                "double strike",
                "can't be blocked",
                "unblockable",
                "trample");
    }

    /// <summary>
    /// Builds deterministic activated commander engine pressure from cached card text.
    /// </summary>
    private static ActivatedCommanderEnginePressure BuildActivatedCommanderEnginePressure(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> battlefield,
        IReadOnlyList<DeckCard> hand,
        int availableMana,
        bool commanderOnline)
    {
        DeckCard? commander = battlefield.FirstOrDefault(IsActivatedLibraryCheatCommander);
        double highCmcHitDensity = HighCmcCreatureHitDensity(workspace);
        bool topdeckSetup = battlefield.Concat(hand).Any(IsTopdeckSetupCard);
        bool libraryRevealCheat = commander is not null;
        int activationCost = commander is null ? int.MaxValue : EstimateActivationCost(GetSnapshot(commander).OracleText ?? "");
        bool activationManaAvailable = commanderOnline && libraryRevealCheat && availableMana >= activationCost;
        bool repeatableActivation = commander is not null
            && !ContainsAny(GetSnapshot(commander).OracleText ?? "", "sacrifice", "exile this", "activate only once");
        int pressure = 0;
        if (commanderOnline && libraryRevealCheat)
        {
            pressure += 25;
        }

        if (activationManaAvailable)
        {
            pressure += 25;
        }

        if (topdeckSetup)
        {
            pressure += 15;
        }

        if (repeatableActivation)
        {
            pressure += 15;
        }

        pressure += Math.Clamp((int)Math.Round(highCmcHitDensity * 20), 0, 20);

        ActivatedCommanderEnginePressure result = new()
        {
            CommanderOnline = commanderOnline && commander is not null,
            ActivationManaAvailable = activationManaAvailable,
            TopdeckSetup = topdeckSetup,
            LibraryRevealCheat = libraryRevealCheat,
            HighCmcHitDensity = Math.Round(highCmcHitDensity, 3),
            RepeatableActivation = repeatableActivation,
            Pressure = Math.Clamp(pressure, 0, 100)
        };
        if (commander is not null)
        {
            result.Evidence.Add($"{commander.Name} has activated library/topdeck cheat text in cached snapshot.");
        }

        if (activationManaAvailable)
        {
            result.Evidence.Add($"Available mana {availableMana} met estimated activation cost {activationCost}.");
        }

        if (topdeckSetup)
        {
            result.Evidence.Add("Cached battlefield or hand text contains deterministic topdeck setup language.");
        }

        if (highCmcHitDensity > 0)
        {
            result.Evidence.Add($"High-CMC creature hit density is {highCmcHitDensity:0.###}.");
        }

        return result;
    }

    /// <summary>
    /// Builds deterministic sorcery finisher pressure from cached card text.
    /// </summary>
    private static SorceryFinisherPressure BuildSorceryFinisherPressure(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<DeckCard> castThisTurn,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int availableMana,
        bool commanderOnline,
        int boardPower)
    {
        DeckCard? heldFinisher = hand.FirstOrDefault(IsSorceryFinisherCard);
        DeckCard? castFinisher = castThisTurn.LastOrDefault(IsSorceryFinisherCard);
        DeckCard? finisher = heldFinisher ?? castFinisher;
        bool held = finisher is not null;
        GoldfishCastCost? heldCost = heldFinisher is null
            ? null
            : EstimateGoldfishCastCost(
                heldFinisher,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                availableMana,
                commanderOnline);
        bool castable = castFinisher is not null
            || (heldCost is not null && heldCost.TotalManaSpent <= availableMana);
        int projectedDamage = castable
            ? EstimateProjectedFinisherDamage(finisher!, boardPower)
            : boardPower;
        int pressure = castable && boardPower >= 6
            ? Math.Clamp(projectedDamage * 3, 0, 100)
            : 0;
        SorceryFinisherPressure result = new()
        {
            SorceryFinisherHeld = held,
            CastableFinisher = castable,
            BoardPowerBeforeFinisher = boardPower,
            ProjectedDamage = Math.Clamp(projectedDamage, 0, 200),
            Pressure = pressure
        };
        if (heldFinisher is not null)
        {
            result.Evidence.Add($"{heldFinisher.Name} matched deterministic sorcery finisher text in hand.");
        }

        if (castFinisher is not null)
        {
            result.Evidence.Add($"{castFinisher.Name} was cast this turn and matched deterministic sorcery finisher text.");
        }

        if (castable)
        {
            string costText = heldCost is null
                ? "the finisher was already cast"
                : $"effective cost {heldCost.TotalManaSpent}";
            result.Evidence.Add($"Available mana {availableMana} can support {costText}.");
        }

        if (pressure > 0)
        {
            result.Evidence.Add($"Projected damage pressure {projectedDamage} from board power {boardPower}.");
        }

        return result;
    }

    /// <summary>
    /// Identifies commanders with activated library/topdeck cheat text.
    /// </summary>
    private static bool IsActivatedLibraryCheatCommander(DeckCard card)
    {
        if (!IsCommanderCard(card))
        {
            return false;
        }

        string text = GetSnapshot(card).OracleText ?? "";
        return text.Contains(':', StringComparison.Ordinal)
            && ContainsAny(text, "top", "library", "reveal")
            && ContainsAny(text, "put", "battlefield", "cast");
    }

    /// <summary>
    /// Identifies deterministic topdeck setup text in cached snapshots.
    /// </summary>
    private static bool IsTopdeckSetupCard(DeckCard card)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "scry", "surveil", "look at the top", "rearrange", "put on top", "put that card on top");
    }

    /// <summary>
    /// Estimates high-CMC creature density among included non-commander cards.
    /// </summary>
    private static double HighCmcCreatureHitDensity(DeckWorkspace workspace)
    {
        int creatures = 0;
        int highCmcCreatures = 0;
        foreach (DeckCard card in IncludedCards(workspace).Where(card => !IsCommanderCard(card)))
        {
            if (!ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"))
            {
                continue;
            }

            creatures += Math.Max(0, card.Quantity);
            if ((GetSnapshot(card).ManaValue ?? 0) >= 5)
            {
                highCmcCreatures += Math.Max(0, card.Quantity);
            }
        }

        return creatures == 0 ? 0 : highCmcCreatures / (double)creatures;
    }

    /// <summary>
    /// Estimates the first activated ability mana cost from mana symbols before a colon.
    /// </summary>
    private static int EstimateActivationCost(string text)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return 0;
        }

        string costText = text[..colon];
        int cost = 0;
        for (int index = 0; index < costText.Length; index++)
        {
            if (costText[index] != '{')
            {
                continue;
            }

            int close = costText.IndexOf('}', index + 1);
            if (close < 0)
            {
                break;
            }

            string symbol = costText[(index + 1)..close];
            if (int.TryParse(symbol, out int generic))
            {
                cost += generic;
            }
            else if (!symbol.Equals("T", StringComparison.OrdinalIgnoreCase)
                && !symbol.Equals("Q", StringComparison.OrdinalIgnoreCase))
            {
                cost += 1;
            }

            index = close;
        }

        return Math.Max(0, cost);
    }

    /// <summary>
    /// Identifies sorceries that convert a board into immediate combat or draw pressure.
    /// </summary>
    private static bool IsSorceryFinisherCard(DeckCard card)
    {
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        string text = GetSnapshot(card).OracleText ?? "";
        return typeLine.Contains("Sorcery", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "creatures you control", "target creatures", "additional combat", "extra combat", "draw cards equal to")
            && ContainsAny(text, "+x/+x", "+1/+1", "+2/+2", "+3/+3", "trample", "additional combat", "extra combat", "greatest power", "power among creatures");
    }

    /// <summary>
    /// Estimates bounded damage pressure after resolving a sorcery finisher.
    /// </summary>
    private static int EstimateProjectedFinisherDamage(DeckCard finisher, int boardPower)
    {
        string text = GetSnapshot(finisher).OracleText ?? "";
        int damage = boardPower;
        if (ContainsAny(text, "+x/+x", "greatest power", "power among creatures"))
        {
            damage += boardPower;
        }
        else if (ContainsAny(text, "+3/+3"))
        {
            damage += 9;
        }
        else if (ContainsAny(text, "+2/+2"))
        {
            damage += 6;
        }
        else if (ContainsAny(text, "+1/+1"))
        {
            damage += 3;
        }

        if (ContainsAny(text, "additional combat", "extra combat"))
        {
            damage *= 2;
        }

        if (ContainsAny(text, "trample"))
        {
            damage += Math.Max(2, boardPower / 3);
        }

        return damage;
    }

    /// <summary>
    /// Estimates how much a pump, equipment, aura, or anthem permanent increases pressure.
    /// </summary>
    private static int EstimatePumpPressure(DeckCard card)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        int pressure = 0;
        if (ContainsAny(typeLine, "Equipment", "Aura"))
        {
            pressure += 2;
        }

        if (ContainsAny(text, "+1/+1"))
        {
            pressure += 2;
        }

        if (ContainsAny(text, "+2/+2"))
        {
            pressure += 4;
        }

        if (ContainsAny(text, "+3/+3"))
        {
            pressure += 6;
        }

        if (ContainsAny(text, "double strike"))
        {
            pressure += 4;
        }

        if (ContainsAny(text, "trample", "flying", "menace", "can't be blocked", "unblockable"))
        {
            pressure += 2;
        }

        return Math.Max(1, pressure);
    }

    /// <summary>
    /// Estimates recurring life-loss pressure from aristocrats and drain boards.
    /// </summary>
    private static int EstimateDrainPressure(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int drainPayoffs = battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Aristocrats, StringComparer.OrdinalIgnoreCase);
        });
        if (drainPayoffs == 0)
        {
            return 0;
        }

        int sacrificeSupport = battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.Tags.Contains(DeckTags.SacOutlet, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.SacrificeFodder, StringComparer.OrdinalIgnoreCase);
        });
        return drainPayoffs * Math.Max(1, Math.Min(4, tokens + sacrificeSupport));
    }

    /// <summary>
    /// Identifies payoffs that turn lifegain into opponent life loss or win pressure.
    /// </summary>
    private static bool IsLifegainDrainPayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
            || (ContainsAny(text, "whenever you gain life", "whenever you gained life")
                && ContainsAny(text, "each opponent loses", "opponent loses", "loses that much life", "you win the game"));
    }

    /// <summary>
    /// Identifies payoffs for sacrificing or losing artifact tokens such as Food.
    /// </summary>
    private static bool IsArtifactLeavesDrainPayoff(DeckCard card)
    {
        string text = GetSnapshot(card).OracleText ?? "";
        return ContainsAny(
                text,
                "whenever an artifact is put into a graveyard",
                "whenever one or more artifacts",
                "whenever you sacrifice an artifact",
                "whenever you sacrifice a food",
                "whenever one or more tokens you control leave")
            && ContainsAny(text, "each opponent loses", "opponent loses", "damage to each opponent", "drain");
    }

    /// <summary>
    /// Identifies combat payoffs that can convert a banked Food/token board into an alpha strike.
    /// </summary>
    private static bool IsFoodCombatPayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.CombatPayoff, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(
                text,
                "creatures you control get +",
                "creatures you control gain trample",
                "creatures you control have trample",
                "creatures you control can't be blocked",
                "for each artifact you control",
                "for each food you control");
    }

    /// <summary>
    /// Checks whether the battlefield has a repeatable engine permanent online.
    /// </summary>
    private static bool HasGoldfishEngineOnline(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Any(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            string text = GetSnapshot(card).OracleText ?? "";
            return role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                || (ContainsAny(text, "whenever", "at the beginning")
                    && ContainsAny(text, "draw", "create", "return", "each opponent loses", "opponent loses"));
        });
    }

    /// <summary>
    /// Adds 0-100 summary metrics that distinguish board shape from detected kill confidence.
    /// </summary>
    private static void AddGoldfishSummaryMetrics(
        GoldfishSimulationResult result,
        IReadOnlyList<GoldfishRun> runs,
        int targetTurn)
    {
        ProjectedTurnState target = result.TurnSummaries.FirstOrDefault(summary => summary.Turn == targetTurn)
            ?? result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState();
        result.BoardDevelopmentScore = Math.Clamp(
            (target.MedianLands * 8)
                + (target.MedianManaSources * 4)
                + (target.MedianNonlandPermanents * 8)
                + (Math.Min(target.MedianCardsInHand, 7) * 3)
                + (target.MedianTokens * 3),
            0,
            100);

        List<GoldfishTurnSnapshot> targetSnapshots = runs
            .SelectMany(run => run.Turns.Where(snapshot => snapshot.Turn == targetTurn))
            .ToList();
        int medianThreat = Median(targetSnapshots.Select(snapshot => snapshot.ThreatPressure));
        result.PressureOnlyProgress = Math.Clamp(medianThreat, 0, 100);
        double routePressure = result.WinEstimate.Routes.Count == 0
            ? 0
            : result.WinEstimate.Routes.Max(route => route.Probability) * 100;
        double turnWinRate = result.WinEstimate.WinByTurnRates.TryGetValue(targetTurn, out double rate)
            ? rate * 100
            : 0;
        result.ThreatPressure = Math.Clamp(
            (int)Math.Round(Math.Max(medianThreat, Math.Max(routePressure, turnWinRate))),
            0,
            100);

        result.EngineOnlineRate = targetSnapshots.Count == 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(targetSnapshots.Count(snapshot => snapshot.EngineOnline) * 100.0 / targetSnapshots.Count),
                0,
                100);
        result.EnginePressure = BuildEnginePressureSummary(targetSnapshots);
        result.SorceryFinisherPressure = BuildSorceryFinisherPressureSummary(targetSnapshots);

        double confidence = result.WinEstimate.RouteEvidence.Count == 0
            ? 0
            : result.WinEstimate.RouteEvidence.Max(evidence => evidence.Confidence) * 100;
        result.WinDetectionConfidence = Math.Clamp((int)Math.Round(confidence), 0, 100);
        result.LethalConfidence = Math.Clamp(
            (int)Math.Round(result.WinEstimate.ObservedWinRate * confidence),
            0,
            100);
        result.WinEstimate.PressureOnlyProgress = result.PressureOnlyProgress;
        result.WinEstimate.LethalConfidence = result.LethalConfidence;
        result.Notes.Add(
            "Summary metrics use 0-100 scales: boardDevelopmentScore measures board shape, "
                + "threatPressure measures combat/drain/route pressure, engineOnlineRate measures repeatable engines, "
                + "pressureOnlyProgress is non-lethal pressure, and lethalConfidence combines win rate with route evidence.");
    }

    /// <summary>
    /// Builds a target-turn activated commander engine summary.
    /// </summary>
    private static ActivatedCommanderEnginePressure BuildEnginePressureSummary(IReadOnlyList<GoldfishTurnSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new ActivatedCommanderEnginePressure();
        }

        ActivatedCommanderEnginePressure strongest = snapshots
            .Select(snapshot => snapshot.EnginePressure)
            .OrderByDescending(pressure => pressure.Pressure)
            .FirstOrDefault()
            ?? new ActivatedCommanderEnginePressure();
        return new ActivatedCommanderEnginePressure
        {
            CommanderOnline = snapshots.Any(snapshot => snapshot.EnginePressure.CommanderOnline),
            ActivationManaAvailable = snapshots.Any(snapshot => snapshot.EnginePressure.ActivationManaAvailable),
            TopdeckSetup = snapshots.Any(snapshot => snapshot.EnginePressure.TopdeckSetup),
            LibraryRevealCheat = snapshots.Any(snapshot => snapshot.EnginePressure.LibraryRevealCheat),
            HighCmcHitDensity = Math.Round(snapshots.Select(snapshot => snapshot.EnginePressure.HighCmcHitDensity).DefaultIfEmpty(0).Average(), 3),
            RepeatableActivation = snapshots.Any(snapshot => snapshot.EnginePressure.RepeatableActivation),
            Pressure = Median(snapshots.Select(snapshot => snapshot.EnginePressure.Pressure)),
            Evidence = strongest.Evidence.Take(6).ToList()
        };
    }

    /// <summary>
    /// Builds a target-turn sorcery finisher pressure summary.
    /// </summary>
    private static SorceryFinisherPressure BuildSorceryFinisherPressureSummary(IReadOnlyList<GoldfishTurnSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new SorceryFinisherPressure();
        }

        SorceryFinisherPressure strongest = snapshots
            .Select(snapshot => snapshot.SorceryFinisherPressure)
            .OrderByDescending(pressure => pressure.Pressure)
            .FirstOrDefault()
            ?? new SorceryFinisherPressure();
        return new SorceryFinisherPressure
        {
            SorceryFinisherHeld = snapshots.Any(snapshot => snapshot.SorceryFinisherPressure.SorceryFinisherHeld),
            CastableFinisher = snapshots.Any(snapshot => snapshot.SorceryFinisherPressure.CastableFinisher),
            BoardPowerBeforeFinisher = Median(snapshots.Select(snapshot => snapshot.SorceryFinisherPressure.BoardPowerBeforeFinisher)),
            ProjectedDamage = Median(snapshots.Select(snapshot => snapshot.SorceryFinisherPressure.ProjectedDamage)),
            Pressure = strongest.Pressure,
            Evidence = strongest.Evidence.Take(6).ToList()
        };
    }

    /// <summary>
    /// Builds one projected turn summary.
    /// </summary>
    private static ProjectedTurnState BuildProjectedTurnState(int turn, IReadOnlyList<GoldfishRun> runs)
    {
        List<GoldfishTurnSnapshot> snapshots = runs.SelectMany(run => run.Turns.Where(snapshot => snapshot.Turn == turn)).ToList();
        int lands = Median(snapshots.Select(snapshot => snapshot.Lands));
        int manaSources = Median(snapshots.Select(snapshot => snapshot.ManaSources));
        int permanents = Median(snapshots.Select(snapshot => snapshot.NonlandPermanents));
        int hand = Median(snapshots.Select(snapshot => snapshot.CardsInHand));
        int power = Median(snapshots.Select(snapshot => snapshot.Power));
        int tokens = Median(snapshots.Select(snapshot => snapshot.Tokens));
        return new ProjectedTurnState
        {
            Turn = turn,
            ModelLabel = BoardProjectionModelLabel,
            MedianLands = lands,
            MedianManaSources = manaSources,
            MedianNonlandPermanents = permanents,
            MedianCardsInHand = hand,
            MedianPower = power,
            MedianTokens = tokens,
            LikelyBoard = $"{lands} lands, {manaSources} mana sources, {permanents} nonland permanents, about {power} pressure, {hand} cards in hand.",
            Confidence = Math.Clamp(0.45 + Math.Min(0.35, runs.Count / 2000.0), 0, 0.85),
            Notes =
            [
                "Model label heuristic-board-projection: derived from optimistic goldfish runs and intended for board-state shape, not strict castability proof.",
                "Opponent interaction and full Magic rules are not simulated.",
            ],
        };
    }

    /// <summary>
    /// Builds command-zone timing metrics from goldfish runs.
    /// </summary>
    private static CommandZonePerformance BuildCommandZonePerformance(
        IReadOnlyList<GoldfishRun> runs,
        int maxTurn,
        CommandZonePlan plan)
    {
        CommandZonePerformance result = new()
        {
            CommandZoneNames = plan.Cards.Select(card => card.Card.Name).ToList(),
            CommanderNames = plan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Commander)
                .Select(card => card.Card.Name)
                .ToList(),
            AverageCommanderCastTurn = AverageTurn(runs.Select(run => run.CommanderCastTurn)),
        };
        if (plan.HasBackgroundPair)
        {
            result.BackgroundNames = plan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Background)
                .Select(card => card.Card.Name)
                .ToList();
            result.AverageBackgroundCastTurn = AverageTurn(runs.Select(run => run.BackgroundCastTurn));
            result.AverageCommanderWithBackgroundOnlineTurn = AverageTurn(runs.Select(run => run.CommanderWithBackgroundOnlineTurn));
        }

        if (plan.Cards.Count == 0)
        {
            return result;
        }

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.CommanderCastByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-cast-by-turn",
                turn,
                runs.Count(run => run.CommanderCastTurn <= turn),
                runs.Count));
            if (plan.HasBackgroundPair)
            {
                result.BackgroundCastByTurn.Add(PerformanceStatistics.BuildProbability(
                    "background-cast-by-turn",
                    turn,
                    runs.Count(run => run.BackgroundCastTurn <= turn),
                    runs.Count));
                result.CommanderWithBackgroundOnlineByTurn.Add(PerformanceStatistics.BuildProbability(
                    "commander-with-background-online-by-turn",
                    turn,
                    runs.Count(run => run.CommanderWithBackgroundOnlineTurn <= turn),
                    runs.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// Averages observed turn values while ignoring runs where the event did not occur.
    /// </summary>
    private static double? AverageTurn(IEnumerable<int?> turns)
    {
        List<int> observed = turns
            .Where(turn => turn.HasValue)
            .Select(turn => turn!.Value)
            .ToList();
        return observed.Count == 0 ? null : observed.Average();
    }

    /// <summary>
    /// Builds a win-turn estimate from goldfish runs.
    /// </summary>
    private static WinTurnEstimate BuildWinEstimate(DeckWorkspace workspace, IReadOnlyList<GoldfishRun> runs, int maxTurn)
    {
        List<int> wins = runs.Where(run => run.WinTurn.HasValue).Select(run => run.WinTurn!.Value).Order().ToList();
        WinTurnEstimate estimate = new()
        {
            WorkspaceId = workspace.Id,
            ModelLabel = GoldfishModelLabel,
            Simulations = runs.Count,
            ObservedWins = wins.Count,
            ObservedWinRate = runs.Count == 0 ? 0 : wins.Count / (double)runs.Count,
            MedianObservedWinTurn = Percentile(wins, 0.50),
            P25ObservedWinTurn = Percentile(wins, 0.25),
            P75ObservedWinTurn = Percentile(wins, 0.75)
        };
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            estimate.WinByTurnRates[turn] = runs.Count == 0 ? 0 : runs.Count(run => run.WinTurn <= turn) / (double)runs.Count;
        }

        foreach (IGrouping<string, GoldfishRun> route in runs.Where(run => run.WinRoute is not null).GroupBy(run => run.WinRoute!))
        {
            List<SimulationRouteEvidence> evidence = route
                .SelectMany(run => run.RouteEvidence)
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5)
                .ToList();
            estimate.Routes.Add(new WinRoute
            {
                Name = route.Key,
                Kind = route.Key,
                EarliestTurn = route.Min(run => run.WinTurn),
                Probability = route.Count() / (double)runs.Count,
                Cards = RouteCards(workspace, route.Key),
                Rationale = BuildRouteRationale(route.Key, evidence),
                Evidence = evidence,
            });
        }

        estimate.RouteEvidence = runs
            .SelectMany(run => run.RouteEvidence)
            .GroupBy(item => $"{item.Source}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();
        estimate.PressureOnlyProgress = EstimateFallbackPressureProgress(estimate.Routes);
        estimate.LethalConfidence = EstimateLethalConfidence(estimate);

        if (estimate.MedianObservedWinTurn is null)
        {
            estimate.Notes.Add($"No likely win was found by turn {maxTurn} in the goldfish runs.");
        }

        if (BuildPartialCommanderDeckWarning(workspace) is string partialDeckWarning)
        {
            estimate.Notes.Add(partialDeckWarning);
        }

        estimate.Notes.Add("Win timing is probabilistic and assumes no interaction.");
        estimate.Notes.Add(
            "Model label optimistic-goldfish-model: route evidence combines deterministic route predicates "
                + "with fallback board-pressure heuristics.");
        estimate.Notes.Add(
            "deck_analyze_performance can report different timing because it uses strict-sequencing-model "
                + "scenario probabilities instead of heuristic win-pressure detection.");
        estimate.Notes.Add(
            "Observed win-turn percentiles only include runs that reached a heuristic win; winByTurnRates "
                + "and observedWinRate are measured against all runs.");
        estimate.Notes.Add("Pressure-only progress is reported separately from lethal confidence.");
        return estimate;
    }

    /// <summary>
    /// Estimates how much of the win estimate came from fallback pressure instead of deterministic routes.
    /// </summary>
    private static int EstimateFallbackPressureProgress(IReadOnlyList<WinRoute> routes)
    {
        double progress = 0;
        foreach (WinRoute route in routes)
        {
            bool fallbackOnly = route.Evidence.Count > 0
                && route.Evidence.All(evidence => evidence.Source.Equals("fallback", StringComparison.OrdinalIgnoreCase));
            if (fallbackOnly)
            {
                progress = Math.Max(progress, route.Probability * 100);
            }
        }

        return Math.Clamp((int)Math.Round(progress), 0, 100);
    }

    /// <summary>
    /// Combines observed win rate with the strongest route evidence confidence.
    /// </summary>
    private static int EstimateLethalConfidence(WinTurnEstimate estimate)
    {
        double confidence = estimate.RouteEvidence.Count == 0
            ? 0
            : estimate.RouteEvidence.Max(evidence => evidence.Confidence) * 100;
        return Math.Clamp((int)Math.Round(estimate.ObservedWinRate * confidence), 0, 100);
    }

    /// <summary>
    /// Labels deterministic route evidence separately from fallback heuristic pressure.
    /// </summary>
    private static string BuildRouteRationale(string route, IReadOnlyList<SimulationRouteEvidence> evidence)
    {
        bool deterministicEvidence = evidence.Any(item => !item.Source.Equals("fallback", StringComparison.OrdinalIgnoreCase));
        if (deterministicEvidence)
        {
            return $"The simulator found {route} through deterministic route evidence.";
        }

        if (evidence.Count > 0)
        {
            return $"The simulator found {route} through fallback heuristic pressure.";
        }

        return $"The simulator found {route} through fallback pressure heuristics.";
    }

    /// <summary>
    /// Gets representative cards for a win route.
    /// </summary>
    private static List<string> RouteCards(DeckWorkspace workspace, string route)
    {
        return IncludedCards(workspace)
            .Where(card =>
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                return route switch
                {
                    "combo" => role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler),
                    "finisher" => role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers),
                    "combat" => IsCombatRouteCard(card),
                    _ => false
                };
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Calculates an integer median.
    /// </summary>
    private static int Median(IEnumerable<int> values)
    {
        List<int> sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>
    /// Calculates a percentile turn.
    /// </summary>
    private static int? Percentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp((int)Math.Round((sortedValues.Count - 1) * percentile), 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    /// <summary>
    /// Stores one goldfish run.
    /// </summary>
    private sealed class GoldfishRun
    {
        /// <summary>
        /// Gets or sets whether the run mulliganed.
        /// </summary>
        public bool Mulliganed { get; set; }

        /// <summary>
        /// Gets or sets the win turn.
        /// </summary>
        public int? WinTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest non-Background commander cast turn.
        /// </summary>
        public int? CommanderCastTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest Background cast turn.
        /// </summary>
        public int? BackgroundCastTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest turn where commander and Background were both online.
        /// </summary>
        public int? CommanderWithBackgroundOnlineTurn { get; set; }

        /// <summary>
        /// Gets or sets the win route.
        /// </summary>
        public string? WinRoute { get; set; }

        /// <summary>
        /// Gets or sets turn snapshots.
        /// </summary>
        public List<GoldfishTurnSnapshot> Turns { get; set; } = [];

        /// <summary>
        /// Gets or sets the representative line.
        /// </summary>
        public List<string> Line { get; set; } = [];

        /// <summary>
        /// Gets or sets deterministic route evidence captured during the run.
        /// </summary>
        public List<SimulationRouteEvidence> RouteEvidence { get; set; } = [];
    }

    /// <summary>
    /// Stores a goldfish opening hand after mulligans.
    /// </summary>
    private sealed class GoldfishOpeningHand
    {
        /// <summary>
        /// Gets or sets the kept hand.
        /// </summary>
        public List<DeckCard> Hand { get; set; } = [];

        /// <summary>
        /// Gets or sets the remaining library.
        /// </summary>
        public List<DeckCard> Library { get; set; } = [];

        /// <summary>
        /// Gets or sets how many mulligans were taken.
        /// </summary>
        public int Mulligans { get; set; }
    }

    /// <summary>
    /// Stores one simulated turn snapshot.
    /// </summary>
    private sealed class GoldfishTurnSnapshot
    {
        /// <summary>
        /// Gets or sets the turn number.
        /// </summary>
        public int Turn { get; set; }

        /// <summary>
        /// Gets or sets lands in play.
        /// </summary>
        public int Lands { get; set; }

        /// <summary>
        /// Gets or sets mana sources in play.
        /// </summary>
        public int ManaSources { get; set; }

        /// <summary>
        /// Gets or sets nonland permanents in play.
        /// </summary>
        public int NonlandPermanents { get; set; }

        /// <summary>
        /// Gets or sets cards in hand.
        /// </summary>
        public int CardsInHand { get; set; }

        /// <summary>
        /// Gets or sets battlefield power.
        /// </summary>
        public int Power { get; set; }

        /// <summary>
        /// Gets or sets token count.
        /// </summary>
        public int Tokens { get; set; }

        /// <summary>
        /// Gets or sets the bounded threat-pressure score for this turn.
        /// </summary>
        public int ThreatPressure { get; set; }

        /// <summary>
        /// Gets or sets whether a repeatable engine appeared online by this turn.
        /// </summary>
        public bool EngineOnline { get; set; }

        /// <summary>
        /// Gets activated commander engine pressure evidence for this turn.
        /// </summary>
        public ActivatedCommanderEnginePressure EnginePressure { get; set; } = new();

        /// <summary>
        /// Gets sorcery finisher pressure evidence for this turn.
        /// </summary>
        public SorceryFinisherPressure SorceryFinisherPressure { get; set; } = new();

        /// <summary>
        /// Gets or sets whether a non-Background commander had been cast by this turn.
        /// </summary>
        public bool CommanderCastByTurn { get; set; }

        /// <summary>
        /// Gets or sets whether a Background had been cast by this turn.
        /// </summary>
        public bool BackgroundCastByTurn { get; set; }

        /// <summary>
        /// Gets or sets whether a commander and Background were both online by this turn.
        /// </summary>
        public bool CommanderWithBackgroundOnlineByTurn { get; set; }
    }

    /// <summary>
    /// Carries the bounded cast-cost estimate used by one goldfish spell cast.
    /// </summary>
    private sealed record GoldfishCastCost(int RequiredMana, int XValue)
    {
        /// <summary>
        /// Total mana the sequencer spends, including chosen X value.
        /// </summary>
        public int TotalManaSpent => RequiredMana + XValue;
    }

    /// <summary>
    /// Carries token subcounts from one resolved spell.
    /// </summary>
    private sealed record GoldfishTokenProduction(int Total, int ArtifactTokens, int FoodTokens);

    /// <summary>
    /// Lists hand-spell sequencing windows around delayed command-zone deployment.
    /// </summary>
    private enum GoldfishSpellWindow
    {
        /// <summary>
        /// Cast every eligible spell.
        /// </summary>
        All,

        /// <summary>
        /// Cast only setup spells before delayed command-zone deployment.
        /// </summary>
        SetupOnly,

        /// <summary>
        /// Cast only non-setup spells after delayed command-zone deployment.
        /// </summary>
        NonSetup,
    }
}
