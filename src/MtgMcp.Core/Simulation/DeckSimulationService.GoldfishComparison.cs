namespace MtgMcp.Core;

/// <summary>
/// Provides deterministic goldfish comparisons against imported Archidekt reference decks.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Compares local workspaces and optional read-only Archidekt references with one baseline.
    /// </summary>
    public async Task<DeckGoldfishComparisonResult> CompareGoldfishAsync(
        IReadOnlyList<string> workspaceIds,
        IReadOnlyList<string>? archidektDeckIdsOrUrls,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        return await CompareGoldfishAsync(
                workspaceIds,
                archidektDeckIdsOrUrls,
                SimulationProfileIds.Auto,
                targetTurn,
                simulations,
                seed,
                mulligan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares local workspaces and optional references with a caller-selected simulation profile.
    /// </summary>
    public async Task<DeckGoldfishComparisonResult> CompareGoldfishAsync(
        IReadOnlyList<string> workspaceIds,
        IReadOnlyList<string>? archidektDeckIdsOrUrls,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        List<string> workspaceInputs = CollectReferenceInputs(workspaceIds?.ToArray() ?? []);
        List<string> archidektInputs = CollectReferenceInputs(archidektDeckIdsOrUrls?.ToArray() ?? []);
        int totalInputs = workspaceInputs.Count + archidektInputs.Count;
        if (workspaceInputs.Count == 0)
        {
            throw new InvalidOperationException("At least one local workspace id is required as the comparison baseline.");
        }

        if (totalInputs is < 2 or > 8)
        {
            throw new InvalidOperationException("Goldfish comparison requires 2 to 8 total workspace or Archidekt inputs.");
        }

        DeckWorkspace baselineWorkspace = await LoadWorkspaceAsync(workspaceInputs[0], cancellationToken)
            .ConfigureAwait(false);
        GoldfishSimulationResult baselineGoldfish = SimulateGoldfish(
            baselineWorkspace,
            simulationProfile,
            targetTurn,
            simulations,
            seed,
            mulligan,
            simulationProfiles);
        DeckGoldfishComparisonResult result = new()
        {
            WorkspaceId = baselineWorkspace.Id,
            TargetTurn = baselineGoldfish.TargetTurn,
            Simulations = baselineGoldfish.Simulations,
            Seed = seed,
            Mulligan = mulligan,
            BaselineDeck = BuildDeckComparison(
                "active",
                "workspace",
                input: workspaceInputs[0],
                baselineWorkspace,
                baselineGoldfish,
                delta: null),
            Notes =
            [
                "Every deck uses the same target turn, simulation count, seed, and mulligan setting.",
                "Deltas are compared deck minus active baseline; negative medianObservedWinTurnDelta means the compared deck's observed wins were faster.",
                "Archidekt reference decks are imported read-only with writeBack=false.",
                "Per-input failures are returned without aborting other comparisons."
            ],
        };

        for (int index = 1; index < workspaceInputs.Count; index++)
        {
            string input = workspaceInputs[index];
            string label = $"workspace-{index + 1}";
            await AddWorkspaceComparisonAsync(
                    result,
                    baselineGoldfish,
                    label,
                    input,
                    simulationProfile,
                    targetTurn,
                    simulations,
                    seed,
                    mulligan,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (archidektInputs.Count > 0)
        {
            IArchidektGateway? gateway = null;
            for (int index = 0; index < archidektInputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string input = archidektInputs[index];
                string label = $"reference-{index + 1}";
                if (!IsArchidektReference(input))
                {
                    result.Failures.Add(BuildImportFailure(
                        label,
                        input,
                        DetectReferenceSource(input),
                        "Only Archidekt deck ids and URLs can be imported by this tool today."));
                    continue;
                }

                if (gateway is null)
                {
                    try
                    {
                        gateway = RequireArchidektGateway();
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        result.Failures.Add(BuildImportFailure(
                            label,
                            input,
                            "archidekt",
                            exception.Message));
                        continue;
                    }
                }

                await AddArchidektComparisonAsync(
                        result,
                        baselineGoldfish,
                        gateway,
                        label,
                        input,
                        simulationProfile,
                        targetTurn,
                        simulations,
                        seed,
                        mulligan,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        result.Warnings = result.Failures
            .Select(failure => $"{failure.Label}: {failure.Reason}")
            .ToList();
        return result;
    }

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
        return await CompareArchidektGoldfishAsync(
                workspaceId,
                archidektDeckUrl1,
                archidektDeckUrl2,
                archidektDeckUrl3,
                SimulationProfileIds.Auto,
                targetTurn,
                simulations,
                seed,
                mulligan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compares the active workspace against Archidekt references with a caller-selected profile.
    /// </summary>
    public async Task<ArchidektGoldfishComparisonResult> CompareArchidektGoldfishAsync(
        string workspaceId,
        string? archidektDeckUrl1,
        string? archidektDeckUrl2,
        string? archidektDeckUrl3,
        string simulationProfile,
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

        DeckGoldfishComparisonResult comparison = await CompareGoldfishAsync(
                [workspaceId],
                referenceInputs,
                simulationProfile,
                targetTurn,
                simulations,
                seed,
                mulligan,
                cancellationToken)
            .ConfigureAwait(false);

        return new ArchidektGoldfishComparisonResult
        {
            WorkspaceId = comparison.WorkspaceId,
            TargetTurn = comparison.TargetTurn,
            Simulations = comparison.Simulations,
            Seed = comparison.Seed,
            Mulligan = comparison.Mulligan,
            ActiveDeck = comparison.BaselineDeck,
            ReferenceDecks = comparison.ComparedDecks,
            ReferenceFailures = comparison.Failures,
            Notes = comparison.Notes,
            Warnings = comparison.Warnings,
        };
    }

    /// <summary>
    /// Adds one local workspace comparison row or failure.
    /// </summary>
    private async Task AddWorkspaceComparisonAsync(
        DeckGoldfishComparisonResult result,
        GoldfishSimulationResult baselineGoldfish,
        string label,
        string input,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        try
        {
            DeckWorkspace workspace = await LoadWorkspaceAsync(input, cancellationToken)
                .ConfigureAwait(false);
            GoldfishSimulationResult goldfish = SimulateGoldfish(
                workspace,
                simulationProfile,
                targetTurn,
                simulations,
                seed,
                mulligan,
                simulationProfiles);
            result.ComparedDecks.Add(BuildDeckComparison(
                label,
                "workspace",
                input,
                workspace,
                goldfish,
                BuildDelta(baselineGoldfish, goldfish)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.Failures.Add(BuildImportFailure(label, input, "workspace", exception.Message));
        }
    }

    /// <summary>
    /// Adds one read-only Archidekt comparison row or failure.
    /// </summary>
    private async Task AddArchidektComparisonAsync(
        DeckGoldfishComparisonResult result,
        GoldfishSimulationResult baselineGoldfish,
        IArchidektGateway gateway,
        string label,
        string input,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        try
        {
            DeckWorkspace workspace = await gateway
                .ImportDeckAsync(input, writeBack: false, cancellationToken)
                .ConfigureAwait(false);
            GoldfishSimulationResult goldfish = SimulateGoldfish(
                workspace,
                simulationProfile,
                targetTurn,
                simulations,
                seed,
                mulligan,
                simulationProfiles);
            result.ComparedDecks.Add(BuildDeckComparison(
                label,
                "archidekt",
                input,
                workspace,
                goldfish,
                BuildDelta(baselineGoldfish, goldfish)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.Failures.Add(BuildImportFailure(label, input, "archidekt", exception.Message));
        }
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
