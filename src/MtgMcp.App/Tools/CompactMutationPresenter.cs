using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes compact mutation output for MCP tools when callers do not need a full workspace snapshot.
/// </summary>
internal static class CompactMutationPresenter
{
    /// <summary>
    /// Runs a workspace mutation and returns either the original full result or a compact diff.
    /// </summary>
    public static async Task<object> RunMutationAsync(
        DeckWorkspaceService decks,
        string workspaceId,
        bool? includeWorkspace,
        string? detailLevel,
        Func<Task<DeckChangeResult>> mutation,
        CancellationToken cancellationToken)
    {
        DetailLevel normalizedDetailLevel = ResolveDetailLevel(includeWorkspace, detailLevel);
        if (normalizedDetailLevel == DetailLevel.Full)
        {
            return await mutation().ConfigureAwait(false);
        }

        CompactMutationSnapshot before = Capture(
            await decks.GetDeckResourceAsync(workspaceId, cancellationToken)
                .ConfigureAwait(false));
        DeckChangeResult result = await mutation().ConfigureAwait(false);
        CompactMutationSnapshot after = Capture(result.Workspace);
        CompactMutationResult compact = FromSnapshots(
            before,
            after,
            result.WorkspaceId,
            result.Persistence,
            result.Message,
            CompactMutationDelta.Build(before, after));
        return normalizedDetailLevel == DetailLevel.Normal
            ? compact
            : ToSummary(compact);
    }

    /// <summary>
    /// Resolves detail-level compatibility between the old includeWorkspace flag and the new detailLevel parameter.
    /// </summary>
    public static DetailLevel ResolveDetailLevel(bool? includeWorkspace, string? detailLevel)
    {
        if (!string.IsNullOrWhiteSpace(detailLevel))
        {
            return DetailLevelParser.Parse(detailLevel);
        }

        return includeWorkspace == true ? DetailLevel.Full : DetailLevel.Summary;
    }

    /// <summary>
    /// Converts a normal compact mutation result to the summary shape.
    /// </summary>
    public static CompactMutationSummaryResult ToSummary(CompactMutationResult compact)
    {
        return new CompactMutationSummaryResult
        {
            Success = compact.Success,
            WorkspaceId = compact.WorkspaceId,
            ChangedCards = compact.ChangedCards.ToList(),
            Message = compact.Message,
            ValidationSummary = BuildValidationSummary(compact.Validation)
        };
    }

