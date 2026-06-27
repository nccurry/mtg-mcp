using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes goldfish-heavy MCP outputs without teaching Core services about MCP detail levels.
/// </summary>
internal static class GoldfishOutputPresenter
{
    /// <summary>
    /// Presents a generalized comparison with the requested evidence depth.
    /// </summary>
    public static object Present(DeckGoldfishComparisonResult result, string detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }
        string normalizedName = normalized.ToWireName();

        return new
        {
            detailLevel = normalizedName,
            workspaceId = result.WorkspaceId,
            targetTurn = result.TargetTurn,
            simulations = result.Simulations,
            seed = result.Seed,
            mulligan = result.Mulligan,
            baselineDeck = PresentDeck(result.BaselineDeck, normalized, result.TargetTurn),
            comparedDecks = result.ComparedDecks
                .Select(deck => PresentDeck(deck, normalized, result.TargetTurn))
                .ToList(),
            failures = result.Failures,
            notes = result.Notes,
            warnings = result.Warnings,
        };
    }

    /// <summary>
    /// Presents an Archidekt comparison with the requested evidence depth.
    /// </summary>
    public static object Present(ArchidektGoldfishComparisonResult result, string detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }
        string normalizedName = normalized.ToWireName();

        return new
        {
            detailLevel = normalizedName,
            workspaceId = result.WorkspaceId,
            targetTurn = result.TargetTurn,
            simulations = result.Simulations,
            seed = result.Seed,
            mulligan = result.Mulligan,
            activeDeck = PresentDeck(result.ActiveDeck, normalized, result.TargetTurn),
            referenceDecks = result.ReferenceDecks
                .Select(deck => PresentDeck(deck, normalized, result.TargetTurn))
                .ToList(),
            referenceFailures = result.ReferenceFailures,
            notes = result.Notes,
            warnings = result.Warnings,
        };
    }

    /// <summary>
    /// Presents a conservative rules-backed goldfish race with bounded evidence.
    /// </summary>
    public static object Present(RulesGoldfishRaceResult result, string detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }
        string normalizedName = normalized.ToWireName();

        return new
        {
            detailLevel = normalizedName,
            modelName = result.ModelName,
            engineVersion = result.EngineVersion,
            modelDescription = "Conservative template simulator; not a full Magic rules engine.",
            randomKind = result.RandomKind,
            seed = result.Seed,
            simulations = result.Simulations,
            startingLife = result.StartingLife,
            turnLimit = result.TurnLimit,
            mulligan = result.Mulligan,
            firstPlayerDraws = result.FirstPlayerDraws,
            seatOrder = result.SeatOrder,
            seedPolicy = result.SeedPolicy,
            tiePolicy = result.TiePolicy,
            commanderDamageIgnored = result.CommanderDamageIgnored,
            decks = result.Decks
                .Select(deck => PresentRaceDeck(deck, normalized))
                .ToList(),
            sampleOutcomes = normalized == DetailLevel.Normal
                ? result.SampleOutcomes
                : null,
            failures = result.Failures,
            notes = normalized == DetailLevel.Normal
                ? result.Notes
                : result.Notes.Take(2).ToList(),
            warnings = result.Warnings,
        };
    }

    /// <summary>
    /// Presents a batch tuning report with bounded goldfish evidence.
    /// </summary>
    public static object Present(DeckBatchTuningReport result, string detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }
        string normalizedName = normalized.ToWireName();

        return new
        {
            detailLevel = normalizedName,
            targetTurn = result.TargetTurn,
            simulations = result.Simulations,
            seed = result.Seed,
            maxBudget = result.MaxBudget,
            decks = result.Decks
                .Select(deck => PresentBatchDeck(deck, normalized, result.TargetTurn))
                .ToList(),
            failures = result.Failures,
            notes = result.Notes,
        };
    }

    /// <summary>
    /// Presents one conservative race deck row.
    /// </summary>
    private static object PresentRaceDeck(RulesGoldfishRaceDeckSummary deck, DetailLevel detailLevel)
    {
        return new
        {
            label = deck.Label,
            seat = deck.Seat,
            workspaceId = deck.WorkspaceId,
            name = deck.Name,
            wins = deck.Wins,
            ties = deck.Ties,
            draws = deck.Draws,
            losses = deck.Losses,
            winRate = deck.WinRate,
            tieRate = deck.TieRate,
            lethalRuns = deck.LethalRuns,
            medianLethalTurn = deck.MedianLethalTurn,
            lethalTurnCounts = detailLevel == DetailLevel.Normal
                ? deck.LethalTurnCounts
                : null,
            representativeTrace = detailLevel == DetailLevel.Normal
                ? deck.RepresentativeTrace
                : null,
            warnings = deck.Warnings,
        };
    }

    /// <summary>
    /// Presents one compared deck row.
    /// </summary>
    private static object PresentDeck(
        GoldfishDeckComparison deck,
        DetailLevel detailLevel,
        int targetTurn)
    {
        return new
        {
            label = deck.Label,
            source = deck.Source,
            input = deck.Input,
            workspaceId = deck.WorkspaceId,
            name = deck.Name,
            archidektDeckId = deck.ArchidektDeckId,
            includedCards = deck.IncludedCards,
            metrics = PresentMetrics(deck.Goldfish, targetTurn),
            deltaFromActive = deck.DeltaFromActive,
            details = detailLevel == DetailLevel.Normal
                ? PresentDetails(deck.Goldfish, targetTurn)
                : null,
        };
    }

    /// <summary>
    /// Presents one batch tuning deck row.
    /// </summary>
    private static object PresentBatchDeck(
        DeckBatchTuningDeckReport deck,
        DetailLevel detailLevel,
        int targetTurn)
    {
        return new
        {
            workspaceId = deck.WorkspaceId,
            name = deck.Name,
            validation = new
            {
                isValid = deck.Validation.IsValid,
                errors = deck.Validation.Errors,
                warnings = deck.Validation.Warnings,
            },
            cost = new
            {
                includedTotal = deck.Cost.IncludedTotal,
                maxBudget = deck.Cost.MaxBudget,
                withinKnownBudget = deck.Cost.WithinKnownBudget,
                withinBudget = deck.Cost.WithinBudget,
                budgetDelta = deck.Cost.BudgetDelta,
                budgetStatus = deck.Cost.BudgetStatus,
                priceRiskStatus = deck.Cost.PriceRiskStatus,
                missingPriceCards = deck.Cost.MissingPriceCards.Count,
                basicMissingPriceCards = deck.Cost.BasicMissingPriceCards.Count,
                nonBasicMissingPriceCards = deck.Cost.NonBasicMissingPriceCards.Count,
                unresolvedMissingPriceCards = deck.Cost.UnresolvedMissingPriceCards.Count,
                priceRiskNotes = deck.Cost.PriceRiskNotes,
            },
            bracket = new
            {
                estimatedBracket = deck.Bracket.EstimatedBracket,
                bracketFloor = deck.Bracket.BracketFloor,
                confidence = deck.Bracket.Confidence,
                gameChangerCount = deck.Bracket.GameChangerCount,
            },
            mana = new
            {
                landCount = deck.Mana.LandCount,
                manaProducingLandCount = deck.Mana.ManaProducingLandCount,
                alwaysTappedLandCount = deck.Mana.AlwaysTappedLandCount,
                conditionalTappedLandCount = deck.Mana.ConditionalTappedLandCount,
                fixingCount = deck.Mana.FixingCount,
                risks = deck.Mana.Risks.Take(3).ToList(),
            },
            consistency = new
            {
                deckSize = deck.Consistency.DeckSize,
                rampCount = deck.Consistency.RampCount,
                drawCount = deck.Consistency.DrawCount,
                tutorCount = deck.Consistency.TutorCount,
                cardSelectionCount = deck.Consistency.CardSelectionCount,
                risks = deck.Consistency.Risks.Take(3).ToList(),
            },
            bestPractices = new
            {
                recommendedProfile = deck.BestPractices.RecommendedProfile,
                risks = deck.BestPractices.Risks.Take(3).ToList(),
                recommendations = deck.BestPractices.Recommendations.Take(3).ToList(),
            },
            goldfish = PresentMetrics(deck.Goldfish, targetTurn),
            goldfishDetails = detailLevel == DetailLevel.Normal
                ? PresentDetails(deck.Goldfish, targetTurn)
                : null,
            risks = deck.Risks.Take(8).ToList(),
        };
    }

    /// <summary>
    /// Presents compact metrics that fit summary output.
    /// </summary>
    private static object PresentMetrics(
        GoldfishSimulationResult goldfish,
        int targetTurn)
    {
        WinTurnEstimate estimate = goldfish.WinEstimate;
        return new
        {
            targetTurn = goldfish.TargetTurn,
            modelLabel = goldfish.ModelLabel,
            rngKind = goldfish.RngKind,
            simulations = goldfish.Simulations,
            mulliganRate = goldfish.Simulations > 0
                ? goldfish.Mulligans / (double)goldfish.Simulations
                : 0,
            observedWins = estimate.ObservedWins,
            observedWinRate = estimate.ObservedWinRate,
            targetTurnWinRate = estimate.WinByTurnRates.TryGetValue(targetTurn, out double targetRate)
                ? targetRate
                : 0,
            medianObservedWinTurn = estimate.MedianObservedWinTurn,
            p25ObservedWinTurn = estimate.P25ObservedWinTurn,
            p75ObservedWinTurn = estimate.P75ObservedWinTurn,
            boardDevelopmentScore = goldfish.BoardDevelopmentScore,
            threatPressure = goldfish.ThreatPressure,
            engineOnlineRate = goldfish.EngineOnlineRate,
            winDetectionConfidence = goldfish.WinDetectionConfidence,
            profileId = goldfish.ProfileResolution.Profile.Id,
            profileSource = goldfish.ProfileResolution.Source,
            warnings = goldfish.Warnings,
        };
    }

    /// <summary>
    /// Presents bounded evidence for normal detail output.
    /// </summary>
    private static object PresentDetails(
        GoldfishSimulationResult goldfish,
        int targetTurn)
    {
        return new
        {
            targetTurnBoard = FindTurnSummary(goldfish, targetTurn),
            routes = goldfish.WinEstimate.Routes
                .Select(PresentRoute)
                .Take(5)
                .ToList(),
            routeEvidence = goldfish.WinEstimate.RouteEvidence
                .Take(5)
                .ToList(),
            representativeLines = goldfish.RepresentativeLines
                .Take(8)
                .ToList(),
            notes = goldfish.Notes
                .Take(8)
                .ToList(),
        };
    }

    /// <summary>
    /// Presents one route with bounded evidence rows.
    /// </summary>
    private static WinRoute PresentRoute(WinRoute route)
    {
        return new WinRoute
        {
            Name = route.Name,
            Kind = route.Kind,
            EarliestTurn = route.EarliestTurn,
            Probability = route.Probability,
            Cards = route.Cards.Take(5).ToList(),
            Rationale = route.Rationale,
            Evidence = route.Evidence.Take(3).ToList(),
        };
    }

    /// <summary>
    /// Finds a requested turn summary or the final available summary.
    /// </summary>
    private static ProjectedTurnState? FindTurnSummary(GoldfishSimulationResult goldfish, int targetTurn)
    {
        return goldfish.TurnSummaries.FirstOrDefault(summary => summary.Turn == targetTurn)
            ?? goldfish.TurnSummaries.LastOrDefault();
    }

}
