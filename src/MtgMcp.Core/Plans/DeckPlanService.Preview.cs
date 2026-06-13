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
        string? sourceSupportDepth,
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
                previewResult.Preview.After.Bracket),
            SourceSupportDepth = normalizedSourceSupportDepth,
            SourceSupport = await BuildPackageSourceSupportAsync(
                    plan,
                    previewResult.BeforeWorkspace,
                    previewResult.AfterWorkspace,
                    normalizedSourceSupportDepth,
                    cancellationToken)
                .ConfigureAwait(false),
            Performance = new DeckPerformanceComparison
            {
                PlanId = "",
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
    private async Task<List<DeckPackageSourceSupport>> BuildPackageSourceSupportAsync(
        DeckEditPlan plan,
        DeckWorkspace before,
        DeckWorkspace after,
        string sourceSupportDepth,
        CancellationToken cancellationToken)
    {
        if (sourceSupportDepth.Equals(PreviewSourceSupportDepths.None, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        IReadOnlyDictionary<string, CardInfo> resolvedCards = await ResolvePackageSourceCardsAsync(
                plan,
                before,
                after,
                cancellationToken)
            .ConfigureAwait(false);
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
