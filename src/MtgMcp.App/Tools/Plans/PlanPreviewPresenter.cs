using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes plan-preview MCP outputs without changing Core preview semantics.
/// </summary>
internal static class PlanPreviewPresenter
{
    /// <summary>
    /// Presents a persisted plan preview at the requested detail level.
    /// </summary>
    public static object Present(DeckPlanPreviewResult result, string? detailLevel)
    {
        string normalized = NormalizeDetailLevel(detailLevel);
        if (normalized == PreviewDetailLevels.Full)
        {
            return result;
        }

        return new
        {
            detailLevel = normalized,
            planId = result.PlanId,
            workspaceId = result.WorkspaceId,
            canApply = !string.IsNullOrWhiteSpace(result.PlanId),
            applyPlanId = string.IsNullOrWhiteSpace(result.PlanId) ? null : result.PlanId,
            previewOnly = false,
            resolveAddedCards = result.ResolveAddedCards,
            summary = BuildSummary(result),
            before = normalized == PreviewDetailLevels.Normal ? PresentSnapshot(result.Before) : null,
            after = normalized == PreviewDetailLevels.Normal ? PresentSnapshot(result.After) : null,
            warnings = result.Warnings.Take(8).ToList(),
        };
    }

    /// <summary>
    /// Presents a transient package preview at the requested detail level.
    /// </summary>
    public static object Present(DeckCardPackagePreviewResult result, string? detailLevel)
    {
        string normalized = NormalizeDetailLevel(detailLevel);
        if (normalized == PreviewDetailLevels.Full)
        {
            return PresentFullPackage(result, normalized);
        }

        Dictionary<string, object?> response = PresentPackageHeader(result, normalized);
        response["previewPlan"] = PresentPreviewPlan(result.PreviewPlan, normalized);
        response["summary"] = BuildSummary(result.Preview);
        response["roleDeltas"] = result.RoleDeltas;
        response["categoryDeltas"] = BuildCountDeltas(
            result.Preview.Before.Analysis.CategoryCounts,
            result.Preview.After.Analysis.CategoryCounts);
        response["validationChanges"] = result.ValidationChanges;
        response["priceDelta"] = result.PriceDelta;
        response["bracketImpact"] = result.BracketImpact;
        response["sourceSupport"] = PresentSourceSupport(result.SourceSupport);
        response["performance"] = new
        {
            deltas = result.Performance.Deltas.Take(8).ToList(),
            warnings = result.Performance.Warnings.Take(8).ToList(),
        };
        response["before"] = normalized == PreviewDetailLevels.Normal ? PresentSnapshot(result.Preview.Before) : null;
        response["after"] = normalized == PreviewDetailLevels.Normal ? PresentSnapshot(result.Preview.After) : null;
        response["warnings"] = result.Warnings.Take(8).ToList();
        return response;
    }

    /// <summary>
    /// Builds compact before/after preview metrics.
    /// </summary>
    private static object BuildSummary(DeckPlanPreviewResult preview)
    {
        return new
        {
            includedCards = new
            {
                before = preview.Before.Analysis.IncludedCards,
                after = preview.After.Analysis.IncludedCards,
                delta = preview.After.Analysis.IncludedCards - preview.Before.Analysis.IncludedCards,
            },
            categoryDeltas = BuildCountDeltas(
                preview.Before.Analysis.CategoryCounts,
                preview.After.Analysis.CategoryCounts),
            roleDeltas = BuildCountDeltas(
                preview.Before.Analysis.RoleCounts,
                preview.After.Analysis.RoleCounts),
            budget = new
            {
                beforeIncludedTotal = preview.Before.Cost.IncludedTotal,
                afterIncludedTotal = preview.After.Cost.IncludedTotal,
                includedTotalDelta = preview.After.Cost.IncludedTotal - preview.Before.Cost.IncludedTotal,
                withinKnownBudget = preview.After.Cost.WithinKnownBudget,
                withinBudget = preview.After.Cost.WithinBudget,
                priceRiskStatus = preview.After.Cost.PriceRiskStatus,
                unresolvedMissingPriceCards = preview.After.Cost.UnresolvedMissingPriceCards.Take(8).ToList(),
            },
            validation = new
            {
                beforeIsValid = preview.Before.Validation.IsValid,
                afterIsValid = preview.After.Validation.IsValid,
                addedErrors = Difference(preview.After.Validation.Errors, preview.Before.Validation.Errors),
                addedWarnings = Difference(preview.After.Validation.Warnings, preview.Before.Validation.Warnings),
            },
            bracket = new
            {
                before = preview.Before.Bracket.EstimatedBracket,
                after = preview.After.Bracket.EstimatedBracket,
                delta = preview.After.Bracket.EstimatedBracket - preview.Before.Bracket.EstimatedBracket,
                afterGameChangerCount = preview.After.Bracket.GameChangerCount,
            },
            warnings = preview.Warnings.Take(8).ToList(),
        };
    }

