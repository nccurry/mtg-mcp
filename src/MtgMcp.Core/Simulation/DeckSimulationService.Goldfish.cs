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
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return SimulateGoldfish(workspace, targetTurn, simulations, seed, mulligan, simulationProfiles);
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
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
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
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
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
            .Resolve(workspace, SimulationProfileIds.Auto, intent);
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

        IEnumerable<GoldfishRun> representativeCandidates = runs;
        if (commandZonePlan.HasBackground && runs.Any(run => run.CommanderWithBackgroundOnlineTurn.HasValue))
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
            if (profileResolution.Profile.Sequencing.PreferCommanderOnCurve)
            {
                CastGoldfishCommandZoneCards(commandZone, turn, battlefield, run, ref availableMana);
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
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.All,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
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
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.SetupOnly,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
                    ref winPressure,
                    ref dungeonProgress);
                CastGoldfishCommandZoneCards(commandZone, turn, battlefield, run, ref availableMana);
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
                    run,
                    turn,
                    profileResolution.Profile,
                    GoldfishSpellWindow.NonSetup,
                    commandZone.CommanderOnline,
                    commanderRules,
                    ref restrictedCreatureMana,
                    ref availableMana,
                    ref tokens,
                    ref winPressure,
                    ref dungeonProgress);
            }

            int power = EstimateBattlefieldPower(battlefield, tokens);
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
                && (role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
                    || role.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase)
                    || role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase));
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
        ref int availableMana)
    {
        while (true)
        {
            CommandZoneCardPlan? next = commandZone.NextPending();
            if (next is null || turn < next.TargetTurn)
            {
                return;
            }

            int cost = GoldfishManaValue(next.Card);
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
        GoldfishRun run,
        int turn,
        SimulationProfile profile,
        GoldfishSpellWindow window,
        bool commanderOnline,
        CommanderSpecificSimulationRules commanderRules,
        ref int restrictedCreatureMana,
        ref int availableMana,
        ref int tokens,
        ref int winPressure,
        ref int dungeonProgress)
    {
        foreach (DeckCard spell in hand.OrderBy(card => CastPriority(card, turn, profile)).ThenBy(card => GetSnapshot(card).ManaValue ?? 0).ToList())
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(spell);
            if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || IsCommanderCard(spell)
                || !UseGoldfishSpellInWindow(role, window))
            {
                continue;
            }

            int cost = Math.Max(0, (int)Math.Ceiling(GetSnapshot(spell).ManaValue ?? 2));
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

            if (role.Tags.Contains(DeckTags.Tokens) || role.Tags.Contains(DeckTags.SacrificeFodder))
            {
                tokens += 2;
            }

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
                winPressure += 4;
            }
        }
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
        int permanentPower = battlefield
            .Where(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"))
            .Sum(card => Math.Max(1, (int)Math.Ceiling(GetSnapshot(card).ManaValue ?? 2)));
        int finisherBoost = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Finishers)) * 4;
        return permanentPower + tokens + finisherBoost;
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
            BackgroundNames = plan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Background)
                .Select(card => card.Card.Name)
                .ToList(),
            AverageCommanderCastTurn = AverageTurn(runs.Select(run => run.CommanderCastTurn)),
            AverageBackgroundCastTurn = AverageTurn(runs.Select(run => run.BackgroundCastTurn)),
            AverageCommanderWithBackgroundOnlineTurn = AverageTurn(runs.Select(run => run.CommanderWithBackgroundOnlineTurn)),
        };

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
                Rationale = evidence.Count > 0
                    ? $"The simulator found {route.Key} through deterministic route evidence."
                    : $"The simulator found {route.Key} through fallback pressure heuristics.",
                Evidence = evidence,
            });
        }

        estimate.RouteEvidence = runs
            .SelectMany(run => run.RouteEvidence)
            .GroupBy(item => $"{item.Source}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();

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
        return estimate;
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
