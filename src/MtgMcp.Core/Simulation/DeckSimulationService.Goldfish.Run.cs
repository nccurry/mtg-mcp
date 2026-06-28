namespace MtgMcp.Core;

/// <summary>
/// Contains the goldfish game loop, library, and opening-hand helpers.
/// </summary>
public sealed partial class DeckSimulationService
{
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
        DeterministicSimulationRandom random = new(seed);
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
        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace).Where(card => !IsCommanderCard(card)))
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
    private static void Shuffle(List<DeckCard> cards, DeterministicSimulationRandom random)
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
        DeterministicSimulationRandom random,
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
        int includedCount = DeckServiceHelpers.IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity));
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
        return Math.Max(0, (int)Math.Ceiling(DeckServiceHelpers.GetSnapshot(card).ManaValue ?? 2));
    }
}