    /// <summary>
    /// Presents a bounded plan body for transient packages.
    /// </summary>
    private static object PresentPreviewPlan(PreviewDeckEditPlan plan, string detailLevel)
    {
        List<object> operations = [];
        if (detailLevel == PreviewDetailLevels.Normal)
        {
            foreach (DeckEditOperation operation in plan.Operations)
            {
                operations.Add(operation);
            }
        }
        else
        {
            foreach (DeckEditOperation operation in plan.Operations.Take(20))
            {
                operations.Add(new
                {
                    operation = operation.Operation,
                    cardName = operation.CardName,
                    quantity = operation.Quantity,
                    category = operation.Category,
                    fromCategory = operation.FromCategory,
                    toCategory = operation.ToCategory,
                });
            }
        }

        return new
        {
            workspaceId = plan.WorkspaceId,
            name = plan.Name,
            kind = plan.Kind,
            rationale = plan.Rationale,
            confidence = plan.Confidence,
            operationCount = plan.Operations.Count,
            operations,
            warnings = plan.Warnings.Take(8).ToList(),
        };
    }

    /// <summary>
    /// Presents the full package preview while preserving explicit preview-only safety fields.
    /// </summary>
    private static Dictionary<string, object?> PresentFullPackage(
        DeckCardPackagePreviewResult result,
        string detailLevel)
    {
        Dictionary<string, object?> response = PresentPackageHeader(result, detailLevel);
        response["previewPlan"] = result.PreviewPlan;
        response["preview"] = result.Preview;
        response["roleDeltas"] = result.RoleDeltas;
        response["validationChanges"] = result.ValidationChanges;
        response["priceDelta"] = result.PriceDelta;
        response["bracketImpact"] = result.BracketImpact;
        response["sourceSupport"] = result.SourceSupport;
        response["performance"] = result.Performance;
        response["warnings"] = result.Warnings;
        return response;
    }

    /// <summary>
    /// Builds the package preview top-level safety fields shared by all detail levels.
    /// </summary>
    private static Dictionary<string, object?> PresentPackageHeader(
        DeckCardPackagePreviewResult result,
        string detailLevel)
    {
        return new Dictionary<string, object?>
        {
            ["detailLevel"] = detailLevel,
            ["workspaceId"] = result.WorkspaceId,
            ["previewOnly"] = true,
            ["canApply"] = false,
            ["applyPlanId"] = null,
            ["nextAction"] = result.NextAction,
            ["sourceSupportDepth"] = result.SourceSupportDepth,
        };
    }

