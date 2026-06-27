using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes a compact deck re-evaluation MCP tool composed from existing read-only analyses.
/// </summary>
[McpServerToolType]
public sealed class DeckReEvaluationTools
{
    /// <summary>
    /// Uses a compact default that leaves room for every re-evaluation section.
    /// </summary>
    private const int DefaultLimit = 8;

    /// <summary>
    /// Caps each section so the tool remains safe for MCP responses.
    /// </summary>
    private const int MaxLimit = 20;

    /// <summary>
    /// Provides validation and workspace state reads.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Provides mana, consistency, role-balance, and weak-slot analyses.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Provides optional source-backed recommendation evidence.
    /// </summary>
    private readonly DeckRecommendationService? recommendations;

    /// <summary>
    /// Provides optional performance snapshots for analysis comparison.
    /// </summary>
    private readonly DeckSimulationService? simulation;

    /// <summary>
    /// Creates deck re-evaluation tools for the MCP surface.
    /// </summary>
    public DeckReEvaluationTools(
        DeckWorkspaceService decks,
        DeckAnalysisService analysis,
        DeckRecommendationService? recommendations = null,
        DeckSimulationService? simulation = null)
    {
        this.decks = decks;
        this.analysis = analysis;
        this.recommendations = recommendations;
        this.simulation = simulation;
    }

    /// <summary>
    /// Re-runs the core deck health checks and returns a bounded tuning snapshot.
    /// </summary>
    [McpServerTool(Name = "deck_re_evaluate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compact re-evaluation of an updated workspace: validation, role balance, mana, consistency, top risks, suspected cuts, and sideboard/maybeboard upgrade candidates.")]
    public async Task<object> ReEvaluateDeckAsync(
        string workspaceId,
        [Description("Heuristic analysis profile: auto or a documented deck intent Heuristic Profile value.")]
        string analysisProfile = "auto",
        [Description("Maximum rows per bounded list. Values are clamped from 1 to 20.")]
        int limit = DefaultLimit,
        [Description("Whether to query configured recommendation sources for commander trend evidence.")]
        bool includeSourceEvidence = false,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? sourceAnalysisDepth = null,
        [Description("Maximum source-backed recommendation rows. Defaults from limit and is capped at 10.")]
        int? sourceLimit = null,
        [Description("Whether to bypass fresh source-evidence cache entries when source evidence is enabled.")]
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        int boundedLimit = Math.Clamp(limit, 1, MaxLimit);
        int boundedSourceLimit = Math.Clamp(sourceLimit ?? boundedLimit, 1, 10);
        Task<DeckValidationResult> validationTask = decks.ValidateDeckAsync(workspaceId, cancellationToken);
        Task<ManaBaseAnalysis> manaTask = analysis.AnalyzeManaBaseAsync(workspaceId, cancellationToken);
        Task<DeckConsistencyAnalysis> consistencyTask = analysis.AnalyzeDeckConsistencyAsync(workspaceId, cancellationToken);
        Task<DeckWeakSpotReview> weakReviewTask = analysis.ReviewWeakSpotsAsync(
            workspaceId,
            analysisProfile,
            boundedLimit,
            cancellationToken);

        await Task.WhenAll(validationTask, manaTask, consistencyTask, weakReviewTask).ConfigureAwait(false);

        DeckValidationResult validation = await validationTask.ConfigureAwait(false);
        ManaBaseAnalysis mana = await manaTask.ConfigureAwait(false);
        DeckConsistencyAnalysis consistency = await consistencyTask.ConfigureAwait(false);
        DeckWeakSpotReview weakReview = await weakReviewTask.ConfigureAwait(false);
        object sourceRecommendations = includeSourceEvidence
            ? await BuildSourceRecommendationsAsync(
                    workspaceId,
                    boundedSourceLimit,
                    sourceAnalysisDepth,
                    bypassCache,
                    cancellationToken)
                .ConfigureAwait(false)
            : new
            {
                Status = "notQueried",
                Notes = new[]
                {
                    "Default re-evaluation uses saved workspace evidence only; set includeSourceEvidence=true when live corpus evidence is needed."
                }
            };

        return new
        {
            WorkspaceId = workspaceId,
            DetailLevel = "summary",
            AnalysisProfile = analysisProfile,
            Limit = boundedLimit,
            Validation = SummarizeValidation(validation, boundedLimit),
            Mana = SummarizeMana(mana, boundedLimit),
            Consistency = SummarizeConsistency(consistency, boundedLimit),
            RoleBalance = BuildRoleBalance(weakReview, boundedLimit),
            TopRisks = BuildTopRisks(validation, mana, consistency, weakReview, boundedLimit),
            TopSuspectedCuts = BuildSuspectedCuts(weakReview, boundedLimit),
            BestExcludedUpgrades = BuildExcludedUpgrades(weakReview, boundedLimit),
            SourceRecommendations = sourceRecommendations,
            SourceStatuses = weakReview.SourceStatuses,
            Notes = TakeStrings(weakReview.Notes, boundedLimit)
        };
    }