    /// <summary>
    /// Captures the immutable workspace facts needed to report a compact mutation.
    /// </summary>
    public static CompactMutationSnapshot Capture(DeckWorkspace workspace)
    {
        DeckAnalysis analysis = DeckAnalyzer.Analyze(workspace);
        DeckValidationResult validation = DeckValidator.Validate(workspace);
        return new CompactMutationSnapshot(
            workspace.Id,
            analysis.IncludedCards,
            new Dictionary<string, int>(analysis.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            validation,
            BuildCardIndex(workspace));
    }

    /// <summary>
    /// Builds a compact diff for a plan apply result.
    /// </summary>
    public static CompactMutationResult FromPlanApply(
        CompactMutationSnapshot before,
        CompactMutationSnapshot after,
        DeckEditPlanApplyResult result)
    {
        bool applyStateUnknown = result.Status == DeckEditPlanStatus.ApplyStateUnknown;
        CompactMutationDelta delta = applyStateUnknown
            ? CompactMutationDelta.Empty
            : CompactMutationDelta.Build(before, after);
        CompactMutationResult compact = FromSnapshots(
            before,
            after,
            result.WorkspaceId,
            result.Persistence,
            result.Success ? "Applied deck edit plan." : result.Error ?? "Deck edit plan apply failed.",
            delta);
        compact.Success = result.Success;
        compact.PlanId = result.PlanId;
        compact.Status = result.Status;
        compact.CheckpointId = result.CheckpointId;
        compact.Notes.AddRange(result.Messages);
        if (applyStateUnknown)
        {
            compact.Notes.Add("Plan apply state is unknown; compact mutation counts are unavailable until the workspace is reopened or diffed.");
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            compact.Notes.Add(result.Error);
        }

        return compact;
    }

    /// <summary>
    /// Builds a compact mutation result from before and after workspace snapshots.
    /// </summary>
    private static CompactMutationResult FromSnapshots(
        CompactMutationSnapshot before,
        CompactMutationSnapshot after,
        string workspaceId,
        string persistence,
        string message,
        CompactMutationDelta delta)
    {
        return new CompactMutationResult
        {
            WorkspaceId = workspaceId,
            WorkspaceResourceUri = $"mtg://workspace/{workspaceId}",
            Persistence = persistence,
            Message = message,
            Added = delta.Added,
            Removed = delta.Removed,
            Moved = delta.Moved,
            ChangedCards = delta.ChangedCards,
            IncludedCountBefore = before.IncludedCount,
            IncludedCountAfter = after.IncludedCount,
            CategoryCountsBefore = new Dictionary<string, int>(before.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            CategoryCountsAfter = new Dictionary<string, int>(after.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            Validation = after.Validation
        };
    }

    /// <summary>
    /// Builds bounded validation counts for summary mutation output.
    /// </summary>
    private static CompactValidationSummary BuildValidationSummary(DeckValidationResult validation)
    {
        return new CompactValidationSummary
        {
            IsValid = validation.IsValid,
            ErrorCount = validation.Errors.Count,
            WarningCount = validation.Warnings.Count
        };
    }

    /// <summary>
    /// Indexes cards by stable card identity for before/after comparison.
    /// </summary>
    private static Dictionary<string, CompactCardAggregate> BuildCardIndex(DeckWorkspace workspace)
    {
        Dictionary<string, CompactCardAggregate> cards = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards)
        {
            string identity = CardIdentity(card);
            if (!cards.TryGetValue(identity, out CompactCardAggregate? aggregate))
            {
                aggregate = new CompactCardAggregate
                {
                    Identity = identity,
                    CardName = card.Name
                };
                cards[identity] = aggregate;
            }

            aggregate.Quantity += Math.Max(0, card.Quantity);
            AddDistinct(aggregate.PrimaryCategories, DeckCategoryOrdering.PrimaryCategory(card));
            foreach (string category in DeckCategoryOrdering.OrderedDistinct(
                    DeckCategoryOrdering.PrimaryCategory(card),
                    card.Categories))
            {
                AddDistinct(aggregate.Categories, category);
            }
        }

        foreach (CompactCardAggregate aggregate in cards.Values)
        {
            aggregate.PrimaryCategories.Sort(StringComparer.OrdinalIgnoreCase);
            aggregate.Categories.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return cards;
    }

    /// <summary>
    /// Builds the identity string used for card matching across workspace snapshots.
    /// </summary>
    private static string CardIdentity(DeckCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.ScryfallOracleId))
        {
            return $"oracle:{card.ScryfallOracleId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(card.ScryfallId))
        {
            return $"scryfall:{card.ScryfallId.Trim()}";
        }

        return $"name:{card.Name.Trim().ToLowerInvariant()}";
    }

    /// <summary>
    /// Adds a value once using case-insensitive equality.
    /// </summary>
    private static void AddDistinct(List<string> values, string value)
    {
        if (!values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(value);
        }
    }

    /// <summary>
    /// Checks whether two string lists contain the same values ignoring order and case.
    /// </summary>
    private static bool SameValues(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.All(value => right.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stores immutable workspace facts for compact mutation reporting.
    /// </summary>
    public sealed class CompactMutationSnapshot
    {
        /// <summary>
        /// Creates a compact mutation snapshot.
        /// </summary>
        public CompactMutationSnapshot(
            string workspaceId,
            int includedCount,
            Dictionary<string, int> categoryCounts,
            DeckValidationResult validation,
            Dictionary<string, CompactCardAggregate> cards)
        {
            WorkspaceId = workspaceId;
            IncludedCount = includedCount;
            CategoryCounts = categoryCounts;
            Validation = validation;
            Cards = cards;
        }

        /// <summary>
        /// Gets the workspace id.
        /// </summary>
        public string WorkspaceId { get; }

        /// <summary>
        /// Gets the included card quantity.
        /// </summary>
        public int IncludedCount { get; }

        /// <summary>
        /// Gets primary-category counts across all cards.
        /// </summary>
        public Dictionary<string, int> CategoryCounts { get; }

        /// <summary>
        /// Gets deck-rule validation for the snapshot.
        /// </summary>
        public DeckValidationResult Validation { get; }

        /// <summary>
        /// Gets indexed card aggregates.
        /// </summary>
        public Dictionary<string, CompactCardAggregate> Cards { get; }
    }

    /// <summary>
    /// Aggregates equivalent cards for compact mutation comparison.
    /// </summary>
    public sealed class CompactCardAggregate
    {
        /// <summary>
        /// Gets or sets comparison identity.
        /// </summary>
        public string Identity { get; set; } = "";

        /// <summary>
        /// Gets or sets display card name.
        /// </summary>
        public string CardName { get; set; } = "";

        /// <summary>
        /// Gets or sets aggregate quantity.
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Gets or sets all primary categories found for this identity.
        /// </summary>
        public List<string> PrimaryCategories { get; set; } = [];

        /// <summary>
        /// Gets or sets all categories found for this identity.
        /// </summary>
        public List<string> Categories { get; set; } = [];

        /// <summary>
        /// Gets primary category label for comparison.
        /// </summary>
        public string PrimaryCategory => PrimaryCategories.Count == 1
            ? PrimaryCategories[0]
            : string.Join(" | ", PrimaryCategories);
    }

    /// <summary>
    /// Describes the actual card-level changes between compact snapshots.
    /// </summary>
    private sealed class CompactMutationDelta
    {
        /// <summary>
        /// Gets an empty delta for unknown remote mutation state.
        /// </summary>
        public static CompactMutationDelta Empty { get; } = new();

        /// <summary>
        /// Gets or sets aggregate card-copy quantity increases.
        /// </summary>
        public int Added { get; set; }

        /// <summary>
        /// Gets or sets aggregate card-copy quantity decreases.
        /// </summary>
        public int Removed { get; set; }

        /// <summary>
        /// Gets or sets count of card identities whose primary category changed.
        /// </summary>
        public int Moved { get; set; }

        /// <summary>
        /// Gets or sets card names with actual quantity, primary-category, or tag changes.
        /// </summary>
        public List<string> ChangedCards { get; set; } = [];

        /// <summary>
        /// Builds a compact mutation delta from before and after snapshots.
        /// </summary>
        public static CompactMutationDelta Build(
            CompactMutationSnapshot before,
            CompactMutationSnapshot after)
        {
            CompactMutationDelta delta = new();
            HashSet<string> identities = new(before.Cards.Keys, StringComparer.OrdinalIgnoreCase);
            identities.UnionWith(after.Cards.Keys);
            foreach (string identity in identities)
            {
                before.Cards.TryGetValue(identity, out CompactCardAggregate? left);
                after.Cards.TryGetValue(identity, out CompactCardAggregate? right);
                int beforeQuantity = left?.Quantity ?? 0;
                int afterQuantity = right?.Quantity ?? 0;
                if (afterQuantity > beforeQuantity)
                {
                    delta.Added += afterQuantity - beforeQuantity;
                }
                else if (beforeQuantity > afterQuantity)
                {
                    delta.Removed += beforeQuantity - afterQuantity;
                }

                bool primaryChanged = left is not null
                    && right is not null
                    && !left.PrimaryCategory.Equals(right.PrimaryCategory, StringComparison.OrdinalIgnoreCase);
                if (primaryChanged)
                {
                    delta.Moved++;
                }

                bool categoriesChanged = left is not null
                    && right is not null
                    && !SameValues(left.Categories, right.Categories);
                bool changed = left is null
                    || right is null
                    || beforeQuantity != afterQuantity
                    || primaryChanged
                    || categoriesChanged;
                if (changed)
                {
                    delta.ChangedCards.Add(right?.CardName ?? left?.CardName ?? identity);
                }
            }

            delta.ChangedCards = delta.ChangedCards
                .Where(card => !string.IsNullOrWhiteSpace(card))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return delta;
        }
    }

}
