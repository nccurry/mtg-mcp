using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Captures provider import baselines for same-workspace follow-up diffs.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Keeps only the most recent same-source import baselines per workspace.
    /// </summary>
    private const int ImportHistoryRetention = 5;

    /// <summary>
    /// Saves an imported workspace and records the previous same-workspace state when available.
    /// </summary>
    private async Task<DeckWorkspace> SaveImportedWorkspaceAsync(
        DeckWorkspace imported,
        string? localWorkspaceId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(localWorkspaceId))
        {
            imported.Id = localWorkspaceId.Trim();
        }

        EnsureImportSourceReference(imported);
        DeckWorkspace? previous = await Repository.GetAsync(imported.Id, cancellationToken)
            .ConfigureAwait(false);
        ImportSource? source = ResolveImportSource(imported);
        if (previous is not null)
        {
            imported.ImportHistory = previous.ImportHistory.ToList();
            imported.LocalCheckpoints = previous.LocalCheckpoints.ToList();
        }

        if (previous is not null
            && source is not null
            && SameImportScope(previous, imported.Id, source))
        {
            imported.ImportHistory.Add(new DeckImportHistoryEntry
            {
                Provider = source.Provider,
                ExternalId = source.ExternalId,
                LocalWorkspaceId = imported.Id,
                ImportedAt = DateTimeOffset.UtcNow,
                BaselineWorkspace = CloneImportBaseline(previous)
            });
            TrimImportHistory(imported.ImportHistory);
        }

        return await Repository.SaveAsync(imported, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures imported Archidekt workspaces have a source reference for history lookups.
    /// </summary>
    private static void EnsureImportSourceReference(DeckWorkspace workspace)
    {
        if (workspace.Mode != WorkspaceMode.Archidekt || string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            return;
        }

        foreach (DeckSourceReference reference in workspace.SourceReferences)
        {
            if (reference.Provider.Equals(DeckImportProviders.Archidekt, StringComparison.OrdinalIgnoreCase)
                && reference.ExternalId.Equals(workspace.ArchidektDeckId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        workspace.SourceReferences.Add(new DeckSourceReference
        {
            Provider = DeckImportProviders.Archidekt,
            ExternalId = workspace.ArchidektDeckId,
            ImportedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Resolves the primary provider source used for import history.
    /// </summary>
    private static ImportSource? ResolveImportSource(DeckWorkspace workspace)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && !string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            return new ImportSource(DeckImportProviders.Archidekt, workspace.ArchidektDeckId.Trim());
        }

        foreach (DeckSourceReference reference in workspace.SourceReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Provider) && !string.IsNullOrWhiteSpace(reference.ExternalId))
            {
                return new ImportSource(reference.Provider.Trim().ToLowerInvariant(), reference.ExternalId.Trim());
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the provider participates in last-import diff history.
    /// </summary>
    private static bool IsImportHistoryProvider(string provider)
    {
        return provider.Equals(DeckImportProviders.Archidekt, StringComparison.OrdinalIgnoreCase)
            || provider.Equals(DeckImportProviders.Moxfield, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the previous workspace belongs to the same import-history scope.
    /// </summary>
    private static bool SameImportScope(
        DeckWorkspace previous,
        string localWorkspaceId,
        ImportSource source)
    {
        if (!previous.Id.Equals(localWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ImportSource? previousSource = ResolveImportSource(previous);
        return previousSource is not null
            && previousSource.Provider.Equals(source.Provider, StringComparison.OrdinalIgnoreCase)
            && previousSource.ExternalId.Equals(source.ExternalId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes old import-history entries after preserving the newest scoped baselines.
    /// </summary>
    private static void TrimImportHistory(List<DeckImportHistoryEntry> history)
    {
        history.Sort(static (left, right) => right.ImportedAt.CompareTo(left.ImportedAt));
        if (history.Count <= ImportHistoryRetention)
        {
            return;
        }

        history.RemoveRange(ImportHistoryRetention, history.Count - ImportHistoryRetention);
    }

    /// <summary>
    /// Deep-clones a baseline snapshot while preventing recursive history growth.
    /// </summary>
    private static DeckWorkspace CloneImportBaseline(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        DeckWorkspace clone = JsonSerializer.Deserialize<DeckWorkspace>(json) ?? new DeckWorkspace();
        clone.ImportHistory = [];
        clone.LocalCheckpoints = [];
        return clone;
    }

    /// <summary>
    /// Identifies a provider deck import source.
    /// </summary>
    private sealed record ImportSource(string Provider, string ExternalId);
}
