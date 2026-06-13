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
                includeLiveBracket: true,
                bracketSkipReason: null,
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
        string? sourceSupportDepth,
        string? analysisMode,
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

        string normalizedSourceSupportDepth = NormalizeSourceSupportDepth(sourceSupportDepth);
        string normalizedAnalysisMode = NormalizeAnalysisMode(analysisMode);
        bool largePackage = IsLargePackage(plan);
        bool partialDeck = IsPartialCommanderDeck(workspace, expectedIncludedCards: 100);
        bool includeLiveBracket = ShouldIncludeLiveBracket(normalizedAnalysisMode, largePackage);
        bool performanceSkipped = ShouldSkipPerformance(normalizedAnalysisMode, largePackage, partialDeck);
        string? performanceSkipReason = performanceSkipped
            ? BuildPerformanceSkipReason(normalizedAnalysisMode, plan, workspace, largePackage, partialDeck)
            : null;
        string? bracketSkipReason = includeLiveBracket
            ? null
            : BuildBracketSkipReason(normalizedAnalysisMode, largePackage);
        PlanPreviewWorkspaceResult previewResult = await PreviewPlanWithWorkspacesAsync(
                plan,
                resolveAddedCards,
                includeLiveBracket,
                bracketSkipReason,
                cancellationToken)
            .ConfigureAwait(false);
        DeckPerformanceComparison performance = performanceSkipped
            ? BuildSkippedPerformance(plan.WorkspaceId, performanceSkipReason)
            : BuildPackagePerformance(
                previewResult.BeforeWorkspace,
                previewResult.AfterWorkspace,
                plan.WorkspaceId,
                simulationProfile,
                simulations,
                maxTurn,
                seed,
                previewResult.Preview.Warnings,
                cancellationToken);
        previewResult.Preview.PlanId = "";

        DeckCardPackagePreviewResult result = new()
        {
            WorkspaceId = workspace.Id,
            PreviewPlan = BuildPreviewPlan(plan),
            PreviewOnly = true,
            CanApply = false,
            ApplyPlanId = null,
            NextAction = "Create a persisted deck edit plan with deck_plan_create before calling deck_plan_apply.",
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
                previewResult.Preview.After.Bracket,
                bracketSkipReason),
            AnalysisMode = normalizedAnalysisMode,
            PartialDeck = partialDeck,
            ExpectedIncludedCards = partialDeck ? 100 : null,
            PerformanceSkipped = performanceSkipped,
            PerformanceSkipReason = performanceSkipReason,
            SourceSupportDepth = normalizedSourceSupportDepth,
            SourceSupport = await BuildPackageSourceSupportAsync(
                    plan,
                    previewResult.BeforeWorkspace,
                    previewResult.AfterWorkspace,
                    normalizedSourceSupportDepth,
                    previewResult.Preview.Warnings,
                    cancellationToken)
                .ConfigureAwait(false),
            Performance = performance,
            Warnings = previewResult.Preview.Warnings
        };

        return result;
    }

    /// <summary>
    /// Copies the preview plan body without exposing the transient generated plan id.
    /// </summary>
    private static PreviewDeckEditPlan BuildPreviewPlan(DeckEditPlan plan)
    {
        return new PreviewDeckEditPlan
        {
            WorkspaceId = plan.WorkspaceId,
            Name = plan.Name,
            Kind = plan.Kind,
            Rationale = plan.Rationale,
            Confidence = plan.Confidence,
            Warnings = plan.Warnings.ToList(),
            Operations = plan.Operations
                .Select(operation => new DeckEditOperation
                {
                    Operation = operation.Operation,
                    CardName = operation.CardName,
                    ReplacementCardName = operation.ReplacementCardName,
                    Quantity = operation.Quantity,
                    Category = operation.Category,
                    FromCategory = operation.FromCategory,
                    ToCategory = operation.ToCategory,
                    Name = operation.Name,
                    Format = operation.Format,
                    Description = operation.Description,
                    IncludedInDeck = operation.IncludedInDeck,
                    IncludedInPrice = operation.IncludedInPrice,
                    Rationale = operation.Rationale
                })
                .ToList()
        };
    }

    /// <summary>
    /// Builds preview metrics and keeps both before and after workspaces for transient comparisons.
    /// </summary>
    private async Task<PlanPreviewWorkspaceResult> PreviewPlanWithWorkspacesAsync(
        DeckEditPlan plan,
        bool resolveAddedCards,
        bool includeLiveBracket,
        string? bracketSkipReason,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        DeckPlanPreviewer previewer = new(CardCatalog);
        DeckWorkspace preview = previewer.CloneWorkspace(workspace);
        List<string> warnings = [];
        IReadOnlySet<string> gameChangers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool gameChangerDataAvailable = true;
        string? gameChangerNote = null;
        if (includeLiveBracket)
        {
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
        }
        else
        {
            gameChangerNote = "Live Game Changer lookup was skipped for this preview; bracket estimates exclude live Game Changer signals.";
            if (!string.IsNullOrWhiteSpace(bracketSkipReason))
            {
                warnings.Add(bracketSkipReason);
            }
        }

        await previewer.ApplyOperationsAsync(
                preview,
                plan.Operations,
                resolveAddedCards,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);

        DeckPlanPreviewResult previewResult = new()
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            ResolveAddedCards = resolveAddedCards,
            Before = BuildMetricSnapshot(workspace, gameChangers, gameChangerDataAvailable, gameChangerNote),
            After = BuildMetricSnapshot(preview, gameChangers, gameChangerDataAvailable, gameChangerNote),
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
        CommanderBracketEstimate after,
        string? skipReason)
    {
        return new DeckBracketImpact
        {
            Skipped = !string.IsNullOrWhiteSpace(skipReason),
            SkipReason = skipReason,
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
    private async Task<List<DeckPackageSourceSupport>> BuildPackageSourceSupportAsync(
        DeckEditPlan plan,
        DeckWorkspace before,
        DeckWorkspace after,
        string sourceSupportDepth,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (sourceSupportDepth.Equals(PreviewSourceSupportDepths.None, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        IReadOnlyDictionary<string, CardInfo> resolvedCards;
        try
        {
            resolvedCards = await ResolvePackageSourceCardsAsync(
                    plan,
                    before,
                    after,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            warnings.Add($"Package source-support metadata resolution failed: {exception.Message} Returning partial preview rows.");
            resolvedCards = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        }

        List<DeckPackageSourceSupport> rows = [];
        foreach (DeckEditOperation operation in plan.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.CardName))
            {
                continue;
            }

            DeckCard? workspaceCard = FindPackageCard(operation, before, after);
            resolvedCards.TryGetValue(operation.CardName, out CardInfo? resolvedCard);
            rows.Add(BuildPackageSourceSupportRow(
                operation,
                workspaceCard,
                resolvedCard,
                sourceSupportDepth));
        }

        return rows;
    }

    /// <summary>
    /// Builds deterministic performance comparison when the caller requests full analysis.
    /// </summary>
    private static DeckPerformanceComparison BuildPackagePerformance(
        DeckWorkspace beforeWorkspace,
        DeckWorkspace afterWorkspace,
        string workspaceId,
        string simulationProfile,
        int simulations,
        int maxTurn,
        int seed,
        IReadOnlyList<string> previewWarnings,
        CancellationToken cancellationToken)
    {
        DeckPerformanceAnalysis beforePerformance = DeckPerformanceAnalyzer.Analyze(
            beforeWorkspace,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);
        DeckPerformanceAnalysis afterPerformance = DeckPerformanceAnalyzer.Analyze(
            afterWorkspace,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);

        return new DeckPerformanceComparison
        {
            PlanId = "",
            WorkspaceId = workspaceId,
            Before = beforePerformance,
            After = afterPerformance,
            Deltas = DeckPerformanceComparisonBuilder.BuildDeltas(beforePerformance, afterPerformance),
            Warnings = previewWarnings
                .Concat(beforePerformance.Warnings.Select(warning => $"Before: {warning}"))
                .Concat(afterPerformance.Warnings.Select(warning => $"After: {warning}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>
    /// Builds a performance comparison placeholder with explicit skip context.
    /// </summary>
    private static DeckPerformanceComparison BuildSkippedPerformance(string workspaceId, string? skipReason)
    {
        DeckPerformanceComparison comparison = new()
        {
            PlanId = "",
            WorkspaceId = workspaceId,
        };
        if (!string.IsNullOrWhiteSpace(skipReason))
        {
            comparison.Warnings.Add(skipReason);
        }

        return comparison;
    }

    /// <summary>
    /// Resolves missing package card metadata through the configured card catalog.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, CardInfo>> ResolvePackageSourceCardsAsync(
        DeckEditPlan plan,
        DeckWorkspace before,
        DeckWorkspace after,
        CancellationToken cancellationToken)
    {
        List<string> unresolvedNames = [];
        foreach (DeckEditOperation operation in plan.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.CardName)
                || FindPackageCard(operation, before, after) is { Snapshot.ScryfallUri.Length: > 0 })
            {
                continue;
            }

            unresolvedNames.Add(operation.CardName);
        }

        if (unresolvedNames.Count == 0)
        {
            return new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        }

        return await CardCatalog
            .GetCardsByNamesAsync(
                unresolvedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds one source-support row from preview snapshots and optional catalog metadata.
    /// </summary>
    private static DeckPackageSourceSupport BuildPackageSourceSupportRow(
        DeckEditOperation operation,
        DeckCard? workspaceCard,
        CardInfo? resolvedCard,
        string sourceSupportDepth)
    {
        string cardName = operation.CardName ?? "";
        CardSnapshot? snapshot = workspaceCard?.Snapshot;
        string? scryfallUri = FirstNonEmpty(snapshot?.ScryfallUri, resolvedCard?.ScryfallUri);
        int? edhrecRank = snapshot?.EdhrecRank ?? resolvedCard?.EdhrecRank;
        (decimal? price, string? priceSource) = ReadSupportPrice(snapshot?.Prices, resolvedCard?.Prices);
        CardRoleAssignment? assignment = sourceSupportDepth.Equals(PreviewSourceSupportDepths.Balanced, StringComparison.OrdinalIgnoreCase)
            ? DeckRoleClassifier.Classify(workspaceCard ?? CreateSupportCard(cardName, resolvedCard))
            : null;
        DeckPackageSourceSupport row = new()
        {
            CardName = cardName,
            Operation = operation.Operation,
            Status = string.IsNullOrWhiteSpace(scryfallUri) && !edhrecRank.HasValue
                ? "unresolved"
                : "source-backed-metadata",
            ScryfallUri = scryfallUri,
            EdhrecRank = edhrecRank,
            Role = assignment?.PrimaryRole,
            Tags = assignment?.Tags.ToList() ?? [],
            Price = price,
            PriceSource = priceSource,
        };

        if (!string.IsNullOrWhiteSpace(scryfallUri))
        {
            row.Notes.Add("Scryfall card metadata resolved for this package card.");
        }

        if (edhrecRank.HasValue)
        {
            row.Notes.Add($"EDHREC rank {edhrecRank.Value} was available from card metadata.");
        }

        if (row.Status.Equals("unresolved", StringComparison.OrdinalIgnoreCase))
        {
            row.Notes.Add("No source-backed card metadata was available in preview; run source_explain_card_signal for deeper source evidence.");
        }

        return row;
    }

    /// <summary>
    /// Package source support prefers the workspace side where the operation has observable card metadata.
    /// </summary>
    private static DeckCard? FindPackageCard(
        DeckEditOperation operation,
        DeckWorkspace before,
        DeckWorkspace after)
    {
        if (string.IsNullOrWhiteSpace(operation.CardName))
        {
            return null;
        }

        DeckWorkspace preferred = operation.Operation.Equals(DeckEditOperations.RemoveCard, StringComparison.OrdinalIgnoreCase)
            ? before
            : after;
        DeckCard? card = FindCard(preferred, operation.CardName);
        return card ?? FindCard(ReferenceEquals(preferred, before) ? after : before, operation.CardName);
    }

    /// <summary>
    /// Finds one card in a workspace by card name.
    /// </summary>
    private static DeckCard? FindCard(DeckWorkspace workspace, string cardName)
    {
        foreach (DeckCard card in workspace.Cards)
        {
            if (card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a transient card row from catalog metadata for role classification.
    /// </summary>
    private static DeckCard CreateSupportCard(string cardName, CardInfo? card)
    {
        return new DeckCard
        {
            Name = card?.Name ?? cardName,
            Snapshot = card is null
                ? new CardSnapshot()
                : new CardSnapshot
                {
                    ManaCost = card.ManaCost,
                    ManaValue = card.ManaValue,
                    TypeLine = card.TypeLine,
                    OracleText = card.OracleText,
                    ColorIdentity = card.ColorIdentity.ToList(),
                    ProducedMana = card.ProducedMana.ToList(),
                    EdhrecRank = card.EdhrecRank,
                    ScryfallUri = card.ScryfallUri,
                    Prices = new Dictionary<string, string>(card.Prices, StringComparer.OrdinalIgnoreCase)
                }
        };
    }

    /// <summary>
    /// Reads the first useful price field for source-support output.
    /// </summary>
    private static (decimal? Price, string? Source) ReadSupportPrice(
        IReadOnlyDictionary<string, string>? snapshotPrices,
        IReadOnlyDictionary<string, string>? cardPrices)
    {
        foreach (IReadOnlyDictionary<string, string>? prices in new[] { snapshotPrices, cardPrices })
        {
            if (prices is null)
            {
                continue;
            }

            foreach (string source in new[] { "usd", "usd_foil", "usd_etched" })
            {
                if (prices.TryGetValue(source, out string? text)
                    && decimal.TryParse(
                        text,
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out decimal price)
                    && price > 0)
                {
                    return (price, source);
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Returns the first non-empty string from a small set of candidates.
    /// </summary>
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes source-support depth for transient package previews.
    /// </summary>
    private static string NormalizeSourceSupportDepth(string? sourceSupportDepth)
    {
        string normalized = string.IsNullOrWhiteSpace(sourceSupportDepth)
            ? PreviewSourceSupportDepths.Minimal
            : sourceSupportDepth.Trim().ToLowerInvariant();
        return normalized switch
        {
            PreviewSourceSupportDepths.None => PreviewSourceSupportDepths.None,
            PreviewSourceSupportDepths.Balanced => PreviewSourceSupportDepths.Balanced,
            PreviewSourceSupportDepths.Minimal => PreviewSourceSupportDepths.Minimal,
            _ => throw new ArgumentException(
                "sourceSupportDepth must be none, minimal, or balanced.",
                nameof(sourceSupportDepth))
        };
    }

    /// <summary>
    /// Normalizes package preview analysis mode.
    /// </summary>
    private static string NormalizeAnalysisMode(string? analysisMode)
    {
        string normalized = string.IsNullOrWhiteSpace(analysisMode)
            ? PreviewAnalysisModes.Summary
            : analysisMode.Trim().ToLowerInvariant();
        return normalized switch
        {
            PreviewAnalysisModes.None => PreviewAnalysisModes.None,
            PreviewAnalysisModes.Summary => PreviewAnalysisModes.Summary,
            PreviewAnalysisModes.Full => PreviewAnalysisModes.Full,
            _ => throw new ArgumentException(
                "analysisMode must be none, summary, or full.",
                nameof(analysisMode))
        };
    }

    /// <summary>
    /// Checks whether package size should use bounded summary analysis by default.
    /// </summary>
    private static bool IsLargePackage(DeckEditPlan plan)
    {
        return plan.Operations.Count >= 25 || CountChangedCopies(plan) >= 50;
    }

    /// <summary>
    /// Counts card-copy changes represented by a package plan.
    /// </summary>
    private static int CountChangedCopies(DeckEditPlan plan)
    {
        int count = 0;
        foreach (DeckEditOperation operation in plan.Operations)
        {
            count += Math.Max(1, operation.Quantity ?? 1);
        }

        return count;
    }

    /// <summary>
    /// Checks whether a Commander workspace is below the expected deck size.
    /// </summary>
    private static bool IsPartialCommanderDeck(DeckWorkspace workspace, int expectedIncludedCards)
    {
        if (!workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase)
            && !workspace.Format.Equals("edh", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int included = 0;
        foreach (DeckCard card in workspace.Cards)
        {
            if (DeckCategoryInclusion.IsIncludedInDeck(workspace, card))
            {
                included += Math.Max(0, card.Quantity);
            }
        }

        return included > 0 && included < expectedIncludedCards;
    }

    /// <summary>
    /// Determines whether a package preview should fetch live Game Changer data.
    /// </summary>
    private static bool ShouldIncludeLiveBracket(string analysisMode, bool largePackage)
    {
        if (analysisMode.Equals(PreviewAnalysisModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !largePackage || analysisMode.Equals(PreviewAnalysisModes.Full, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether package performance simulation should run.
    /// </summary>
    private static bool ShouldSkipPerformance(string analysisMode, bool largePackage, bool partialDeck)
    {
        if (analysisMode.Equals(PreviewAnalysisModes.Full, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return analysisMode.Equals(PreviewAnalysisModes.None, StringComparison.OrdinalIgnoreCase)
            || largePackage
            || partialDeck;
    }

    /// <summary>
    /// Builds a stable skip reason for performance analysis.
    /// </summary>
    private static string BuildPerformanceSkipReason(
        string analysisMode,
        DeckEditPlan plan,
        DeckWorkspace workspace,
        bool largePackage,
        bool partialDeck)
    {
        if (analysisMode.Equals(PreviewAnalysisModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return "analysisMode=none skips goldfish performance simulation.";
        }

        if (largePackage)
        {
            return $"Summary analysis skips performance for large packages ({plan.Operations.Count} operations, {CountChangedCopies(plan)} changed copies). Use analysisMode=full to run it.";
        }

        if (partialDeck)
        {
            int included = 0;
            foreach (DeckCard card in workspace.Cards)
            {
                if (DeckCategoryInclusion.IsIncludedInDeck(workspace, card))
                {
                    included += Math.Max(0, card.Quantity);
                }
            }

            return $"Summary analysis skips performance for partial Commander decks ({included}/100 included cards). Use analysisMode=full after the deck is complete.";
        }

        return "Performance analysis skipped.";
    }

    /// <summary>
    /// Builds a stable skip reason for live Commander bracket lookups.
    /// </summary>
    private static string? BuildBracketSkipReason(string analysisMode, bool largePackage)
    {
        if (analysisMode.Equals(PreviewAnalysisModes.None, StringComparison.OrdinalIgnoreCase))
        {
            return "analysisMode=none skips live Commander Game Changer lookup; bracket impact excludes live Game Changer signals.";
        }

        return largePackage
            ? "Summary analysis skips live Commander Game Changer lookup for large packages; bracket impact excludes live Game Changer signals."
            : null;
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