    /// <summary>
    /// Compares current deck health against an explicit or last-import baseline.
    /// </summary>
    [McpServerTool(Name = "deck_compare_workspaces_analysis", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare validation, legality, mana, consistency, cost, role balance, weak slots, risks, and optional compact performance between a workspace and a baseline.")]
    public async Task<object> CompareWorkspacesAnalysisAsync(
        string workspaceId,
        string baselineMode = "last-import",
        string? baselineWorkspaceId = null,
        [Description("Heuristic analysis profile: auto or a documented deck intent Heuristic Profile value.")]
        string analysisProfile = "auto",
        bool includePerformance = false,
        [Description("Output detail level: summary, normal, or full.")]
        string detailLevel = "summary",
        [Description("Maximum rows per bounded list. Values are clamped from 1 to 20.")]
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        string normalizedDetailLevel = NormalizeAnalysisDetailLevel(detailLevel);
        string normalizedBaselineMode = NormalizeBaselineMode(baselineMode);
        int boundedLimit = Math.Clamp(limit, 1, MaxLimit);
        DeckWorkspace current = await decks.OpenLocalDeckAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        (WorkspaceDiffLastImportStatus baselineStatus, DeckWorkspace? baselineWorkspace, List<string> baselineNotes) = await ResolveBaselineAsync(
                normalizedBaselineMode,
                baselineWorkspaceId,
                workspaceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (baselineWorkspace is null)
        {
            return new
            {
                Status = baselineStatus,
                WorkspaceId = workspaceId,
                BaselineMode = normalizedBaselineMode,
                BaselineWorkspaceId = baselineWorkspaceId,
                Notes = baselineNotes
            };
        }

        WorkspaceAnalysisBundle before = BuildAnalysisBundle(baselineWorkspace, analysisProfile, boundedLimit);
        WorkspaceAnalysisBundle after = BuildAnalysisBundle(current, analysisProfile, boundedLimit);
        WorkspaceDiffResult workspaceDiff = decks.DiffWorkspaceSnapshots(current, baselineWorkspace);
        object performance = includePerformance
            ? BuildPerformanceComparison(
                baselineWorkspace,
                current,
                analysisProfile,
                normalizedDetailLevel,
                cancellationToken)
            : new { Status = "notRequested" };

        return new
        {
            Status = "compared",
            DetailLevel = normalizedDetailLevel,
            WorkspaceId = workspaceId,
            BaselineMode = normalizedBaselineMode,
            BaselineWorkspaceId = baselineWorkspace.Id,
            AnalysisProfile = analysisProfile,
            Limit = boundedLimit,
            Baseline = PresentAnalysisBundle(before, normalizedDetailLevel, boundedLimit),
            Current = PresentAnalysisBundle(after, normalizedDetailLevel, boundedLimit),
            Deltas = BuildAnalysisDeltas(before, after, workspaceDiff),
            WorkspaceDiff = normalizedDetailLevel == DetailLevelParser.Full
                ? workspaceDiff
                : SummarizeWorkspaceDiff(workspaceDiff, boundedLimit),
            Performance = performance,
            Notes = baselineNotes
        };
    }

    /// <summary>
    /// Normalizes analysis comparison detail levels.
    /// </summary>
    private static string NormalizeAnalysisDetailLevel(string? detailLevel)
    {
        return DetailLevelParser.Normalize(detailLevel);
    }

    /// <summary>
    /// Normalizes analysis comparison baseline modes.
    /// </summary>
    private static string NormalizeBaselineMode(string? baselineMode)
    {
        string normalized = string.IsNullOrWhiteSpace(baselineMode)
            ? "last-import"
            : baselineMode.Trim().ToLowerInvariant();
        if (normalized is "last-import" or "explicit")
        {
            return normalized;
        }

        throw new ArgumentException("baselineMode must be last-import or explicit.", nameof(baselineMode));
    }

    /// <summary>
    /// Resolves the requested analysis baseline without hiding unavailable states.
    /// </summary>
    private async Task<(WorkspaceDiffLastImportStatus Status, DeckWorkspace? Baseline, List<string> Notes)> ResolveBaselineAsync(
        string normalizedBaselineMode,
        string? baselineWorkspaceId,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        if (normalizedBaselineMode == "explicit")
        {
            if (string.IsNullOrWhiteSpace(baselineWorkspaceId))
            {
                throw new ArgumentException(
                    "baselineWorkspaceId is required when baselineMode is explicit.",
                    nameof(baselineWorkspaceId));
            }

            DeckWorkspace baseline = await decks.OpenLocalDeckAsync(baselineWorkspaceId, cancellationToken)
                .ConfigureAwait(false);
            return (WorkspaceDiffLastImportStatus.BaselineFound, baseline, [$"Compared against explicit baseline workspace '{baseline.Id}'."]);
        }

        WorkspaceImportBaselineResolution resolution = await decks
            .GetLastImportBaselineAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return resolution.BaselineWorkspace is null
            ? (resolution.Status, null, resolution.Notes)
            : (resolution.Status, resolution.BaselineWorkspace, resolution.Notes);
    }

    /// <summary>
    /// Runs the local analysis suite for one workspace snapshot.
    /// </summary>
    private WorkspaceAnalysisBundle BuildAnalysisBundle(
        DeckWorkspace workspace,
        string analysisProfile,
        int limit)
    {
        return new WorkspaceAnalysisBundle
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Validation = DeckValidator.Validate(workspace),
            Legality = decks.ValidateLegalitySnapshot(workspace, includeExcluded: false),
            Mana = analysis.AnalyzeManaBaseSnapshot(workspace),
            Consistency = analysis.AnalyzeDeckConsistencySnapshot(workspace),
            Cost = analysis.AnalyzeDeckCostSnapshot(workspace),
            WeakReview = analysis.ReviewWeakSpotsSnapshot(workspace, analysisProfile, limit)
        };
    }

    /// <summary>
    /// Presents one analysis bundle at the requested detail level.
    /// </summary>
    private static object PresentAnalysisBundle(WorkspaceAnalysisBundle bundle, string detailLevel, int limit)
    {
        if (detailLevel == DetailLevelParser.Full)
        {
            return new
            {
                bundle.WorkspaceId,
                bundle.Name,
                bundle.Validation,
                bundle.Legality,
                bundle.Mana,
                bundle.Consistency,
                bundle.Cost,
                bundle.WeakReview
            };
        }

        return new
        {
            bundle.WorkspaceId,
            bundle.Name,
            Validation = SummarizeValidation(bundle.Validation, limit),
            Legality = SummarizeLegality(bundle.Legality, limit),
            Mana = SummarizeMana(bundle.Mana, limit),
            Consistency = SummarizeConsistency(bundle.Consistency, limit),
            Cost = SummarizeCost(bundle.Cost, limit),
            RoleBalance = BuildRoleBalance(bundle.WeakReview, limit),
            TopRisks = BuildTopRisks(
                bundle.Validation,
                bundle.Mana,
                bundle.Consistency,
                bundle.WeakReview,
                limit),
            TopSuspectedCuts = BuildSuspectedCuts(bundle.WeakReview, limit),
            BestExcludedUpgrades = BuildExcludedUpgrades(bundle.WeakReview, limit)
        };
    }

    /// <summary>
    /// Builds bounded deltas for the major analysis dimensions.
    /// </summary>
    private static object BuildAnalysisDeltas(
        WorkspaceAnalysisBundle before,
        WorkspaceAnalysisBundle after,
        WorkspaceDiffResult workspaceDiff)
    {
        return new
        {
            workspaceDiff.IncludedCountDelta,
            Validation = new
            {
                ErrorDelta = after.Validation.Errors.Count - before.Validation.Errors.Count,
                WarningDelta = after.Validation.Warnings.Count - before.Validation.Warnings.Count,
                AddedErrors = workspaceDiff.ValidationDelta.AddedErrors,
                RemovedErrors = workspaceDiff.ValidationDelta.RemovedErrors,
                AddedWarnings = workspaceDiff.ValidationDelta.AddedWarnings,
                RemovedWarnings = workspaceDiff.ValidationDelta.RemovedWarnings
            },
            Legality = new
            {
                ErrorDelta = CountLegalityErrors(after.Legality) - CountLegalityErrors(before.Legality),
                WarningDelta = CountLegalityWarnings(after.Legality) - CountLegalityWarnings(before.Legality),
                IsLegalBefore = before.Legality.IsLegal,
                IsLegalAfter = after.Legality.IsLegal
            },
            Mana = new
            {
                LandDelta = after.Mana.LandCount - before.Mana.LandCount,
                LandSlotDelta = after.Mana.LandSlotCount - before.Mana.LandSlotCount,
                FixingDelta = after.Mana.FixingCount - before.Mana.FixingCount,
                RampFixingDelta = after.Mana.RampFixingCount - before.Mana.RampFixingCount,
                TappedLandDelta = after.Mana.TappedLandCount - before.Mana.TappedLandCount
            },
            Consistency = new
            {
                DeckSizeDelta = after.Consistency.DeckSize - before.Consistency.DeckSize,
                RampDelta = after.Consistency.RampCount - before.Consistency.RampCount,
                DrawDelta = after.Consistency.DrawCount - before.Consistency.DrawCount,
                TutorDelta = after.Consistency.TutorCount - before.Consistency.TutorCount,
                CardSelectionDelta = after.Consistency.CardSelectionCount - before.Consistency.CardSelectionCount,
                LowCurveNonlandDelta = after.Consistency.LowCurveNonlandCount - before.Consistency.LowCurveNonlandCount
            },
            Cost = new
            {
                IncludedTotalDelta = after.Cost.IncludedTotal - before.Cost.IncludedTotal,
                MaybeboardTotalDelta = after.Cost.MaybeboardTotal - before.Cost.MaybeboardTotal,
                PricedIncludedCardDelta = after.Cost.PricedIncludedCards - before.Cost.PricedIncludedCards,
                MissingPriceCardDelta = after.Cost.MissingPriceCards.Count - before.Cost.MissingPriceCards.Count
            },
            WeakSlots = new
            {
                WeakSlotDelta = after.WeakReview.WeakSlots.Count - before.WeakReview.WeakSlots.Count,
                CandidateDelta = after.WeakReview.CandidateRows.Count - before.WeakReview.CandidateRows.Count,
                ImbalanceDelta = CountImbalances(after.WeakReview) - CountImbalances(before.WeakReview)
            }
        };
    }

    /// <summary>
    /// Builds a compact workspace diff payload.
    /// </summary>
    private static object SummarizeWorkspaceDiff(WorkspaceDiffResult diff, int limit)
    {
        return new
        {
            diff.WorkspaceId,
            diff.PreviousWorkspaceId,
            diff.Baseline,
            diff.Current,
            diff.IncludedCountBefore,
            diff.IncludedCountAfter,
            diff.IncludedCountDelta,
            Counts = new
            {
                AddedCards = diff.AddedCards.Count,
                RemovedCards = diff.RemovedCards.Count,
                PrimaryMoves = diff.PrimaryMoves.Count,
                SecondaryTagChanges = diff.SecondaryTagChanges.Count,
                QuantityChanges = diff.QuantityChanges.Count
            },
            AddedCards = SummarizeDiffRows(diff.AddedCards, limit),
            RemovedCards = SummarizeDiffRows(diff.RemovedCards, limit),
            PrimaryMoves = SummarizeDiffRows(diff.PrimaryMoves, limit),
            SecondaryTagChanges = SummarizeDiffRows(diff.SecondaryTagChanges, limit),
            QuantityChanges = SummarizeDiffRows(diff.QuantityChanges, limit),
            ValidationDelta = diff.ValidationDelta,
            Notes = TakeStrings(diff.Notes, limit)
        };
    }

    /// <summary>
    /// Runs a bounded performance comparison for two workspace snapshots.
    /// </summary>
    private object BuildPerformanceComparison(
        DeckWorkspace baseline,
        DeckWorkspace current,
        string analysisProfile,
        string detailLevel,
        CancellationToken cancellationToken)
    {
        if (simulation is null)
        {
            return new
            {
                Status = "unavailable",
                Notes = new[] { "Performance comparison is not wired in this host instance." }
            };
        }

        const int simulations = 1000;
        const int maxTurn = 8;
        const int seed = 1337;
        DeckPerformanceAnalysis before = simulation.AnalyzeDeckPerformanceSnapshot(
            baseline,
            analysisProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);
        DeckPerformanceAnalysis after = simulation.AnalyzeDeckPerformanceSnapshot(
            current,
            analysisProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);

        return new
        {
            Status = "compared",
            Settings = new
            {
                Simulations = simulations,
                MaxTurn = maxTurn,
                Seed = seed,
                IncludeMulligans = true
            },
            Before = PerformanceOutputPresenter.Present(before, detailLevel == DetailLevelParser.Full ? DetailLevelParser.Normal : detailLevel),
            After = PerformanceOutputPresenter.Present(after, detailLevel == DetailLevelParser.Full ? DetailLevelParser.Normal : detailLevel)
        };
    }

    /// <summary>
    /// Queries source-backed commander trend evidence when explicitly requested.
    /// </summary>
    private async Task<object> BuildSourceRecommendationsAsync(
        string workspaceId,
        int limit,
        string? sourceAnalysisDepth,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (recommendations is null)
        {
            return new
            {
                Status = "unavailable",
                Notes = new[] { "Source-backed recommendations are not wired in this host instance." }
            };
        }

        CorpusRecommendationResult result = await recommendations
            .AnalyzeCommanderTrendsAsync(
                workspaceId,
                limit,
                sourceAnalysisDepth,
                refresh: bypassCache,
                cancellationToken)
            .ConfigureAwait(false);
        return new
        {
            Status = "queried",
            result.WorkspaceId,
            result.Commander,
            result.Theme,
            result.AnalysisDepth,
            Limit = limit,
            RecommendationCount = result.Recommendations.Count,
            Recommendations = SummarizeCorpusRecommendations(result.Recommendations, limit),
            Sources = SummarizeCorpusSources(result.Sources, limit),
            ExemplarDecks = SummarizeExemplarDecks(result.ExemplarDecks, Math.Min(limit, 5)),
            Notes = TakeStrings(result.Notes, limit)
        };
    }

    /// <summary>
    /// Builds a bounded validation summary.
    /// </summary>
    private static object SummarizeValidation(DeckValidationResult validation, int limit)
    {
        return new
        {
            validation.IsValid,
            ErrorCount = validation.Errors.Count,
            WarningCount = validation.Warnings.Count,
            Errors = TakeStrings(validation.Errors, limit),
            Warnings = TakeStrings(validation.Warnings, limit)
        };
    }

    /// <summary>
    /// Builds a bounded mana-base summary.
    /// </summary>
    private static object SummarizeMana(ManaBaseAnalysis mana, int limit)
    {
        return new
        {
            mana.LandCount,
            mana.LandSlotCount,
            mana.ManaProducingLandCount,
            mana.TappedLandCount,
            mana.AlwaysTappedLandCount,
            mana.ConditionalTappedLandCount,
            mana.UntappedLandCount,
            mana.FixingCount,
            mana.RampFixingCount,
            mana.ColorSources,
            mana.ProducedManaSources,
            Risks = TakeStrings(mana.Risks, limit),
            TappedLandContributors = BuildTappedLandContributors(mana, Math.Min(limit, 5))
        };
    }

    /// <summary>
    /// Builds a bounded consistency summary.
    /// </summary>
    private static object SummarizeConsistency(DeckConsistencyAnalysis consistency, int limit)
    {
        return new
        {
            consistency.DeckSize,
            consistency.RampCount,
            consistency.DrawCount,
            consistency.TutorCount,
            consistency.CardSelectionCount,
            consistency.LowCurveNonlandCount,
            consistency.FunctionalRoleCounts,
            Risks = TakeStrings(consistency.Risks, limit),
            KeyOdds = BuildKeyOdds(consistency, Math.Min(limit, 5))
        };
    }

    /// <summary>
    /// Builds a bounded legality summary.
    /// </summary>
    private static object SummarizeLegality(DeckLegalityAudit legality, int limit)
    {
        return new
        {
            legality.IsLegal,
            legality.Format,
            legality.IncludeExcluded,
            legality.IncludedCount,
            legality.AuditedCardRows,
            legality.CommandZone,
            ErrorCount = CountLegalityErrors(legality),
            WarningCount = CountLegalityWarnings(legality),
            Errors = TakeStrings(legality.Errors, limit),
            Warnings = TakeStrings(legality.Warnings, limit),
            CardLegalityIssues = SummarizeLegalityIssues(legality.CardLegalityIssues, limit),
            ColorIdentityIssues = SummarizeLegalityIssues(legality.ColorIdentityIssues, limit),
            CopyLimitIssues = SummarizeLegalityIssues(legality.CopyLimitIssues, limit),
            SideboardIssues = SummarizeLegalityIssues(legality.SideboardIssues, limit),
            MetadataGaps = SummarizeLegalityIssues(legality.MetadataGaps, limit),
            Assumptions = TakeStrings(legality.Assumptions, limit)
        };
    }

    /// <summary>
    /// Builds a bounded price and budget summary.
    /// </summary>
    private static object SummarizeCost(DeckCostAnalysis cost, int limit)
    {
        return new
        {
            cost.IncludedTotal,
            cost.MaybeboardTotal,
            cost.MaxBudget,
            cost.WithinBudget,
            cost.BudgetDelta,
            cost.BudgetStatus,
            cost.PriceRiskStatus,
            cost.PricedIncludedCards,
            MissingPriceCardCount = cost.MissingPriceCards.Count,
            UnresolvedMissingPriceCardCount = cost.UnresolvedMissingPriceCards.Count,
            PriceRiskNotes = TakeStrings(cost.PriceRiskNotes, limit),
            TopCostDrivers = SummarizeCostDrivers(cost.TopCostDrivers, limit)
        };
    }

    /// <summary>
    /// Builds role and tag balance rows, keeping imbalances first.
    /// </summary>
    private static List<object> BuildRoleBalance(DeckWeakSpotReview weakReview, int limit)
    {
        List<object> rows = [];
        foreach (DeckWeakSpotBalanceRow row in weakReview.RoleBalance)
        {
            if (row.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(CreateRoleBalanceRow(row));
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        foreach (DeckWeakSpotBalanceRow row in weakReview.RoleBalance)
        {
            if (!row.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(CreateRoleBalanceRow(row));
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds a de-duplicated top-risk list from validation and analysis findings.
    /// </summary>
    private static List<object> BuildTopRisks(
        DeckValidationResult validation,
        ManaBaseAnalysis mana,
        DeckConsistencyAnalysis consistency,
        DeckWeakSpotReview weakReview,
        int limit)
    {
        List<object> risks = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string error in validation.Errors)
        {
            AddRisk(risks, seen, "validation", "error", error, limit);
        }

        foreach (string warning in validation.Warnings)
        {
            AddRisk(risks, seen, "validation", "warning", warning, limit);
        }

        foreach (string risk in mana.Risks)
        {
            AddRisk(risks, seen, "mana", "warning", risk, limit);
        }

        foreach (string risk in consistency.Risks)
        {
            AddRisk(risks, seen, "consistency", "warning", risk, limit);
        }

        foreach (DeckWeakSpotBalanceRow row in weakReview.RoleBalance)
        {
            if (row.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddRisk(
                risks,
                seen,
                "role-balance",
                "warning",
                $"{row.Target} is {row.Status}: {row.Rationale}",
                limit);
        }

        return risks;
    }

    /// <summary>
    /// Builds bounded suspected-cut rows from weak-slot evidence.
    /// </summary>
    private static List<object> BuildSuspectedCuts(DeckWeakSpotReview weakReview, int limit)
    {
        List<object> rows = [];
        foreach (DeckWeakSlotEvidenceRow row in weakReview.WeakSlots)
        {
            rows.Add(new
            {
                row.CardName,
                row.Quantity,
                row.PrimaryCategory,
                row.Role,
                row.Tags,
                row.ManaValue,
                row.Price,
                row.ClassifierConfidence,
                row.ScryfallUri,
                Signals = TakeStrings(row.Signals, 4),
                ProtectedCardWarnings = TakeStrings(row.ProtectedCardWarnings, 3)
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded sideboard and maybeboard upgrade rows from existing excluded-card evidence.
    /// </summary>
    private static List<object> BuildExcludedUpgrades(DeckWeakSpotReview weakReview, int limit)
    {
        List<object> rows = [];
        foreach (DeckWeakSpotCandidateRow row in weakReview.CandidateRows)
        {
            rows.Add(new
            {
                row.CardName,
                row.SourceCategory,
                row.MatchedTarget,
                row.TargetKind,
                row.Price,
                row.ScryfallUri,
                row.Rationale
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded tapped-land contributor rows.
    /// </summary>
    private static List<object> BuildTappedLandContributors(ManaBaseAnalysis mana, int limit)
    {
        List<object> rows = [];
        foreach (TappedLandContributor contributor in mana.TappedLandContributors)
        {
            rows.Add(new
            {
                contributor.CardName,
                contributor.Quantity,
                contributor.Timing,
                contributor.ProducedMana,
                contributor.Reason,
                contributor.ScryfallUri
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded key draw-odds rows from consistency analysis.
    /// </summary>
    private static List<object> BuildKeyOdds(DeckConsistencyAnalysis consistency, int limit)
    {
        List<object> rows = [];
        foreach (DeckOddsRow row in consistency.KeyOdds.Rows)
        {
            rows.Add(new
            {
                row.Target,
                row.SuccessesInDeck,
                row.HypergeometricAtLeastOne,
                row.HypergeometricAtLeastTwo,
                row.MonteCarloAtLeastOne
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded legality issue rows.
    /// </summary>
    private static List<object> SummarizeLegalityIssues(IReadOnlyList<DeckLegalityIssue> issues, int limit)
    {
        List<object> rows = [];
        foreach (DeckLegalityIssue issue in issues)
        {
            rows.Add(new
            {
                issue.CardName,
                issue.Quantity,
                issue.Category,
                issue.Severity,
                issue.Legality,
                issue.Message,
                issue.ScryfallUri
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded cost-driver rows.
    /// </summary>
    private static List<object> SummarizeCostDrivers(IReadOnlyList<DeckCostDriver> drivers, int limit)
    {
        List<object> rows = [];
        foreach (DeckCostDriver driver in drivers)
        {
            rows.Add(new
            {
                driver.CardName,
                driver.Category,
                driver.Quantity,
                driver.UnitPrice,
                driver.TotalPrice,
                driver.PriceSource,
                driver.PriceKnown,
                driver.PrintingStatus
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded diff rows.
    /// </summary>
    private static List<object> SummarizeDiffRows(IReadOnlyList<WorkspaceDiffCardChange> changes, int limit)
    {
        List<object> rows = [];
        foreach (WorkspaceDiffCardChange change in changes)
        {
            rows.Add(new
            {
                change.CardName,
                change.QuantityBefore,
                change.QuantityAfter,
                change.PrimaryCategoryBefore,
                change.PrimaryCategoryAfter,
                change.CategoriesBefore,
                change.CategoriesAfter,
                change.SecondaryCategoriesBefore,
                change.SecondaryCategoriesAfter,
                change.ScryfallUri,
                Notes = TakeStrings(change.Notes, 3)
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded corpus recommendation rows.
    /// </summary>
    private static List<object> SummarizeCorpusRecommendations(
        IReadOnlyList<CorpusRecommendation> recommendations,
        int limit)
    {
        List<object> rows = [];
        foreach (CorpusRecommendation recommendation in recommendations)
        {
            rows.Add(new
            {
                recommendation.CardName,
                recommendation.ReplaceCard,
                recommendation.RecommendationKind,
                recommendation.Role,
                recommendation.Tags,
                recommendation.Score,
                recommendation.Confidence,
                recommendation.Price,
                recommendation.EdhrecRank,
                recommendation.ScryfallUri,
                recommendation.Rationale,
                Evidence = SummarizeCorpusEvidence(recommendation.Evidence, 3)
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded corpus evidence rows.
    /// </summary>
    private static List<object> SummarizeCorpusEvidence(IReadOnlyList<CorpusEvidence> evidence, int limit)
    {
        List<object> rows = [];
        foreach (CorpusEvidence row in evidence)
        {
            rows.Add(new
            {
                row.Source,
                row.SignalType,
                row.Score,
                row.Summary,
                row.Uri
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded corpus source rows.
    /// </summary>
    private static List<object> SummarizeCorpusSources(IReadOnlyList<CorpusSourceStatus> sources, int limit)
    {
        List<object> rows = [];
        foreach (CorpusSourceStatus source in sources)
        {
            rows.Add(new
            {
                source.Key,
                source.Name,
                source.Kind,
                source.Enabled,
                source.Status,
                source.Uri,
                Notes = TakeStrings(source.Notes, 3)
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds bounded exemplar deck rows.
    /// </summary>
    private static List<object> SummarizeExemplarDecks(IReadOnlyList<DeckExemplarSignal> decks, int limit)
    {
        List<object> rows = [];
        foreach (DeckExemplarSignal deck in decks)
        {
            rows.Add(new
            {
                deck.Name,
                deck.Source,
                deck.Uri,
                deck.Commander,
                deck.PopularityMetric,
                deck.PopularityValue,
                deck.Weight
            });
            if (rows.Count >= limit)
            {
                return rows;
            }
        }

        return rows;
    }

    /// <summary>
    /// Builds one role-balance row for presentation.
    /// </summary>
    private static object CreateRoleBalanceRow(DeckWeakSpotBalanceRow row)
    {
        return new
        {
            row.Target,
            row.TargetKind,
            row.CurrentCount,
            row.Minimum,
            row.Maximum,
            row.Status,
            row.Rationale
        };
    }

    /// <summary>
    /// Adds one risk row when it is non-empty, unique, and within the list cap.
    /// </summary>
    private static void AddRisk(
        List<object> risks,
        HashSet<string> seen,
        string source,
        string severity,
        string message,
        int limit)
    {
        if (risks.Count >= limit || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string key = $"{source}:{severity}:{message}";
        if (!seen.Add(key))
        {
            return;
        }

        risks.Add(new
        {
            Source = source,
            Severity = severity,
            Message = message
        });
    }

    /// <summary>
    /// Copies string rows up to the requested cap.
    /// </summary>
    private static List<string> TakeStrings(IReadOnlyList<string> values, int limit)
    {
        List<string> result = [];
        foreach (string value in values)
        {
            result.Add(value);
            if (result.Count >= limit)
            {
                return result;
            }
        }

        return result;
    }

    /// <summary>
    /// Counts legality errors across top-level and categorized findings.
    /// </summary>
    private static int CountLegalityErrors(DeckLegalityAudit legality)
    {
        return legality.Errors.Count
            + CountSeverity(legality.CardLegalityIssues, "error")
            + CountSeverity(legality.ColorIdentityIssues, "error")
            + CountSeverity(legality.CopyLimitIssues, "error")
            + CountSeverity(legality.SideboardIssues, "error");
    }

    /// <summary>
    /// Counts legality warnings across top-level, issue, and metadata-gap findings.
    /// </summary>
    private static int CountLegalityWarnings(DeckLegalityAudit legality)
    {
        return legality.Warnings.Count
            + CountSeverity(legality.CardLegalityIssues, "warning")
            + CountSeverity(legality.ColorIdentityIssues, "warning")
            + CountSeverity(legality.CopyLimitIssues, "warning")
            + CountSeverity(legality.SideboardIssues, "warning")
            + legality.MetadataGaps.Count;
    }

    /// <summary>
    /// Counts weak-spot role rows that are not already in range.
    /// </summary>
    private static int CountImbalances(DeckWeakSpotReview weakReview)
    {
        int count = 0;
        foreach (DeckWeakSpotBalanceRow row in weakReview.RoleBalance)
        {
            if (!row.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts issues matching one severity label.
    /// </summary>
    private static int CountSeverity(IReadOnlyList<DeckLegalityIssue> issues, string severity)
    {
        int count = 0;
        foreach (DeckLegalityIssue issue in issues)
        {
            if (issue.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Keeps the related snapshot analysis models together while building compare output.
    /// </summary>
    private sealed class WorkspaceAnalysisBundle
    {
        /// <summary>
        /// Gets or sets the workspace id used for this analysis side.
        /// </summary>
        public string WorkspaceId { get; set; } = "";

        /// <summary>
        /// Gets or sets the workspace name used for this analysis side.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets lightweight validation findings.
        /// </summary>
        public DeckValidationResult Validation { get; set; } = new();

        /// <summary>
        /// Gets or sets cached-metadata legality findings.
        /// </summary>
        public DeckLegalityAudit Legality { get; set; } = new();

        /// <summary>
        /// Gets or sets mana-base analysis.
        /// </summary>
        public ManaBaseAnalysis Mana { get; set; } = new();

        /// <summary>
        /// Gets or sets consistency analysis.
        /// </summary>
        public DeckConsistencyAnalysis Consistency { get; set; } = new();

        /// <summary>
        /// Gets or sets price and budget analysis.
        /// </summary>
        public DeckCostAnalysis Cost { get; set; } = new();

        /// <summary>
        /// Gets or sets weak-slot and role-balance evidence.
        /// </summary>
        public DeckWeakSpotReview WeakReview { get; set; } = new();
    }
}
