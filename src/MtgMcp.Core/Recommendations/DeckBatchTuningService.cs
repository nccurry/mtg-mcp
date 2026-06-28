namespace MtgMcp.Core;

/// <summary>
/// Provides batch read-only tuning reports across several workspaces.
/// </summary>
public sealed class DeckBatchTuningService
{
    /// <summary>
    /// Loads local workspaces without taking the full recommendation service dependency.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Supplies validation-adjacent deck metrics and best-practice analysis.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Supplies goldfish simulations for each batch row.
    /// </summary>
    private readonly DeckSimulationService simulation;

    /// <summary>
    /// Creates a batch tuning collaborator with explicit read-only dependencies.
    /// </summary>
    public DeckBatchTuningService(
        IDeckWorkspaceRepository repository,
        DeckAnalysisService analysis,
        DeckSimulationService simulation)
    {
        this.repository = repository;
        this.analysis = analysis;
        this.simulation = simulation;
    }

    /// <summary>
    /// Builds a read-only tuning report for one to eight workspaces.
    /// </summary>
    public async Task<DeckBatchTuningReport> BuildBatchTuningReportAsync(
        IReadOnlyList<string> workspaceIds,
        decimal? maxBudget,
        int targetTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await BuildBatchTuningReportAsync(
                workspaceIds,
                maxBudget,
                SimulationProfileIds.Auto,
                targetTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a read-only tuning report with a caller-selected goldfish simulation profile.
    /// </summary>
    public async Task<DeckBatchTuningReport> BuildBatchTuningReportAsync(
        IReadOnlyList<string> workspaceIds,
        decimal? maxBudget,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        List<string> inputs = [];
        foreach (string workspaceId in workspaceIds)
        {
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                continue;
            }

            string trimmed = workspaceId.Trim();
            if (!inputs.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                inputs.Add(trimmed);
            }
        }

        if (inputs.Count is < 1 or > 8)
        {
            throw new InvalidOperationException("Batch tuning report requires 1 to 8 workspace ids.");
        }

        int boundedTargetTurn = Math.Clamp(targetTurn, 1, 20);
        int boundedSimulations = Math.Clamp(simulations, 100, 10_000);
        DeckBatchTuningReport report = new()
        {
            TargetTurn = boundedTargetTurn,
            Simulations = boundedSimulations,
            Seed = seed,
            MaxBudget = maxBudget,
            Notes =
            [
                "Report is read-only and uses existing validation, analysis, bracket, and goldfish workflows.",
                "Workspace failures are returned without aborting other workspaces."
            ]
        };

        foreach (string workspaceId in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
                    .ConfigureAwait(false);
                DeckCostAnalysis cost = await analysis
                    .AnalyzeDeckCostAsync(workspaceId, maxBudget, cancellationToken)
                    .ConfigureAwait(false);
                DeckBatchTuningDeckReport deckReport = new()
                {
                    WorkspaceId = workspace.Id,
                    Name = workspace.Name,
                    Validation = DeckValidator.Validate(workspace),
                    Cost = cost,
                    Bracket = await analysis.EstimateCommanderBracketAsync(workspaceId, cancellationToken)
                        .ConfigureAwait(false),
                    Mana = await analysis.AnalyzeManaBaseAsync(workspaceId, cancellationToken)
                        .ConfigureAwait(false),
                    Consistency = await analysis.AnalyzeDeckConsistencyAsync(workspaceId, cancellationToken)
                        .ConfigureAwait(false),
                    BestPractices = await analysis.AnalyzeDeckBestPracticesAsync(workspaceId, "auto", cancellationToken)
                        .ConfigureAwait(false),
                    Goldfish = await simulation.SimulateGoldfishAsync(
                            workspaceId,
                            simulationProfile,
                            boundedTargetTurn,
                            boundedSimulations,
                            seed,
                            mulligan: true,
                            cancellationToken)
                        .ConfigureAwait(false),
                };
                AddBatchRisks(deckReport, maxBudget);
                report.Decks.Add(deckReport);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                report.Failures.Add(new DeckBatchTuningFailure
                {
                    WorkspaceId = workspaceId,
                    Reason = exception.Message
                });
            }
        }

        return report;
    }

    /// <summary>
    /// Adds concise high-priority risk strings for one batch row.
    /// </summary>
    private static void AddBatchRisks(DeckBatchTuningDeckReport deckReport, decimal? maxBudget)
    {
        if (!deckReport.Validation.IsValid)
        {
            deckReport.Risks.Add("Deck validation has errors.");
        }

        if (deckReport.Cost.BudgetStatus.Equals("over-budget", StringComparison.OrdinalIgnoreCase)
            && maxBudget.HasValue)
        {
            deckReport.Risks.Add($"Known included cost exceeds max budget {maxBudget.Value:0.##}.");
        }

        deckReport.Risks.AddRange(deckReport.Cost.PriceRiskNotes.Take(3));
        deckReport.Risks.AddRange(deckReport.Mana.Risks.Take(3));
        deckReport.Risks.AddRange(deckReport.Consistency.Risks.Take(3));
        deckReport.Risks.AddRange(deckReport.BestPractices.Risks.Take(3));
        deckReport.Risks.AddRange(deckReport.Goldfish.Warnings.Take(3));
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
