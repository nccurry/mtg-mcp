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
    /// Creates deck re-evaluation tools for the MCP surface.
    /// </summary>
    public DeckReEvaluationTools(DeckWorkspaceService decks, DeckAnalysisService analysis)
    {
        this.decks = decks;
        this.analysis = analysis;
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
        CancellationToken cancellationToken = default)
    {
        int boundedLimit = Math.Clamp(limit, 1, MaxLimit);
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
            SourceRecommendations = new
            {
                Status = "notQueried",
                Notes = new[]
                {
                    "Default re-evaluation uses saved workspace evidence only; call source-backed recommendation tools when live corpus evidence is needed."
                }
            },
            SourceStatuses = weakReview.SourceStatuses,
            Notes = TakeStrings(weakReview.Notes, boundedLimit)
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
}
