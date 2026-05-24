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
        List<GoldfishReferenceImportFailure> failures = [];
        for (int index = 0; index < referenceInputs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = referenceInputs[index];
            string label = $"reference-{index + 1}";
            if (!IsArchidektReference(input))
            {
                failures.Add(BuildImportFailure(
                    label,
                    input,
                    DetectReferenceSource(input),
                    "Only Archidekt deck ids and URLs can be imported by this tool today."));
                continue;
            }

            DeckWorkspace referenceWorkspace;
            try
            {
                referenceWorkspace = await gateway
                    .ImportDeckAsync(input, writeBack: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(BuildImportFailure(
                    label,
                    input,
                    "archidekt",
                    exception.Message));
                continue;
            }

            GoldfishSimulationResult referenceGoldfish = SimulateGoldfish(
                referenceWorkspace,
                targetTurn,
                simulations,
                seed,
                mulligan);
            GoldfishComparisonDelta delta = BuildDelta(activeGoldfish, referenceGoldfish);
            references.Add(BuildDeckComparison(
                label,
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
            ReferenceFailures = failures,
            Notes =
            [
                "Archidekt reference decks are imported read-only with writeBack=false.",
                "Every deck uses the same target turn, simulation count, seed, and mulligan setting.",
                "Deltas are reference minus active; negative medianObservedWinTurnDelta means the reference's observed wins were faster.",
                "Non-Archidekt references are returned in referenceFailures without aborting other comparisons."
            ],
            Warnings = failures
                .Select(failure => $"{failure.Label}: {failure.Reason}")
                .ToList(),
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
    /// Creates a deterministic import failure row for one reference.
    /// </summary>
    private static GoldfishReferenceImportFailure BuildImportFailure(
        string label,
        string input,
        string source,
        string reason)
    {
        return new GoldfishReferenceImportFailure
        {
            Label = label,
            Input = input,
            Source = source,
            Reason = reason,
        };
    }

    /// <summary>
    /// Determines whether the reference can be passed to the Archidekt gateway.
    /// </summary>
    private static bool IsArchidektReference(string input)
    {
        if (input.All(char.IsDigit))
        {
            return true;
        }

        return Uri.TryCreate(input, UriKind.Absolute, out Uri? uri)
            && uri.Host.Contains("archidekt.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports the likely source for a non-Archidekt reference.
    /// </summary>
    private static string DetectReferenceSource(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            return "unknown";
        }

        return uri.Host;
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
        int? medianObservedWinTurnDelta =
            active.WinEstimate.MedianObservedWinTurn.HasValue
            && reference.WinEstimate.MedianObservedWinTurn.HasValue
                ? reference.WinEstimate.MedianObservedWinTurn.Value - active.WinEstimate.MedianObservedWinTurn.Value
                : null;

        return new GoldfishComparisonDelta
        {
            BaselineWorkspaceId = active.WorkspaceId,
            ReferenceWorkspaceId = reference.WorkspaceId,
            TargetTurn = active.TargetTurn,
            MedianObservedWinTurnDelta = medianObservedWinTurnDelta,
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
