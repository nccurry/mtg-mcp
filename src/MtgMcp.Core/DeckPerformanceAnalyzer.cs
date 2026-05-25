namespace MtgMcp.Core;

/// <summary>
/// Analyzes deck performance without repository, MCP, or backend concerns.
/// </summary>
internal static class DeckPerformanceAnalyzer
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
        int deckSize = included.Sum(card => Math.Max(0, card.Quantity));
        (bool colorIdentityKnown, HashSet<string> deckColors) = GetDeckColorIdentity(workspace);
        List<PerformanceRun> runs = [];

        for (int index = 0; index < safeSimulations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.Add(RunPerformanceGame(
                workspace,
                included,
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
            Commander = BuildCommanderPerformance(included, runs, safeMaxTurn),
            ComboAssembly = BuildComboAssemblyPerformance(included, runs, safeMaxTurn),
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
            intent);
        AddPerformanceNotes(analysis, workspace, included, colorIdentityKnown, profileResolution, intent);
        analysis.Warnings.AddRange(profileResolution.Warnings);
        return analysis;
    }

    /// <summary>
    /// Builds the deck-level context used by opening-hand mulligan decisions.
    /// </summary>
    private static PerformanceMulliganContext BuildPerformanceMulliganContext(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> included,
        IReadOnlySet<string> deckColors,
        SimulationProfile profile)
    {
        return new PerformanceMulliganContext
        {
            FreeFirstMulligan = UsesFreeFirstMulligan(workspace.Format),
            DeckColors = new HashSet<string>(deckColors, StringComparer.OrdinalIgnoreCase),
            Commander = included.FirstOrDefault(IsCommanderCard),
            Mulligan = profile.Mulligan,
        };
    }

    /// <summary>
    /// Simulates one heuristic game from opening hand through the target turn.
    /// </summary>
    private static PerformanceRun RunPerformanceGame(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> included,
        IReadOnlySet<string> deckColors,
        int maxTurn,
        int seed,
        bool includeMulligans,
        SimulationProfile profile)
    {
        Random random = new(seed);
        PerformanceMulliganContext mulliganContext = BuildPerformanceMulliganContext(
            workspace,
            included,
            deckColors,
            profile);
        PerformanceOpeningHand opening = DrawPerformanceOpeningHand(
            workspace,
            random,
            includeMulligans,
            mulliganContext);
        List<DeckCard> hand = opening.Hand;
        List<DeckCard> library = opening.Library;
        List<PerformancePermanent> battlefield = [];
        List<DeckCard> graveyard = [];
        List<IReadOnlyList<string>> virtualManaSources = [];
        DeckCard? commander = included.FirstOrDefault(IsCommanderCard);
        PerformanceRun run = new()
        {
            Mulligans = opening.Mulligans,
            KeptHandSize = opening.Hand.Count,
            KeptOpeningLands = CountPerformanceRole(opening.Hand, DeckRoles.Lands),
            OpeningSevenLands = opening.OpeningSevenLands,
        };
        bool rampCastByTurn = false;
        bool drawCastByTurn = false;
        bool commanderCast = false;

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            if (library.Count > 0)
            {
                PerformanceDrawOne(hand, library);
            }

            DeckCard? landPlayed = ChoosePerformanceLand(hand, battlefield, virtualManaSources, deckColors);
            PerformancePermanent? unavailablePermanent = null;
            if (landPlayed is not null)
            {
                hand.Remove(landPlayed);
                PerformancePermanent permanent = new() { Card = landPlayed };
                battlefield.Add(permanent);
                if (PerformanceMana.LooksTapped(PerformanceMana.GetSnapshot(landPlayed)))
                {
                    unavailablePermanent = permanent;
                }
            }

            List<PerformanceManaSource> availableSources = GetPerformanceManaSources(
                battlefield,
                virtualManaSources,
                unavailablePermanent);
            List<PerformanceManaSource> turnStartSources = availableSources.ToList();
            int totalManaSources = GetPerformanceManaSources(
                    battlefield,
                    virtualManaSources,
                    unavailablePermanent: null)
                .Count;
            PerformanceTurnState state = new()
            {
                Turn = turn,
                LandsInPlay = CountPerformanceRole(
                    battlefield.Select(permanent => permanent.Card),
                    DeckRoles.Lands),
                ManaSources = totalManaSources,
                AvailableMana = availableSources.Count,
                ColorSources = new HashSet<string>(
                    ExtractColoredSymbols(availableSources),
                    StringComparer.OrdinalIgnoreCase),
                UntappedManaSources = turnStartSources,
                LandDropMade = landPlayed is not null,
                OnCurveUntappedMana = availableSources.Count >= turn,
            };

            if (!commanderCast
                && commander is not null
                && PerformanceMana.TryPay(commander, availableSources, out List<PerformanceManaSource> afterCommanderSources))
            {
                commanderCast = true;
                availableSources = afterCommanderSources;
                battlefield.Add(new PerformancePermanent { Card = commander });
                run.CommanderCastTurn = turn;
            }

            foreach (DeckCard spell in hand
                .Where(card => !IsCommanderCard(card))
                .Where(card => !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
                .OrderBy(card => PerformanceCastPriority(card, turn, profile))
                .ThenBy(PerformanceManaValue)
                .ToList())
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(spell);
                if (ShouldHoldPerformanceSpell(spell, role, turn, commanderCast, profile))
                {
                    continue;
                }

                if (!PerformanceMana.TryPay(spell, availableSources, out List<PerformanceManaSource> afterSpellSources))
                {
                    continue;
                }

                availableSources = afterSpellSources;
                hand.Remove(spell);
                if (IsPermanent(spell))
                {
                    battlefield.Add(new PerformancePermanent { Card = spell });
                }
                else
                {
                    graveyard.Add(spell);
                }

                if (role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
                {
                    rampCastByTurn = true;
                    state.RampCastByTurn = true;
                    if (!IsPermanent(spell))
                    {
                        virtualManaSources.Add(BuildPerformanceRampSource(spell, deckColors));
                    }
                }

                if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase))
                {
                    drawCastByTurn = true;
                    state.DrawCastByTurn = true;
                    if (library.Count > 0)
                    {
                        PerformanceDrawOne(hand, library);
                    }
                }
            }

            List<DeckCard> battlefieldCards = battlefield.Select(permanent => permanent.Card).ToList();
            List<DeckCard> seenCards = hand.Concat(battlefieldCards).Concat(graveyard).ToList();
            state.AvailableMana = availableSources.Count;
            state.RampSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Ramp);
            state.RampCastByTurn = state.RampCastByTurn || rampCastByTurn;
            state.DrawSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Draw);
            state.DrawCastByTurn = state.DrawCastByTurn || drawCastByTurn;
            state.InteractionSeenByTurn = PerformanceHasAnyRole(seenCards, DeckRoles.Interaction, DeckRoles.BoardWipes);
            state.ProtectionSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Protection);
            state.GraveyardHateSeenByTurn = PerformanceHasTag(seenCards, DeckTags.GraveyardHate);
            state.InteractionHeldUp = HasHeldPerformanceRole(
                hand,
                availableSources,
                DeckRoles.Interaction);
            state.ProtectionHeldUp = HasHeldPerformanceRole(
                hand,
                availableSources,
                DeckRoles.Protection);
            state.CastableHandRate = CalculatePerformanceCastableHandRate(
                hand,
                turnStartSources);
            state.CardsInHand = hand.Count;
            state.AllDeckColorsAvailable = deckColors.Count == 0
                || deckColors.All(color => state.ColorSources.Contains(color));
            state.CommanderCastByTurn = commanderCast;
            state.CommanderProtectedByTurn = commanderCast
                && (state.ProtectionHeldUp
                    || PerformanceHasRole(battlefieldCards, DeckRoles.Protection)
                    || PerformanceHasTag(battlefieldCards, DeckTags.CombatProtection));

            if (state.CommanderProtectedByTurn && !run.CommanderProtectedTurn.HasValue)
            {
                run.CommanderProtectedTurn = turn;
            }

            int comboPiecesSeen = CountPerformanceComboCards(seenCards, includeTutors: false);
            bool tutorSeen = PerformanceHasRole(seenCards, DeckRoles.Tutors);
            state.ComboPiecesSeen = comboPiecesSeen;
            state.ComboAssemblyByTurn = comboPiecesSeen >= 2;
            state.TutorAssistedComboByTurn = comboPiecesSeen >= 1 && tutorSeen;
            if (state.ComboAssemblyByTurn && !run.ComboAssemblyTurn.HasValue)
            {
                run.ComboAssemblyTurn = turn;
            }

            if ((state.ComboAssemblyByTurn || state.TutorAssistedComboByTurn)
                && !run.TutorAssistedComboTurn.HasValue)
            {
                run.TutorAssistedComboTurn = turn;
            }

            run.Turns.Add(state);
        }

        AddPerformanceStrandedCards(run, hand, run.Turns.LastOrDefault(), maxTurn);
        return run;
    }

    /// <summary>
    /// Draws an opening hand and applies the London mulligan heuristic.
    /// </summary>
    private static PerformanceOpeningHand DrawPerformanceOpeningHand(
        DeckWorkspace workspace,
        Random random,
        bool includeMulligans,
        PerformanceMulliganContext context)
    {
        int mulligans = 0;
        int maximumMulligans = context.FreeFirstMulligan ? 3 : 2;
        List<DeckCard> firstSeven = [];
        while (mulligans <= maximumMulligans)
        {
            int targetHandSize = PerformanceTargetHandSize(mulligans, context);
            List<DeckCard> library = ExpandPerformanceLibrary(workspace);
            Shuffle(library, random);
            List<DeckCard> hand = library.Take(Math.Min(7, library.Count)).ToList();
            library = library.Skip(hand.Count).ToList();
            if (mulligans == 0)
            {
                firstSeven = hand.ToList();
            }

            bool keep = !includeMulligans
                || IsKeepablePerformanceHand(hand, targetHandSize, mulligans, context)
                || targetHandSize <= 5;
            if (keep)
            {
                BottomPerformanceCards(hand, targetHandSize, context);
                return new PerformanceOpeningHand
                {
                    Hand = hand,
                    Library = library,
                    Mulligans = mulligans,
                    OpeningSevenLands = CountPerformanceRole(firstSeven, DeckRoles.Lands),
                };
            }

            mulligans++;
        }

        throw new InvalidOperationException("Mulligan heuristic failed to keep a hand by five cards.");
    }

    /// <summary>
    /// Expands included non-commander cards into individual library entries.
    /// </summary>
    private static List<DeckCard> ExpandPerformanceLibrary(DeckWorkspace workspace)
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
    /// Computes kept hand size after actual mulligans, including free first mulligans.
    /// </summary>
    private static int PerformanceTargetHandSize(
        int mulligans,
        PerformanceMulliganContext context)
    {
        int paidMulligans = context.FreeFirstMulligan && mulligans > 0
            ? mulligans - 1
            : mulligans;
        return Math.Max(0, 7 - paidMulligans);
    }

    /// <summary>
    /// Determines whether a candidate opening hand should be kept.
    /// </summary>
    private static bool IsKeepablePerformanceHand(
        IReadOnlyList<DeckCard> hand,
        int targetHandSize,
        int mulligans,
        PerformanceMulliganContext context)
    {
        int lands = CountPerformanceRole(hand, DeckRoles.Lands);
        if (lands == 0)
        {
            return false;
        }

        if (targetHandSize >= 6 && lands >= 6)
        {
            return false;
        }

        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2);
        if (targetHandSize >= 6 && lands == 1 && earlyRamp < 2)
        {
            return false;
        }

        double score = ScorePerformanceOpeningHand(hand, context);
        return score >= MinimumPerformanceKeepScore(targetHandSize, mulligans, context);
    }

    /// <summary>
    /// Scores how well an opening hand supports functional early development.
    /// </summary>
    private static double ScorePerformanceOpeningHand(
        IReadOnlyList<DeckCard> hand,
        PerformanceMulliganContext context)
    {
        int lands = CountPerformanceRole(hand, DeckRoles.Lands);
        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2);
        int oneManaRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 1);
        int earlyDraw = CountEarlyPerformanceRole(hand, DeckRoles.Draw, maxManaValue: 3);
        int cheapPlays = CountCheapPerformancePlays(hand);
        int earlyInteraction = CountEarlyPerformanceRole(hand, DeckRoles.Interaction, maxManaValue: 2)
            + CountEarlyPerformanceRole(hand, DeckRoles.Protection, maxManaValue: 2);
        int expensiveCards = hand.Count(card =>
            !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && PerformanceManaValue(card) >= 6);
        HashSet<string> colors = BuildOpeningHandColorAccess(hand);
        bool hasCastableEarlySpell = HasCastableEarlyPerformanceSpell(hand);
        bool hasCommanderPlan = HasPerformanceCommanderPlan(hand, context);
        bool hasEarlyPlan = earlyRamp > 0
            || earlyDraw > 0
            || cheapPlays >= 2
            || hasCastableEarlySpell
            || hasCommanderPlan;

        double score = lands switch
        {
            0 => -8,
            1 => earlyRamp >= 2 ? 2 : -4,
            2 => 4,
            3 => 5,
            4 => 3,
            5 => 1,
            _ => -4,
        };

        score += Math.Min(earlyRamp, 2) * context.Mulligan.EarlyRampWeight;
        score += oneManaRamp > 0 ? context.Mulligan.OneManaRampWeight : 0;
        score += Math.Min(earlyDraw, 2) * context.Mulligan.EarlyDrawWeight;
        score += Math.Min(cheapPlays, 3) * context.Mulligan.CheapPlayWeight;
        score += Math.Min(earlyInteraction, 2) * context.Mulligan.EarlyInteractionWeight;
        score += hasCastableEarlySpell ? 1 : -1;
        score += hasCommanderPlan ? context.Mulligan.CommanderPlanWeight : 0;
        score -= Math.Min(expensiveCards, 3);

        if (context.DeckColors.Count > 0)
        {
            int coveredColors = context.DeckColors.Count(color => colors.Contains(color));
            if (coveredColors == context.DeckColors.Count)
            {
                score += 1.5;
            }
            else if (coveredColors > 0)
            {
                score += 0.5;
            }
            else
            {
                score -= 2;
            }
        }

        if (lands >= 5 && !hasEarlyPlan)
        {
            score -= 2;
        }

        if (lands == 2 && earlyRamp == 0 && earlyDraw == 0 && !hasCommanderPlan)
        {
            score -= 1;
        }

        if (!hasEarlyPlan)
        {
            score -= 1;
        }

        return score;
    }

    /// <summary>
    /// Returns the minimum hand score needed before accepting a mulligan decision.
    /// </summary>
    private static double MinimumPerformanceKeepScore(
        int targetHandSize,
        int mulligans,
        PerformanceMulliganContext context)
    {
        if (targetHandSize <= 5)
        {
            return context.Mulligan.FiveCardKeepScore;
        }

        if (targetHandSize == 6)
        {
            return context.Mulligan.SixCardKeepScore;
        }

        return context.FreeFirstMulligan && mulligans == 0
            ? context.Mulligan.SevenCardFreeKeepScore
            : context.Mulligan.SevenCardKeepScore;
    }

    /// <summary>
    /// Counts cheap cards for opening-hand development.
    /// </summary>
    private static int CountCheapPerformancePlays(IReadOnlyList<DeckCard> hand)
    {
        return hand.Count(card =>
            !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && PerformanceManaValue(card) <= 2);
    }

    /// <summary>
    /// Counts early plays with a specific primary role.
    /// </summary>
    private static int CountEarlyPerformanceRole(
        IReadOnlyList<DeckCard> hand,
        string roleName,
        int maxManaValue)
    {
        return hand.Count(card =>
            DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase)
            && PerformanceManaValue(card) <= maxManaValue);
    }

    /// <summary>
    /// Checks whether the hand can deploy at least one cheap nonland card.
    /// </summary>
    private static bool HasCastableEarlyPerformanceSpell(IReadOnlyList<DeckCard> hand)
    {
        List<PerformanceManaSource> sources = BuildOpeningLandManaSources(hand);
        return hand.Any(card =>
            !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && PerformanceManaValue(card) <= 2
            && PerformanceMana.CanPay(card, sources));
    }

    /// <summary>
    /// Checks whether opening resources plausibly cast the commander by turn four.
    /// </summary>
    private static bool HasPerformanceCommanderPlan(
        IReadOnlyList<DeckCard> hand,
        PerformanceMulliganContext context)
    {
        if (context.Commander is null)
        {
            return false;
        }

        int lands = CountPerformanceRole(hand, DeckRoles.Lands);
        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2);
        int expectedLandDropsByTurnFour = lands >= 2 ? Math.Min(4, lands + 1) : lands;
        int expectedManaByTurnFour = expectedLandDropsByTurnFour + Math.Min(earlyRamp, 2);
        if (expectedManaByTurnFour < PerformanceManaValue(context.Commander))
        {
            return false;
        }

        List<PerformanceManaSource> sources = BuildOpeningHandManaSources(hand);
        return PerformanceMana.CanSatisfyRequirement(
            PerformanceMana.BuildCostRequirement(context.Commander),
            sources);
    }

    /// <summary>
    /// Builds mana sources available from lands and castable cheap fixing.
    /// </summary>
    private static List<PerformanceManaSource> BuildOpeningHandManaSources(IReadOnlyList<DeckCard> hand)
    {
        List<PerformanceManaSource> sources = BuildOpeningLandManaSources(hand);
        foreach (DeckCard card in hand.Where(card =>
            !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && PerformanceManaValue(card) <= 2
            && PerformanceMana.ReadProducedMana(card).Count > 0))
        {
            if (PerformanceMana.CanPay(card, sources))
            {
                sources.Add(new PerformanceManaSource(PerformanceMana.ReadProducedMana(card)));
            }
        }

        return sources;
    }

    /// <summary>
    /// Builds mana sources available from lands in the opening hand.
    /// </summary>
    private static List<PerformanceManaSource> BuildOpeningLandManaSources(IReadOnlyList<DeckCard> hand)
    {
        return hand
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            .Select(card => new PerformanceManaSource(PerformanceMana.ReadProducedMana(card)))
            .Where(source => source.Symbols.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Reads color symbols reachable from opening lands and castable cheap fixing.
    /// </summary>
    private static HashSet<string> BuildOpeningHandColorAccess(IReadOnlyList<DeckCard> hand)
    {
        return BuildOpeningHandManaSources(hand)
            .SelectMany(source => source.Symbols)
            .Where(symbol => PerformanceMana.ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bottoms cards after mulligans by preserving the highest-scoring kept hand.
    /// </summary>
    private static void BottomPerformanceCards(
        List<DeckCard> hand,
        int targetHandSize,
        PerformanceMulliganContext context)
    {
        int removeCount = hand.Count - targetHandSize;
        if (removeCount <= 0)
        {
            return;
        }

        List<int> bestIndexes = [];
        double bestScore = double.NegativeInfinity;
        int bestPriority = int.MinValue;
        foreach (List<int> indexes in EnumerateIndexCombinations(hand.Count, removeCount))
        {
            HashSet<int> bottomIndexes = [.. indexes];
            List<DeckCard> kept = hand
                .Where((_, index) => !bottomIndexes.Contains(index))
                .ToList();
            double score = ScorePerformanceOpeningHand(kept, context);
            int priority = indexes.Sum(index => PerformanceBottomPriority(hand[index], hand));
            if (score > bestScore || (score.Equals(bestScore) && priority > bestPriority))
            {
                bestScore = score;
                bestPriority = priority;
                bestIndexes = indexes;
            }
        }

        foreach (int index in bestIndexes.OrderByDescending(index => index))
        {
            hand.RemoveAt(index);
        }
    }

    /// <summary>
    /// Enumerates index combinations for choosing cards to bottom.
    /// </summary>
    private static IEnumerable<List<int>> EnumerateIndexCombinations(int count, int choose)
    {
        List<int> current = [];
        foreach (List<int> combination in EnumerateIndexCombinations(count, choose, start: 0, current))
        {
            yield return combination;
        }
    }

    /// <summary>
    /// Recursively enumerates index combinations in increasing order.
    /// </summary>
    private static IEnumerable<List<int>> EnumerateIndexCombinations(
        int count,
        int choose,
        int start,
        List<int> current)
    {
        if (current.Count == choose)
        {
            yield return [.. current];
            yield break;
        }

        for (int index = start; index <= count - (choose - current.Count); index++)
        {
            current.Add(index);
            foreach (List<int> combination in EnumerateIndexCombinations(count, choose, index + 1, current))
            {
                yield return combination;
            }

            current.RemoveAt(current.Count - 1);
        }
    }

    /// <summary>
    /// Scores a card for bottoming during the mulligan process.
    /// </summary>
    private static int PerformanceBottomPriority(DeckCard card, IReadOnlyList<DeckCard> hand)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        int lands = CountPerformanceRole(hand, DeckRoles.Lands);
        if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
        {
            return lands > 3 ? 5 : 0;
        }

        if (PerformanceManaValue(card) >= 6)
        {
            return 4;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Chooses the land drop that improves untapped color access when possible.
    /// </summary>
    private static DeckCard? ChoosePerformanceLand(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformancePermanent> battlefield,
        IReadOnlyList<IReadOnlyList<string>> virtualManaSources,
        IReadOnlySet<string> deckColors)
    {
        HashSet<string> existingColors = ExtractColoredSymbols(GetPerformanceManaSources(
            battlefield,
            virtualManaSources,
            unavailablePermanent: null));
        List<DeckCard> lands = hand
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return lands
            .OrderByDescending(card => PerformanceMana.ReadProducedMana(card).Count(color => deckColors.Contains(color) && !existingColors.Contains(color)))
            .ThenBy(card => PerformanceMana.LooksTapped(PerformanceMana.GetSnapshot(card)) ? 1 : 0)
            .ThenByDescending(card => PerformanceMana.ReadProducedMana(card).Count)
            .FirstOrDefault();
    }

    /// <summary>
    /// Scores spell sequencing priority for one turn of heuristic development.
    /// </summary>
    private static int PerformanceCastPriority(DeckCard card, int turn, SimulationProfile profile)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        if (turn <= 3 && role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            return profile.Sequencing.EarlyRampPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Engines))
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

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers))
        {
            return profile.Sequencing.WinconPriority;
        }

        return profile.Sequencing.DefaultPriority;
    }

    /// <summary>
    /// Determines whether a nonpermanent spell should be held for interaction or protection.
    /// </summary>
    private static bool ShouldHoldPerformanceSpell(
        DeckCard card,
        CardRoleAssignment role,
        int turn,
        bool commanderCast,
        SimulationProfile profile)
    {
        if (IsPermanent(card))
        {
            return false;
        }

        bool interaction = role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase);
        if (interaction && turn >= profile.Sequencing.HoldInteractionFromTurn && profile.Sequencing.MinimumInteractionHeld > 0)
        {
            return true;
        }

        return commanderCast
            && profile.Sequencing.HoldProtectionWhenCommanderOnline
            && role.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a nonnegative integer mana value from a card snapshot.
    /// </summary>
    private static int PerformanceManaValue(DeckCard card)
    {
        return PerformanceMana.ManaValue(card);
    }

    /// <summary>
    /// Lists battlefield and virtual mana sources available for a turn.
    /// </summary>
    private static List<PerformanceManaSource> GetPerformanceManaSources(
        IReadOnlyList<PerformancePermanent> battlefield,
        IReadOnlyList<IReadOnlyList<string>> virtualManaSources,
        PerformancePermanent? unavailablePermanent)
    {
        List<PerformanceManaSource> sources = [];
        foreach (PerformancePermanent permanent in battlefield)
        {
            if (ReferenceEquals(permanent, unavailablePermanent) || !IsPerformanceManaSource(permanent.Card))
            {
                continue;
            }

            sources.Add(new PerformanceManaSource(PerformanceMana.ReadProducedMana(permanent.Card)));
        }

        sources.AddRange(virtualManaSources.Select(source => new PerformanceManaSource(source)));
        return sources;
    }

    /// <summary>
    /// Checks whether a card contributes mana in the heuristic simulation.
    /// </summary>
    private static bool IsPerformanceManaSource(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            || PerformanceMana.ReadProducedMana(card).Count > 0;
    }

    /// <summary>
    /// Gets color symbols from currently available mana sources.
    /// </summary>
    private static HashSet<string> ExtractColoredSymbols(IEnumerable<PerformanceManaSource> sources)
    {
        return sources
            .SelectMany(source => source.Symbols)
            .Where(symbol => PerformanceMana.ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a nonpermanent ramp spell into a future virtual mana source.
    /// </summary>
    private static IReadOnlyList<string> BuildPerformanceRampSource(
        DeckCard rampCard,
        IReadOnlySet<string> deckColors)
    {
        IReadOnlyList<string> produced = PerformanceMana.ReadProducedMana(rampCard);
        if (produced.Count > 0)
        {
            return produced;
        }

        return deckColors.Count > 0 ? deckColors.ToList() : [];
    }

    /// <summary>
    /// Moves the top library card into hand.
    /// </summary>
    private static void PerformanceDrawOne(List<DeckCard> hand, List<DeckCard> library)
    {
        hand.Add(library[0]);
        library.RemoveAt(0);
    }

    /// <summary>
    /// Calculates what share of nonland hand cards are castable with current resources.
    /// </summary>
    private static double CalculatePerformanceCastableHandRate(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformanceManaSource> availableSources)
    {
        List<DeckCard> spells = hand
            .Where(card => !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            .Where(card => !IsCommanderCard(card))
            .ToList();
        if (spells.Count == 0)
        {
            return 1;
        }

        return spells.Count(card => PerformanceMana.CanPay(card, availableSources)) / (double)spells.Count;
    }

    /// <summary>
    /// Checks whether a role card is held in hand and castable with unused mana.
    /// </summary>
    private static bool HasHeldPerformanceRole(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformanceManaSource> availableSources,
        string roleName)
    {
        return hand.Any(card =>
            DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase)
            && PerformanceMana.CanPay(card, availableSources));
    }

    /// <summary>
    /// Checks whether any card in a zone has the requested primary role.
    /// </summary>
    private static bool PerformanceHasRole(IEnumerable<DeckCard> cards, string roleName)
    {
        return cards.Any(card =>
            DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether any card in a zone has any requested primary role.
    /// </summary>
    private static bool PerformanceHasAnyRole(IEnumerable<DeckCard> cards, params string[] roleNames)
    {
        return cards.Any(card =>
            roleNames.Any(roleName =>
                DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Checks whether any card in a zone has the requested tag.
    /// </summary>
    private static bool PerformanceHasTag(IEnumerable<DeckCard> cards, string tag)
    {
        return cards.Any(card => DeckRoleClassifier.Classify(card).Tags.Contains(tag));
    }

    /// <summary>
    /// Counts cards in a zone with the requested primary role.
    /// </summary>
    private static int CountPerformanceRole(IEnumerable<DeckCard> cards, string roleName)
    {
        return cards.Count(card =>
            DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Counts distinct seen cards that contribute to combo assembly.
    /// </summary>
    private static int CountPerformanceComboCards(
        IEnumerable<DeckCard> cards,
        bool includeTutors)
    {
        return cards
            .Where(card =>
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                return role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler)
                    || (includeTutors && role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase));
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Records high-mana cards still stranded by the final simulated turn.
    /// </summary>
    private static void AddPerformanceStrandedCards(
        PerformanceRun run,
        IReadOnlyList<DeckCard> hand,
        PerformanceTurnState? lastTurn,
        int maxTurn)
    {
        if (lastTurn is null)
        {
            return;
        }

        foreach (DeckCard card in hand)
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || IsCommanderCard(card)
                || PerformanceManaValue(card) < 4)
            {
                continue;
            }

            PerformanceCostRequirement requirement = PerformanceMana.BuildCostRequirement(card);
            bool manaStranded = PerformanceManaValue(card) > lastTurn.ManaSources;
            bool colorStranded = !PerformanceMana.CanSatisfyRequirement(requirement, lastTurn.UntappedManaSources);
            if (!manaStranded && !colorStranded)
            {
                continue;
            }

            run.StrandedCards[card.Name] = new PerformanceStrandedRun
            {
                CardName = card.Name,
                ManaValue = PerformanceManaValue(card),
                ManaStranded = manaStranded,
                ColorStranded = colorStranded,
                FinalTurn = maxTurn,
            };
        }
    }

    /// <summary>
    /// Aggregates opening hand and mulligan metrics from runs.
    /// </summary>
    private static OpeningHandPerformance BuildOpeningHandPerformance(
        IReadOnlyList<PerformanceRun> runs)
    {
        OpeningHandPerformance result = new()
        {
            SevenCardKeepRate = PerformanceStatistics.Rate(runs.Count(run => run.Mulligans == 0), runs.Count),
            AverageMulligans = runs.Count == 0 ? 0 : runs.Average(run => run.Mulligans),
            AverageKeptHandSize = runs.Count == 0 ? 0 : runs.Average(run => run.KeptHandSize),
            AverageKeptLands = runs.Count == 0 ? 0 : runs.Average(run => run.KeptOpeningLands),
            NoLandSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands == 0), runs.Count),
            OneLandSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands == 1), runs.Count),
            FloodedSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands >= 6), runs.Count),
        };

        foreach (IGrouping<int, PerformanceRun> group in runs.GroupBy(run => run.Mulligans))
        {
            result.MulliganDistribution[group.Key] = group.Count();
        }

        return result;
    }

    /// <summary>
    /// Adds shared turn-by-turn probability and average metrics.
    /// </summary>
    private static void AddTurnPerformanceMetrics(
        DeckPerformanceAnalysis analysis,
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn)
    {
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            List<PerformanceTurnState> states = StatesForTurn(runs, turn);
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "land-drop-by-turn",
                turn,
                states.Count(state => state.LandsInPlay >= Math.Min(turn, 10)),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "ramp-seen-by-turn",
                turn,
                states.Count(state => state.RampSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "ramp-cast-by-turn",
                turn,
                states.Count(state => state.RampCastByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "draw-seen-by-turn",
                turn,
                states.Count(state => state.DrawSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "draw-cast-by-turn",
                turn,
                states.Count(state => state.DrawCastByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "interaction-seen-by-turn",
                turn,
                states.Count(state => state.InteractionSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "interaction-held-up-by-turn",
                turn,
                states.Count(state => state.InteractionHeldUp),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "on-curve-untapped-mana-by-turn",
                turn,
                states.Count(state => state.OnCurveUntappedMana),
                states.Count));
            if (colorIdentityKnown && deckColors.Count > 0)
            {
                analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                    "all-deck-colors-by-turn",
                    turn,
                    states.Count(state => state.AllDeckColorsAvailable),
                    states.Count));
            }

            analysis.TurnAverages.Add(PerformanceStatistics.BuildAverage(
                "available-mana-after-development",
                turn,
                states.Select(state => (double)state.AvailableMana).ToList()));
            analysis.TurnAverages.Add(PerformanceStatistics.BuildAverage(
                "cards-in-hand",
                turn,
                states.Select(state => (double)state.CardsInHand).ToList()));
        }
    }

    /// <summary>
    /// Builds spell castability and color reliability metrics.
    /// </summary>
    private static CastabilityPerformance BuildCastabilityPerformance(
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn)
    {
        CastabilityPerformance result = new();
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            List<PerformanceTurnState> states = StatesForTurn(runs, turn);
            result.SpellCastabilityByTurn.Add(PerformanceStatistics.BuildAverage(
                "castable-nonland-hand-rate",
                turn,
                states.Select(state => state.CastableHandRate).ToList()));

            if (!colorIdentityKnown)
            {
                continue;
            }

            foreach (string color in deckColors.Order(StringComparer.OrdinalIgnoreCase))
            {
                result.ColorSourceReliability.Add(PerformanceStatistics.BuildProbability(
                    $"source-{color}-by-turn",
                    turn,
                    states.Count(state => state.ColorSources.Contains(color)),
                    states.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds commander cast and protection timing metrics.
    /// </summary>
    private static CommanderPerformance BuildCommanderPerformance(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn)
    {
        CommanderPerformance result = new()
        {
            CommanderNames = included
                .Where(IsCommanderCard)
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        if (result.CommanderNames.Count == 0)
        {
            return result;
        }

        List<int> castTurns = runs
            .Where(run => run.CommanderCastTurn.HasValue)
            .Select(run => run.CommanderCastTurn!.Value)
            .ToList();
        result.AverageEarliestCastTurn = castTurns.Count == 0 ? null : castTurns.Average();

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.CastByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-cast-by-turn",
                turn,
                runs.Count(run => run.CommanderCastTurn <= turn),
                runs.Count));
            result.ProtectedByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-protected-by-turn",
                turn,
                runs.Count(run => run.CommanderProtectedTurn <= turn),
                runs.Count));
        }

        return result;
    }

    /// <summary>
    /// Builds combo-piece and tutor-assisted assembly metrics.
    /// </summary>
    private static ComboAssemblyPerformance BuildComboAssemblyPerformance(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn)
    {
        ComboAssemblyPerformance result = new()
        {
            RelevantCards = included
                .Where(card =>
                {
                    CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                    return role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler)
                        || role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase);
                })
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList(),
        };

        List<int> assemblyTurns = runs
            .Where(run => run.ComboAssemblyTurn.HasValue)
            .Select(run => run.ComboAssemblyTurn!.Value)
            .ToList();
        result.AverageEarliestAssemblyTurn = assemblyTurns.Count == 0 ? null : assemblyTurns.Average();

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.AssemblyByTurn.Add(PerformanceStatistics.BuildProbability(
                "combo-assembly-by-turn",
                turn,
                runs.Count(run => run.ComboAssemblyTurn <= turn),
                runs.Count));
            result.TutorAssistedAssemblyByTurn.Add(PerformanceStatistics.BuildProbability(
                "tutor-assisted-combo-by-turn",
                turn,
                runs.Count(run => run.TutorAssistedComboTurn <= turn),
                runs.Count));
        }

        return result;
    }

    /// <summary>
    /// Aggregates stranded-card risk rows across all runs.
    /// </summary>
    private static List<StrandedCardPerformance> BuildStrandedCardPerformance(
        IReadOnlyList<PerformanceRun> runs)
    {
        int sampleSize = runs.Count;
        return runs
            .SelectMany(run => run.StrandedCards.Values)
            .GroupBy(card => card.CardName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                int stranded = group.Count();
                return new StrandedCardPerformance
                {
                    CardName = group.Key,
                    ManaValue = group.Max(card => card.ManaValue),
                    StrandedRate = PerformanceStatistics.Rate(stranded, sampleSize),
                    ManaStrandedRate = PerformanceStatistics.Rate(group.Count(card => card.ManaStranded), sampleSize),
                    ColorStrandedRate = PerformanceStatistics.Rate(group.Count(card => card.ColorStranded), sampleSize),
                    SampleSize = sampleSize,
                };
            })
            .Where(card => card.StrandedRate >= 0.03)
            .OrderByDescending(card => card.StrandedRate)
            .ThenByDescending(card => card.ManaValue)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Builds named deckbuilder scenarios from the simulated run set.
    /// </summary>
    private static List<ScenarioPerformance> BuildScenarioPerformance(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        PerformanceScenarioDefaults defaults = BuildScenarioDefaults(maxTurn, profile, intent);
        List<string> commanderDrivers =
            ["Missing early land drops", "Missing required commander colors", "Insufficient early ramp"];
        List<string> commanderProtectionDrivers =
            ["Protection density is low", "Protection is present but not castable after commander development"];
        List<string> graveyardHateDrivers =
            ["Graveyard hate density is low", "Graveyard hate appears after the target turn"];
        List<string> colorDrivers =
            ["Missing color sources", "Tapped lands delayed early color access"];
        List<string> interactionDrivers =
            ["Interaction density is low", "Early development spends mana before interaction can be held up"];
        List<string> comboDrivers =
            ["Combo density is low", "Tutors or pieces are not seen by the target turn"];
        List<string> strandedDrivers =
            ["High mana-value cards outpace available mana", "Colored costs are missing matching sources"];
        List<ScenarioPerformance> scenarios =
        [
            BuildScenario(
                "commander-by-turn-4",
                defaults.CommanderTurn,
                runs.Count(run => run.CommanderCastTurn <= defaults.CommanderTurn),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Commander),
                commanderDrivers,
                defaults.IntentAdjusted
                    ? ["Commander is treated as always available from the command zone.", "Deck intent adjusted the target turn."]
                    : ["Commander is treated as always available from the command zone."],
                BuildCommanderFailureDriverCounts(included, runs, defaults.CommanderTurn, commanderDrivers)),
            BuildScenario(
                "commander-with-protection-by-turn-5",
                defaults.ProtectionTurn,
                runs.Count(run => run.CommanderProtectedTurn <= defaults.ProtectionTurn),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Protection),
                commanderProtectionDrivers,
                ["Protection includes held-up protection spells and protection permanents."],
                BuildProtectionFailureDriverCounts(runs, defaults.ProtectionTurn, commanderProtectionDrivers)),
            BuildScenario(
                "graveyard-hate-by-turn-3",
                defaults.HateTurn,
                runs.Count(run => StateAt(run, defaults.HateTurn)?.GraveyardHateSeenByTurn == true),
                runs.Count,
                RelevantPerformanceTaggedCards(included, DeckTags.GraveyardHate),
                graveyardHateDrivers,
                ["Scenario measures access, not whether the hate is tactically correct to deploy."],
                BuildGraveyardHateFailureDriverCounts(included, runs, defaults.HateTurn, graveyardHateDrivers)),
            BuildScenario(
                "all-colors-by-turn-3",
                defaults.ColorTurn,
                colorIdentityKnown
                    ? runs.Count(run => StateAt(run, defaults.ColorTurn)?.AllDeckColorsAvailable == true)
                    : 0,
                runs.Count,
                ManaSourcePerformanceCards(included, deckColors),
                colorDrivers,
                ["Uses cached produced_mana plus basic-land name fallbacks."],
                BuildColorFailureDriverCounts(runs, defaults.ColorTurn, colorDrivers)),
            BuildScenario(
                "hold-up-interaction-by-turn-4",
                defaults.InteractionTurn,
                runs.Count(run => StateAt(run, defaults.InteractionTurn)?.InteractionHeldUp == true),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Interaction),
                interactionDrivers,
                ["Held-up interaction means a classified interaction spell remains in hand and is castable."],
                BuildInteractionFailureDriverCounts(runs, defaults.InteractionTurn, interactionDrivers)),
            BuildScenario(
                "combo-or-tutor-assembly-by-turn-5",
                defaults.ComboTurn,
                runs.Count(run => run.TutorAssistedComboTurn <= defaults.ComboTurn),
                runs.Count,
                RelevantComboPerformanceCards(included),
                comboDrivers,
                ["Assembly means two combo cards, or a combo card plus a tutor, have been seen."],
                BuildComboFailureDriverCounts(runs, defaults.ComboTurn, comboDrivers)),
            BuildScenario(
                "stranded-high-mana-risk-by-max-turn",
                maxTurn,
                runs.Count(run => run.StrandedCards.Count > 0),
                runs.Count,
                runs
                    .SelectMany(run => run.StrandedCards.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList(),
                strandedDrivers,
                ["This is a risk rate; lower is better."],
                BuildStrandedFailureDriverCounts(runs, strandedDrivers)),
        ];

        if (!colorIdentityKnown)
        {
            ScenarioPerformance colorScenario = scenarios
                .First(scenario => scenario.Name.Equals("all-colors-by-turn-3", StringComparison.OrdinalIgnoreCase));
            colorScenario.FailureDrivers.Add("Deck color identity could not be inferred.");
            colorScenario.FailureDriverCounts["Deck color identity could not be inferred."] = runs.Count;
        }

        return scenarios;
    }

    /// <summary>
    /// Builds target turns for named scenarios from the profile and deck intent.
    /// </summary>
    private static PerformanceScenarioDefaults BuildScenarioDefaults(
        int maxTurn,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        bool intentAdjusted = intent is not null
            && (!string.IsNullOrWhiteSpace(intent.SimulationProfile)
                || !string.IsNullOrWhiteSpace(intent.PowerTarget)
                || !string.IsNullOrWhiteSpace(intent.PowerLevel)
                || intent.TargetGoldfishTurn.HasValue);
        return new PerformanceScenarioDefaults
        {
            CommanderTurn = ClampScenarioTurn(profile.Scenarios.CommanderTurn, maxTurn),
            ProtectionTurn = ClampScenarioTurn(profile.Scenarios.ProtectionTurn, maxTurn),
            HateTurn = ClampScenarioTurn(profile.Scenarios.HateTurn, maxTurn),
            ColorTurn = ClampScenarioTurn(profile.Scenarios.ColorTurn, maxTurn),
            InteractionTurn = ClampScenarioTurn(profile.Scenarios.InteractionTurn, maxTurn),
            ComboTurn = ClampScenarioTurn(profile.Scenarios.ComboTurn, maxTurn),
            IntentAdjusted = intentAdjusted,
        };
    }

    /// <summary>
    /// Clamps a scenario target turn to the simulated horizon.
    /// </summary>
    private static int ClampScenarioTurn(int turn, int maxTurn)
    {
        return Math.Clamp(turn, 1, maxTurn);
    }

    /// <summary>
    /// Creates one scenario result with interval data.
    /// </summary>
    private static ScenarioPerformance BuildScenario(
        string name,
        int targetTurn,
        int successes,
        int sampleSize,
        List<string> relevantCards,
        List<string> failureDrivers,
        List<string> assumptions,
        Dictionary<string, int>? failureDriverCounts = null)
    {
        (double low, double high) = PerformanceStatistics.ConfidenceInterval(successes, sampleSize);
        return new ScenarioPerformance
        {
            Name = name,
            TargetTurn = targetTurn,
            SuccessRate = PerformanceStatistics.Rate(successes, sampleSize),
            LowConfidenceInterval = low,
            HighConfidenceInterval = high,
            SampleSize = sampleSize,
            RelevantCards = relevantCards,
            FailureDrivers = failureDrivers,
            FailureDriverCounts = failureDriverCounts
                ?? BuildFailureDriverCounts(name, successes, sampleSize, failureDrivers),
            Assumptions = assumptions,
        };
    }

    /// <summary>
    /// Counts likely commander deployment failure causes from run states.
    /// </summary>
    private static Dictionary<string, int> BuildCommanderFailureDriverCounts(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        DeckCard? commander = included.FirstOrDefault(IsCommanderCard);
        int commanderCost = commander is null ? 0 : PerformanceManaValue(commander);
        PerformanceCostRequirement? commanderRequirement = commander is null
            ? null
            : PerformanceMana.BuildCostRequirement(commander);
        foreach (PerformanceRun run in runs.Where(run => run.CommanderCastTurn is null || run.CommanderCastTurn > targetTurn))
        {
            PerformanceTurnState? state = StateAt(run, targetTurn);
            if (state is null)
            {
                continue;
            }

            if (state.LandsInPlay < Math.Min(targetTurn, Math.Max(1, commanderCost)))
            {
                Increment(counts, drivers[0]);
            }

            if (commanderRequirement is not null
                && !PerformanceMana.CanSatisfyRequirement(commanderRequirement, state.UntappedManaSources))
            {
                Increment(counts, drivers[1]);
            }

            if (!state.RampCastByTurn && state.ManaSources < commanderCost)
            {
                Increment(counts, drivers[2]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Counts likely commander protection scenario failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildProtectionFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceRun run in runs.Where(run => run.CommanderProtectedTurn is null || run.CommanderProtectedTurn > targetTurn))
        {
            PerformanceTurnState? state = StateAt(run, targetTurn);
            if (state is null || !state.ProtectionSeenByTurn)
            {
                Increment(counts, drivers[0]);
                continue;
            }

            Increment(counts, drivers[1]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely graveyard hate access failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildGraveyardHateFailureDriverCounts(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        bool hasGraveyardHate = RelevantPerformanceTaggedCards(included, DeckTags.GraveyardHate).Count > 0;
        foreach (PerformanceRun run in runs.Where(run => StateAt(run, targetTurn)?.GraveyardHateSeenByTurn != true))
        {
            Increment(counts, hasGraveyardHate ? drivers[1] : drivers[0]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely color access failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildColorFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.AllDeckColorsAvailable))
        {
            Increment(counts, drivers[0]);
            if (!state.OnCurveUntappedMana)
            {
                Increment(counts, drivers[1]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Counts likely interaction hold-up failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildInteractionFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.InteractionHeldUp))
        {
            Increment(counts, state.InteractionSeenByTurn ? drivers[1] : drivers[0]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely combo assembly failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildComboFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.TutorAssistedComboByTurn))
        {
            Increment(counts, state.ComboPiecesSeen == 0 ? drivers[0] : drivers[1]);
        }

        return counts;
    }

    /// <summary>
    /// Counts why high-mana cards were stranded in risky runs.
    /// </summary>
    private static Dictionary<string, int> BuildStrandedFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceStrandedRun stranded in runs.SelectMany(run => run.StrandedCards.Values))
        {
            if (stranded.ManaStranded)
            {
                Increment(counts, drivers[0]);
            }

            if (stranded.ColorStranded)
            {
                Increment(counts, drivers[1]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Creates a zero-filled failure-driver counter.
    /// </summary>
    private static Dictionary<string, int> EmptyDriverCounts(IEnumerable<string> drivers)
    {
        return drivers.ToDictionary(driver => driver, _ => 0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Increments a named counter.
    /// </summary>
    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Converts headline failure drivers into observed count buckets for the scenario result.
    /// </summary>
    private static Dictionary<string, int> BuildFailureDriverCounts(
        string scenarioName,
        int successes,
        int sampleSize,
        IEnumerable<string> failureDrivers)
    {
        int observedFailures = scenarioName.StartsWith("stranded-", StringComparison.OrdinalIgnoreCase)
            ? successes
            : Math.Max(0, sampleSize - successes);
        return failureDrivers.ToDictionary(
            driver => driver,
            _ => observedFailures,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists representative cards for a role-based scenario.
    /// </summary>
    private static List<string> RelevantPerformanceCards(
        IEnumerable<DeckCard> cards,
        string roleName)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists representative cards for a tag-based scenario.
    /// </summary>
    private static List<string> RelevantPerformanceTaggedCards(
        IEnumerable<DeckCard> cards,
        string tag)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Contains(tag))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists cards that represent combo pieces, combo enablers, or tutors.
    /// </summary>
    private static List<string> RelevantComboPerformanceCards(IEnumerable<DeckCard> cards)
    {
        return cards
            .Where(card =>
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                return role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler)
                    || role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase);
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists mana sources relevant to the deck's inferred color identity.
    /// </summary>
    private static List<string> ManaSourcePerformanceCards(
        IEnumerable<DeckCard> cards,
        IReadOnlySet<string> deckColors)
    {
        return cards
            .Where(IsPerformanceManaSource)
            .Where(card => deckColors.Count == 0 || PerformanceMana.ReadProducedMana(card).Any(deckColors.Contains))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Enumerates cards included in deck construction analysis.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Gets the deck color identity from commanders, falling back to included cards.
    /// </summary>
    private static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(DeckWorkspace workspace)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        bool foundCommander = false;
        foreach (DeckCard card in IncludedCards(workspace).Where(IsCommanderCard))
        {
            foundCommander = true;
            AddDeckColors(colors, PerformanceMana.GetSnapshot(card).ColorIdentity);
        }

        if (foundCommander)
        {
            return (true, colors);
        }

        foreach (DeckCard card in IncludedCards(workspace))
        {
            AddDeckColors(colors, PerformanceMana.GetSnapshot(card).ColorIdentity);
        }

        return (colors.Count > 0, colors);
    }

    /// <summary>
    /// Checks whether a card is in the Commander category.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a card remains on the battlefield after being cast.
    /// </summary>
    private static bool IsPermanent(DeckCard card)
    {
        string typeLine = PerformanceMana.GetSnapshot(card).TypeLine ?? "";
        return ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker", "Battle", "Land");
    }

    /// <summary>
    /// Shuffles cards in place using Fisher-Yates.
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
    /// Adds color identity symbols to a set.
    /// </summary>
    private static void AddDeckColors(HashSet<string> colors, IEnumerable<string> colorIdentity)
    {
        foreach (string color in colorIdentity)
        {
            if (PerformanceMana.ColoredSymbols.Contains(color, StringComparer.OrdinalIgnoreCase))
            {
                colors.Add(color);
            }
        }
    }

    /// <summary>
    /// Checks whether text contains any supplied phrase.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether a format normally receives a free first mulligan.
    /// </summary>
    private static bool UsesFreeFirstMulligan(string format)
    {
        return ContainsAny(format, "commander", "brawl");
    }

    /// <summary>
    /// Adds assumptions and warnings that explain simulator boundaries.
    /// </summary>
    private static void AddPerformanceNotes(
        DeckPerformanceAnalysis analysis,
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> included,
        bool colorIdentityKnown,
        ResolvedSimulationProfile profileResolution,
        DeckIntent? intent)
    {
        analysis.Assumptions.Add("Simulation uses cached Scryfall snapshots and local role/tag heuristics.");
        analysis.Assumptions.Add($"Each run draws one card per turn, plays one land per turn, and sequences spells with the '{profileResolution.Profile.Id}' simulation profile.");
        analysis.Assumptions.Add($"Simulation profile source: {profileResolution.Source}.");
        analysis.Assumptions.Add("Opponent interaction, stack timing, replacement effects, activated abilities, and full Magic rules are not simulated.");
        analysis.Assumptions.Add("London mulligans draw seven and bottom cards using a deterministic plan-aware keep heuristic.");
        if (UsesFreeFirstMulligan(workspace.Format))
        {
            analysis.Assumptions.Add("Commander and Brawl performance treats the first mulligan as free.");
        }

        analysis.Assumptions.Add("Nonpermanent ramp becomes one future mana source; draw spells draw one card.");
        if (intent is not null)
        {
            analysis.Assumptions.Add("Saved deck intent can adjust the active heuristic profile and scenario target turns.");
        }

        if (!workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            analysis.Warnings.Add("Default scenarios are Commander-oriented; interpret non-Commander output as generic heuristic sampling.");
        }

        if (!included.Any(IsCommanderCard))
        {
            analysis.Warnings.Add("No commander category was detected, so commander timing scenarios will be zero.");
        }

        if (!colorIdentityKnown)
        {
            analysis.Warnings.Add("Deck color identity could not be inferred from commander or included card snapshots.");
        }

        if (analysis.DeckSize < 60)
        {
            analysis.Warnings.Add("Included deck size is below most constructed deck sizes; probability estimates may be unusual.");
        }
    }

    /// <summary>
    /// Gets the latest recorded state for each run at a requested turn.
    /// </summary>
    private static List<PerformanceTurnState> StatesForTurn(
        IReadOnlyList<PerformanceRun> runs,
        int turn)
    {
        return runs
            .Select(run => StateAt(run, turn))
            .Where(state => state is not null)
            .Cast<PerformanceTurnState>()
            .ToList();
    }

    /// <summary>
    /// Gets the latest recorded state for a run at a requested turn.
    /// </summary>
    private static PerformanceTurnState? StateAt(PerformanceRun run, int turn)
    {
        return run.Turns.LastOrDefault(state => state.Turn <= turn);
    }

}
