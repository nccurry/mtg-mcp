namespace MtgMcp.Core;

/// <summary>
/// Previews generated deck edit plans without mutating local or remote state.
/// </summary>
public sealed partial class DeckPlanService
{
    /// <summary>
    /// Previews a deck edit plan without mutating local or remote state.
    /// </summary>
    public async Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        PlanPreviewWorkspaceResult result = await PreviewPlanWithWorkspacesAsync(
                plan,
                resolveAddedCards,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Preview;
    }

    /// <summary>
    /// Previews caller-supplied card package operations without saving a plan.
    /// </summary>
    public async Task<DeckCardPackagePreviewResult> PreviewCardPackageAsync(
        string workspaceId,
        string? name,
        string? rationale,
        IReadOnlyList<ExplicitDeckPlanCardChange>? addCards,
        IReadOnlyList<ExplicitDeckPlanCardChange>? removeCards,
        IReadOnlyList<ExplicitDeckPlanMoveCardChange>? moveCards,
        bool resolveAddedCards,
        string simulationProfile,
        int simulations,
        int maxTurn,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckEditPlan plan = CreatePlan(
            workspace,
            string.IsNullOrWhiteSpace(name) ? "Transient card package preview" : name.Trim(),
            "transient-card-package");
        plan.Rationale = rationale?.Trim() ?? "";
        plan.Confidence = 1;

        AddCardOperations(plan, addCards);
        AddRemoveOperations(plan, removeCards);
        AddMoveOperations(plan, moveCards);
        if (plan.Operations.Count == 0)
        {
            throw new InvalidOperationException("At least one package add, remove, or move is required.");
        }

        PlanPreviewWorkspaceResult previewResult = await PreviewPlanWithWorkspacesAsync(
                plan,
                resolveAddedCards,
                cancellationToken)
            .ConfigureAwait(false);
        DeckPerformanceAnalysis beforePerformance = DeckPerformanceAnalyzer.Analyze(
            previewResult.BeforeWorkspace,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);
        DeckPerformanceAnalysis afterPerformance = DeckPerformanceAnalyzer.Analyze(
            previewResult.AfterWorkspace,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);

        DeckCardPackagePreviewResult result = new()
        {
            WorkspaceId = workspace.Id,
            Plan = plan,
            Preview = previewResult.Preview,
            RoleDeltas = BuildRoleDeltas(
                previewResult.Preview.Before.Analysis.RoleCounts,
                previewResult.Preview.After.Analysis.RoleCounts),
            ValidationChanges = BuildValidationDelta(
                previewResult.Preview.Before.Validation,
                previewResult.Preview.After.Validation),
            PriceDelta = BuildPriceDelta(
                previewResult.Preview.Before.Cost,
                previewResult.Preview.After.Cost),
            BracketImpact = BuildBracketImpact(
                previewResult.Preview.Before.Bracket,
                previewResult.Preview.After.Bracket),
            SourceSupport = BuildPackageSourceSupport(plan),
            Performance = new DeckPerformanceComparison
            {
                PlanId = plan.PlanId,
                WorkspaceId = plan.WorkspaceId,
                Before = beforePerformance,
                After = afterPerformance,
                Deltas = DeckPerformanceComparisonBuilder.BuildDeltas(beforePerformance, afterPerformance),
                Warnings = previewResult.Preview.Warnings
                    .Concat(beforePerformance.Warnings.Select(warning => $"Before: {warning}"))
                    .Concat(afterPerformance.Warnings.Select(warning => $"After: {warning}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            },
            Warnings = previewResult.Preview.Warnings
        };

        return result;
    }

    /// <summary>
    /// Builds preview metrics and keeps both before and after workspaces for transient comparisons.
    /// </summary>
    private async Task<PlanPreviewWorkspaceResult> PreviewPlanWithWorkspacesAsync(
        DeckEditPlan plan,
        bool resolveAddedCards,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        DeckPlanPreviewer previewer = new(CardCatalog);
        DeckWorkspace preview = previewer.CloneWorkspace(workspace);
        List<string> warnings = [];
        IReadOnlySet<string> gameChangers;
        bool gameChangerDataAvailable = true;
        try
        {
            gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            gameChangers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            gameChangerDataAvailable = false;
            warnings.Add($"{exception.Message} Preview metrics exclude live Game Changer signals.");
        }

        foreach (DeckEditOperation operation in plan.Operations)
        {
            await previewer.ApplyOperationAsync(preview, operation, resolveAddedCards, warnings, cancellationToken)
                .ConfigureAwait(false);
        }

        DeckPlanPreviewResult previewResult = new()
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            ResolveAddedCards = resolveAddedCards,
            Before = BuildMetricSnapshot(workspace, gameChangers, gameChangerDataAvailable),
            After = BuildMetricSnapshot(preview, gameChangers, gameChangerDataAvailable),
            Warnings = warnings
        };

        return new PlanPreviewWorkspaceResult(workspace, preview, previewResult);
    }

    /// <summary>
    /// Builds sorted role deltas between two metric snapshots.
    /// </summary>
    private static List<DeckRoleCountDelta> BuildRoleDeltas(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after)
    {
        HashSet<string> roles = new(before.Keys, StringComparer.OrdinalIgnoreCase);
        roles.UnionWith(after.Keys);
        List<DeckRoleCountDelta> deltas = [];
        foreach (string role in roles)
        {
            int beforeCount = before.GetValueOrDefault(role);
            int afterCount = after.GetValueOrDefault(role);
            int delta = afterCount - beforeCount;
            if (delta == 0)
            {
                continue;
            }

            deltas.Add(new DeckRoleCountDelta
            {
                Role = role,
                Before = beforeCount,
                After = afterCount,
                Delta = delta
            });
        }

        deltas.Sort((left, right) => string.Compare(left.Role, right.Role, StringComparison.OrdinalIgnoreCase));
        return deltas;
    }

    /// <summary>
    /// Builds validation deltas between preview snapshots.
    /// </summary>
    private static DeckValidationDelta BuildValidationDelta(
        DeckValidationResult before,
        DeckValidationResult after)
    {
        return new DeckValidationDelta
        {
            AddedErrors = Difference(after.Errors, before.Errors),
            RemovedErrors = Difference(before.Errors, after.Errors),
            AddedWarnings = Difference(after.Warnings, before.Warnings),
            RemovedWarnings = Difference(before.Warnings, after.Warnings)
        };
    }

    /// <summary>
    /// Builds included-total price delta.
    /// </summary>
    private static DeckPriceDelta BuildPriceDelta(DeckCostAnalysis before, DeckCostAnalysis after)
    {
        return new DeckPriceDelta
        {
            BeforeIncludedTotal = before.IncludedTotal,
            AfterIncludedTotal = after.IncludedTotal,
            IncludedTotalDelta = after.IncludedTotal - before.IncludedTotal
        };
    }

    /// <summary>
    /// Builds bracket impact from before and after estimates.
    /// </summary>
    private static DeckBracketImpact BuildBracketImpact(
        CommanderBracketEstimate before,
        CommanderBracketEstimate after)
    {
        return new DeckBracketImpact
        {
            BeforeEstimatedBracket = before.EstimatedBracket,
            AfterEstimatedBracket = after.EstimatedBracket,
            EstimatedBracketDelta = after.EstimatedBracket - before.EstimatedBracket,
            BeforeGameChangerCount = before.GameChangerCount,
            AfterGameChangerCount = after.GameChangerCount
        };
    }

    /// <summary>
    /// Builds deterministic package source rows without contacting recommendation providers.
    /// </summary>
    private static List<DeckPackageSourceSupport> BuildPackageSourceSupport(DeckEditPlan plan)
    {
        List<DeckPackageSourceSupport> rows = [];
        foreach (DeckEditOperation operation in plan.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.CardName))
            {
                continue;
            }

            rows.Add(new DeckPackageSourceSupport
            {
                CardName = operation.CardName,
                Operation = operation.Operation,
                Status = "not-evaluated",
                Notes =
                [
                    "Transient package preview keeps source-provider research separate; use source_explain_card_signal "
                        + "or source_search_evidence for source-backed support."
                ],
            });
        }

        return rows;
    }

    /// <summary>
    /// Returns values in left that are absent from right.
    /// </summary>
    private static List<string> Difference(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        List<string> result = [];
        foreach (string value in left)
        {
            if (!right.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>
    /// Carries preview metrics plus the workspaces used to produce them.
    /// </summary>
    private sealed record PlanPreviewWorkspaceResult(
        DeckWorkspace BeforeWorkspace,
        DeckWorkspace AfterWorkspace,
        DeckPlanPreviewResult Preview);
}
