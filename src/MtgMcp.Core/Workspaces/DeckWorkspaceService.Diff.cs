namespace MtgMcp.Core;

/// <summary>
/// Compares saved deck workspaces for explicit iterative-review baselines.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Returns deterministic card, category, count, and validation changes between two saved workspaces.
    /// </summary>
    public async Task<WorkspaceDiffResult> DiffWorkspacesAsync(
        string workspaceId,
        string previousWorkspaceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previousWorkspaceId))
        {
            throw new ArgumentException("An explicit previous workspace id is required.", nameof(previousWorkspaceId));
        }

        DeckWorkspace current = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspace previous = await LoadWorkspaceAsync(previousWorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        return BuildWorkspaceDiff(current, previous);
    }

    /// <summary>
    /// Returns deterministic changes between two in-memory workspace snapshots.
    /// </summary>
    public WorkspaceDiffResult DiffWorkspaceSnapshots(DeckWorkspace current, DeckWorkspace previous)
    {
        return BuildWorkspaceDiff(current, previous);
    }

    /// <summary>
    /// Returns deterministic changes against the previous import into this same source-scoped workspace.
    /// </summary>
    public async Task<WorkspaceDiffLastImportResult> DiffLastImportAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace current = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        ImportSource? source = ResolveImportSource(current);
        WorkspaceDiffLastImportResult result = new()
        {
            WorkspaceId = current.Id,
            Provider = source?.Provider,
            ExternalId = source?.ExternalId,
            LocalWorkspaceId = current.Id
        };
        if (source is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.WorkspaceHasNoSource;
            result.Notes.Add("Workspace does not have a provider source reference.");
            return result;
        }

        if (!IsImportHistoryProvider(source.Provider))
        {
            result.Status = WorkspaceDiffLastImportStatus.SourceUnsupported;
            result.Notes.Add($"Import history does not support provider '{source.Provider}'.");
            return result;
        }

        DeckImportHistoryEntry? entry = FindLatestImportHistoryEntry(current, source);
        if (entry is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.NoPriorBaseline;
            result.Notes.Add("No prior import baseline exists for this provider, external deck id, and local workspace id.");
            return result;
        }

        result.ImportedAt = entry.ImportedAt;
        if (entry.BaselineWorkspace is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.HistoryUnavailable;
            result.Notes.Add("The matching import history entry does not contain a baseline workspace snapshot.");
            return result;
        }

        result.Status = WorkspaceDiffLastImportStatus.BaselineFound;
        result.Diff = BuildWorkspaceDiff(current, entry.BaselineWorkspace);
        result.Notes.Add($"Compared against import history captured at {entry.ImportedAt:O}.");
        return result;
    }

    /// <summary>
    /// Returns the prior import baseline workspace for analysis comparison workflows.
    /// </summary>
    public async Task<WorkspaceImportBaselineResolution> GetLastImportBaselineAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace current = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        ImportSource? source = ResolveImportSource(current);
        WorkspaceImportBaselineResolution result = new()
        {
            WorkspaceId = current.Id,
            Provider = source?.Provider,
            ExternalId = source?.ExternalId,
            LocalWorkspaceId = current.Id
        };
        if (source is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.WorkspaceHasNoSource;
            result.Notes.Add("Workspace does not have a provider source reference.");
            return result;
        }

        if (!IsImportHistoryProvider(source.Provider))
        {
            result.Status = WorkspaceDiffLastImportStatus.SourceUnsupported;
            result.Notes.Add($"Import history does not support provider '{source.Provider}'.");
            return result;
        }

        DeckImportHistoryEntry? entry = FindLatestImportHistoryEntry(current, source);
        if (entry is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.NoPriorBaseline;
            result.Notes.Add("No prior import baseline exists for this provider, external deck id, and local workspace id.");
            return result;
        }

        result.ImportedAt = entry.ImportedAt;
        if (entry.BaselineWorkspace is null)
        {
            result.Status = WorkspaceDiffLastImportStatus.HistoryUnavailable;
            result.Notes.Add("The matching import history entry does not contain a baseline workspace snapshot.");
            return result;
        }

        result.Status = WorkspaceDiffLastImportStatus.BaselineFound;
        result.BaselineWorkspace = entry.BaselineWorkspace;
        return result;
    }

    /// <summary>
    /// Builds a deterministic diff for two workspace instances.
    /// </summary>
    private static WorkspaceDiffResult BuildWorkspaceDiff(DeckWorkspace current, DeckWorkspace previous)
    {
        DeckWorkspaceState currentState = BuildWorkspaceState(current);
        DeckWorkspaceState previousState = BuildWorkspaceState(previous);
        Dictionary<string, DiffCardAggregate> currentCards = BuildDiffCardIndex(current);
        Dictionary<string, DiffCardAggregate> previousCards = BuildDiffCardIndex(previous);
        WorkspaceDiffResult result = new()
        {
            WorkspaceId = current.Id,
            PreviousWorkspaceId = previous.Id,
            Baseline = BuildDiffBaseline(previous),
            Current = BuildDiffBaseline(current),
            IncludedCountBefore = previousState.IncludedCount,
            IncludedCountAfter = currentState.IncludedCount,
            IncludedCountDelta = currentState.IncludedCount - previousState.IncludedCount,
            ValidationDelta = BuildValidationDelta(previousState.Validation, currentState.Validation)
        };

        HashSet<string> identities = new(previousCards.Keys, StringComparer.OrdinalIgnoreCase);
        identities.UnionWith(currentCards.Keys);
        foreach (string identity in identities)
        {
            previousCards.TryGetValue(identity, out DiffCardAggregate? before);
            currentCards.TryGetValue(identity, out DiffCardAggregate? after);
            WorkspaceDiffCardChange change = BuildDiffCardChange(identity, before, after);
            if (before is null)
            {
                change.Notes.Add("Card exists only in the current workspace.");
                result.AddedCards.Add(change);
                continue;
            }

            if (after is null)
            {
                change.Notes.Add("Card exists only in the baseline workspace.");
                result.RemovedCards.Add(change);
                continue;
            }

            if (!string.Equals(before.PrimaryCategory, after.PrimaryCategory, StringComparison.OrdinalIgnoreCase))
            {
                change.Notes.Add("Primary category changed.");
                result.PrimaryMoves.Add(change);
            }

            if (!SameValues(before.SecondaryCategories, after.SecondaryCategories))
            {
                change.Notes.Add("Secondary category tags changed.");
                result.SecondaryTagChanges.Add(change);
            }

            if (before.Quantity != after.Quantity)
            {
                change.Notes.Add("Aggregate quantity changed.");
                result.QuantityChanges.Add(change);
            }
        }

        SortDiffRows(result.AddedCards);
        SortDiffRows(result.RemovedCards);
        SortDiffRows(result.PrimaryMoves);
        SortDiffRows(result.SecondaryTagChanges);
        SortDiffRows(result.QuantityChanges);
        result.Notes.Add($"Baseline explicitly selected: {previous.Id} from {result.Baseline.Source} at {result.Baseline.Timestamp:O}.");
        if (current.Id.Equals(previous.Id, StringComparison.OrdinalIgnoreCase))
        {
            result.Notes.Add("Current and baseline workspace ids are the same; diff rows should be empty unless storage changed during the call.");
        }

        return result;
    }

    /// <summary>
    /// Finds the newest import history entry matching the current workspace source scope.
    /// </summary>
    private static DeckImportHistoryEntry? FindLatestImportHistoryEntry(
        DeckWorkspace workspace,
        ImportSource source)
    {
        DeckImportHistoryEntry? result = null;
        foreach (DeckImportHistoryEntry entry in workspace.ImportHistory)
        {
            if (!entry.Provider.Equals(source.Provider, StringComparison.OrdinalIgnoreCase)
                || !entry.ExternalId.Equals(source.ExternalId, StringComparison.OrdinalIgnoreCase)
                || !entry.LocalWorkspaceId.Equals(workspace.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (result is null || entry.ImportedAt > result.ImportedAt)
            {
                result = entry;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds metadata for one side of a diff.
    /// </summary>
    private static WorkspaceDiffBaseline BuildDiffBaseline(DeckWorkspace workspace)
    {
        return new WorkspaceDiffBaseline
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Mode = workspace.Mode.ToString(),
            Persistence = DeckPersistence.For(workspace),
            Source = BuildDiffSourceLabel(workspace),
            Timestamp = workspace.UpdatedAt,
            WorkspaceResourceUri = $"mtg://workspace/{workspace.Id}"
        };
    }

    /// <summary>
    /// Builds a concise source label for baseline transparency.
    /// </summary>
    private static string BuildDiffSourceLabel(DeckWorkspace workspace)
    {
        List<string> parts = [workspace.Mode.ToString()];
        if (!string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            parts.Add($"archidekt:{workspace.ArchidektDeckId}");
        }

        foreach (DeckSourceReference reference in workspace.SourceReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Provider) && !string.IsNullOrWhiteSpace(reference.ExternalId))
            {
                parts.Add($"{reference.Provider}:{reference.ExternalId}");
            }
        }

        return string.Join("; ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Indexes cards by Scryfall oracle id, print id, or normalized name.
    /// </summary>
    private static Dictionary<string, DiffCardAggregate> BuildDiffCardIndex(DeckWorkspace workspace)
    {
        Dictionary<string, DiffCardAggregate> cards = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards)
        {
            string identity = CardIdentity(card);
            if (!cards.TryGetValue(identity, out DiffCardAggregate? aggregate))
            {
                aggregate = new DiffCardAggregate
                {
                    Identity = identity,
                    CardName = card.Name,
                    ScryfallUri = card.Snapshot?.ScryfallUri
                };
                cards[identity] = aggregate;
            }

            aggregate.Quantity += Math.Max(0, card.Quantity);
            AddDistinct(aggregate.PrimaryCategories, DeckCategoryOrdering.PrimaryCategory(card));
            List<string> categories = DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(card),
                card.Categories);
            foreach (string category in categories)
            {
                AddDistinct(aggregate.Categories, category);
            }

            foreach (string category in categories.Skip(1))
            {
                AddDistinct(aggregate.SecondaryCategories, category);
            }

            if (string.IsNullOrWhiteSpace(aggregate.ScryfallUri) && !string.IsNullOrWhiteSpace(card.Snapshot?.ScryfallUri))
            {
                aggregate.ScryfallUri = card.Snapshot.ScryfallUri;
            }
        }

        foreach (DiffCardAggregate aggregate in cards.Values)
        {
            aggregate.PrimaryCategories.Sort(StringComparer.OrdinalIgnoreCase);
            aggregate.Categories.Sort(StringComparer.OrdinalIgnoreCase);
            aggregate.SecondaryCategories.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return cards;
    }

    /// <summary>
    /// Builds one diff row from optional before and after aggregates.
    /// </summary>
    private static WorkspaceDiffCardChange BuildDiffCardChange(
        string identity,
        DiffCardAggregate? before,
        DiffCardAggregate? after)
    {
        return new WorkspaceDiffCardChange
        {
            Identity = identity,
            CardName = after?.CardName ?? before?.CardName ?? identity,
            QuantityBefore = before?.Quantity ?? 0,
            QuantityAfter = after?.Quantity ?? 0,
            PrimaryCategoryBefore = before?.PrimaryCategory,
            PrimaryCategoryAfter = after?.PrimaryCategory,
            CategoriesBefore = before?.Categories.ToList() ?? [],
            CategoriesAfter = after?.Categories.ToList() ?? [],
            SecondaryCategoriesBefore = before?.SecondaryCategories.ToList() ?? [],
            SecondaryCategoriesAfter = after?.SecondaryCategories.ToList() ?? [],
            ScryfallUri = after?.ScryfallUri ?? before?.ScryfallUri
        };
    }

    /// <summary>
    /// Builds validation deltas for a workspace diff.
    /// </summary>
    private static DeckValidationDelta BuildValidationDelta(
        DeckValidationResult before,
        DeckValidationResult after)
    {
        return new DeckValidationDelta
        {
            AddedErrors = Except(after.Errors, before.Errors),
            RemovedErrors = Except(before.Errors, after.Errors),
            AddedWarnings = Except(after.Warnings, before.Warnings),
            RemovedWarnings = Except(before.Warnings, after.Warnings)
        };
    }

    /// <summary>
    /// Returns case-insensitive values present in left but absent from right.
    /// </summary>
    private static List<string> Except(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        HashSet<string> excluded = new(right, StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string value in left)
        {
            if (!excluded.Contains(value))
            {
                result.Add(value);
            }
        }

        return result;
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
    /// Builds the identity string used for card matching across workspace imports.
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
    /// Sorts diff rows by display card name.
    /// </summary>
    private static void SortDiffRows(List<WorkspaceDiffCardChange> rows)
    {
        rows.Sort((left, right) => string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Aggregates equivalent cards for diff comparison.
    /// </summary>
    private sealed class DiffCardAggregate
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
        /// Gets or sets all secondary categories found for this identity.
        /// </summary>
        public List<string> SecondaryCategories { get; set; } = [];

        /// <summary>
        /// Gets or sets Scryfall page when known.
        /// </summary>
        public string? ScryfallUri { get; set; }

        /// <summary>
        /// Gets primary category label for comparison.
        /// </summary>
        public string PrimaryCategory => PrimaryCategories.Count == 1
            ? PrimaryCategories[0]
            : string.Join(" | ", PrimaryCategories);
    }
}
