namespace MtgMcp.Core;

/// <summary>
/// Provides deterministic goldfish comparisons against imported Archidekt reference decks.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Compares the active workspace against up to three read-only Archidekt reference decks.
    /// </summary>
    public async Task<ArchidektGoldfishComparisonResult> CompareArchidektGoldfishAsync(
        string workspaceId,
        string? archidektDeckUrl1,
        string? archidektDeckUrl2,
        string? archidektDeckUrl3,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        List<string> referenceInputs = CollectReferenceInputs(archidektDeckUrl1, archidektDeckUrl2, archidektDeckUrl3);
        if (referenceInputs.Count == 0)
        {
            throw new InvalidOperationException("At least one Archidekt reference deck id or URL is required.");
        }

        DeckWorkspace activeWorkspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        GoldfishSimulationResult activeGoldfish = SimulateGoldfish(
            activeWorkspace,
            targetTurn,
            simulations,
            seed,
            mulligan);
        GoldfishDeckComparison activeDeck = BuildDeckComparison(
            "active",
            "workspace",
            input: null,
            activeWorkspace,
            activeGoldfish,
            delta: null);

        IArchidektGateway gateway = RequireArchidektGateway();
        List<GoldfishDeckComparison> references = [];
        for (int index = 0; index < referenceInputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = referenceInputs[index];
            DeckWorkspace referenceWorkspace = await gateway
                .ImportDeckAsync(input, writeBack: false, cancellationToken)
                .ConfigureAwait(false);
            GoldfishSimulationResult referenceGoldfish = SimulateGoldfish(
                referenceWorkspace,
                targetTurn,
                simulations,
                seed,
                mulligan);
            GoldfishComparisonDelta delta = BuildDelta(activeGoldfish, referenceGoldfish);
            references.Add(BuildDeckComparison(
                $"reference-{index + 1}",
                "archidekt",
                input,
                referenceWorkspace,
                referenceGoldfish,
                delta));
        }

        return new ArchidektGoldfishComparisonResult
        {
            WorkspaceId = activeWorkspace.Id,
            TargetTurn = activeGoldfish.TargetTurn,
            Simulations = activeGoldfish.Simulations,
            Seed = seed,
            Mulligan = mulligan,
            ActiveDeck = activeDeck,
            ReferenceDecks = references,
            Notes =
            [
                "Archidekt reference decks are imported read-only with writeBack=false.",
                "Every deck uses the same target turn, simulation count, seed, and mulligan setting.",
                "Deltas are reference minus active; negative medianWinTurnDelta means the reference goldfished faster."
            ],
        };
    }

    /// <summary>
    /// Collects non-empty reference inputs in caller order.
    /// </summary>
    private static List<string> CollectReferenceInputs(params string?[] inputs)
    {
        return inputs
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .Select(input => input!.Trim())
            .ToList();
    }

    /// <summary>
    /// Creates a comparison row for one deck.
    /// </summary>
    private static GoldfishDeckComparison BuildDeckComparison(
        string label,
        string source,
        string? input,
        DeckWorkspace workspace,
        GoldfishSimulationResult goldfish,
        GoldfishComparisonDelta? delta)
    {
        return new GoldfishDeckComparison
        {
            Label = label,
            Source = source,
            Input = input,
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            ArchidektDeckId = workspace.ArchidektDeckId,
            IncludedCards = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity)),
            Goldfish = goldfish,
            DeltaFromActive = delta,
        };
    }

    /// <summary>
    /// Calculates arithmetic deltas from active to reference results.
    /// </summary>
    private static GoldfishComparisonDelta BuildDelta(
        GoldfishSimulationResult active,
        GoldfishSimulationResult reference)
    {
        ProjectedTurnState activeTurn = GetTurnSummary(active, active.TargetTurn);
        ProjectedTurnState referenceTurn = GetTurnSummary(reference, reference.TargetTurn);
        int? medianWinTurnDelta =
            active.WinEstimate.MedianWinTurn.HasValue
            && reference.WinEstimate.MedianWinTurn.HasValue
                ? reference.WinEstimate.MedianWinTurn.Value - active.WinEstimate.MedianWinTurn.Value
                : null;

        return new GoldfishComparisonDelta
        {
            BaselineWorkspaceId = active.WorkspaceId,
            ReferenceWorkspaceId = reference.WorkspaceId,
            TargetTurn = active.TargetTurn,
            MedianWinTurnDelta = medianWinTurnDelta,
            TargetTurnWinRateDelta = WinRateByTurn(reference, reference.TargetTurn)
                - WinRateByTurn(active, active.TargetTurn),
            MulliganRateDelta = MulliganRate(reference) - MulliganRate(active),
            MedianLandsDelta = referenceTurn.MedianLands - activeTurn.MedianLands,
            MedianManaSourcesDelta = referenceTurn.MedianManaSources - activeTurn.MedianManaSources,
            MedianNonlandPermanentsDelta = referenceTurn.MedianNonlandPermanents
                - activeTurn.MedianNonlandPermanents,
            MedianCardsInHandDelta = referenceTurn.MedianCardsInHand - activeTurn.MedianCardsInHand,
            MedianTokensDelta = referenceTurn.MedianTokens - activeTurn.MedianTokens,
        };
    }

    /// <summary>
    /// Finds a turn summary, falling back to the final summary when the exact turn is unavailable.
    /// </summary>
    private static ProjectedTurnState GetTurnSummary(GoldfishSimulationResult result, int targetTurn)
    {
        return result.TurnSummaries.FirstOrDefault(summary => summary.Turn == targetTurn)
            ?? result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState { Turn = targetTurn };
    }

    /// <summary>
    /// Reads a cumulative win rate for a turn.
    /// </summary>
    private static double WinRateByTurn(GoldfishSimulationResult result, int turn)
    {
        return result.WinEstimate.WinByTurnRates.TryGetValue(turn, out double rate)
            ? rate
            : 0;
    }

    /// <summary>
    /// Calculates the fraction of simulations that took a simple mulligan.
    /// </summary>
    private static double MulliganRate(GoldfishSimulationResult result)
    {
        return result.Simulations > 0
            ? (double)result.Mulligans / result.Simulations
            : 0;
    }
}
