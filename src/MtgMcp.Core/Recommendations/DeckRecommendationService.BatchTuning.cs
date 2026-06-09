namespace MtgMcp.Core;

/// <summary>
/// Provides batch read-only tuning reports across several workspaces.
/// </summary>
public sealed partial class DeckRecommendationService
{
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
        List<string> inputs = workspaceIds
            .Where(workspaceId => !string.IsNullOrWhiteSpace(workspaceId))
            .Select(workspaceId => workspaceId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
}