    /// <summary>
    /// Presents the metric snapshot fields callers most often compare.
    /// </summary>
    private static object PresentSnapshot(DeckMetricSnapshot snapshot)
    {
        return new
        {
            cost = new
            {
                includedTotal = snapshot.Cost.IncludedTotal,
                maxBudget = snapshot.Cost.MaxBudget,
                withinKnownBudget = snapshot.Cost.WithinKnownBudget,
                withinBudget = snapshot.Cost.WithinBudget,
                budgetStatus = snapshot.Cost.BudgetStatus,
                priceRiskStatus = snapshot.Cost.PriceRiskStatus,
            },
            validation = snapshot.Validation,
            analysis = new
            {
                totalCards = snapshot.Analysis.TotalCards,
                includedCards = snapshot.Analysis.IncludedCards,
                categoryCounts = snapshot.Analysis.CategoryCounts,
                allCategoryCounts = snapshot.Analysis.AllCategoryCounts,
                roleCounts = snapshot.Analysis.RoleCounts,
                tagCounts = snapshot.Analysis.TagCounts,
            },
            manaBase = new
            {
                landCount = snapshot.ManaBase.LandCount,
                manaProducingLandCount = snapshot.ManaBase.ManaProducingLandCount,
                fixingCount = snapshot.ManaBase.FixingCount,
                risks = snapshot.ManaBase.Risks.Take(5).ToList(),
            },
            consistency = new
            {
                deckSize = snapshot.Consistency.DeckSize,
                rampCount = snapshot.Consistency.RampCount,
                drawCount = snapshot.Consistency.DrawCount,
                tutorCount = snapshot.Consistency.TutorCount,
                risks = snapshot.Consistency.Risks.Take(5).ToList(),
            },
            bracket = new
            {
                estimatedBracket = snapshot.Bracket.EstimatedBracket,
                bracketFloor = snapshot.Bracket.BracketFloor,
                gameChangerCount = snapshot.Bracket.GameChangerCount,
                confidence = snapshot.Bracket.Confidence,
            },
        };
    }

    /// <summary>
    /// Presents bounded source support rows with stable JSON field names.
    /// </summary>
    private static List<object> PresentSourceSupport(IReadOnlyList<DeckPackageSourceSupport> sourceSupport)
    {
        List<object> rows = [];
        foreach (DeckPackageSourceSupport row in sourceSupport.Take(16))
        {
            rows.Add(new
            {
                cardName = row.CardName,
                operation = row.Operation,
                status = row.Status,
                scryfallUri = row.ScryfallUri,
                edhrecRank = row.EdhrecRank,
                role = row.Role,
                tags = row.Tags,
                price = row.Price,
                priceSource = row.PriceSource,
                notes = row.Notes,
            });
        }

        return rows;
    }

    /// <summary>
    /// Builds count deltas from two case-insensitive metric dictionaries.
    /// </summary>
    private static List<object> BuildCountDeltas(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after)
    {
        HashSet<string> keys = new(before.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(after.Keys);
        List<CountDelta> deltas = [];
        foreach (string key in keys)
        {
            int beforeCount = before.GetValueOrDefault(key);
            int afterCount = after.GetValueOrDefault(key);
            int delta = afterCount - beforeCount;
            if (delta == 0)
            {
                continue;
            }

            deltas.Add(new CountDelta(key, beforeCount, afterCount, delta));
        }

        deltas.Sort(CompareCountDeltas);
        return deltas
            .Select(delta => new
            {
                name = delta.Name,
                before = delta.Before,
                after = delta.After,
                delta = delta.Delta,
            })
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// Sorts deltas by magnitude, then stable name.
    /// </summary>
    private static int CompareCountDeltas(CountDelta left, CountDelta right)
    {
        int byMagnitude = Math.Abs(right.Delta).CompareTo(Math.Abs(left.Delta));
        return byMagnitude != 0
            ? byMagnitude
            : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns values present in left but absent in right.
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
    /// Normalizes the preview output detail level.
    /// </summary>
    private static string NormalizeDetailLevel(string? detailLevel)
    {
        string normalized = string.IsNullOrWhiteSpace(detailLevel)
            ? PreviewDetailLevels.Summary
            : detailLevel.Trim().ToLowerInvariant();
        if (normalized is PreviewDetailLevels.Summary or PreviewDetailLevels.Normal or PreviewDetailLevels.Full)
        {
            return normalized;
        }

        throw new ArgumentException("detailLevel must be summary, normal, or full.", nameof(detailLevel));
    }

    /// <summary>
    /// Carries one count delta while sorting.
    /// </summary>
    private sealed record CountDelta(string Name, int Before, int After, int Delta);

    /// <summary>
    /// Lists accepted plan-preview detail levels.
    /// </summary>
    private static class PreviewDetailLevels
    {
        /// <summary>
        /// Includes key plan settings, deltas, source support, and warnings.
        /// </summary>
        public const string Summary = "summary";

        /// <summary>
        /// Includes summary output plus bounded before/after metric snapshots.
        /// </summary>
        public const string Normal = "normal";

        /// <summary>
        /// Returns the raw Core preview model.
        /// </summary>
        public const string Full = "full";
    }
}
