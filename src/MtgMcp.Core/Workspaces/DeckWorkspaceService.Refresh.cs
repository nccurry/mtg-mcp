namespace MtgMcp.Core;

/// <summary>
/// Refreshes source-backed workspaces in place without mutating provider decks.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Re-imports a workspace from its recorded source while preserving the local workspace id.
    /// </summary>
    public async Task<WorkspaceRefreshFromSourceResult> RefreshWorkspaceFromSourceAsync(
        string workspaceId,
        bool? writeBack,
        CancellationToken cancellationToken)
    {
        DeckWorkspace current = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        ImportSource? source = ResolveImportSource(current);
        WorkspaceRefreshFromSourceResult result = new()
        {
            WorkspaceId = current.Id,
            Provider = source?.Provider,
            ExternalId = source?.ExternalId,
            LocalWorkspaceId = current.Id
        };

        if (source is null)
        {
            result.Status = WorkspaceRefreshFromSourceStatuses.WorkspaceHasNoSource;
            result.Notes.Add("Workspace does not have a provider source reference.");
            return result;
        }

        if (!IsImportHistoryProvider(source.Provider))
        {
            result.Status = WorkspaceRefreshFromSourceStatuses.SourceUnsupported;
            result.Notes.Add($"Provider '{source.Provider}' is not supported by workspace refresh.");
            return result;
        }

        try
        {
            DeckWorkspace refreshed = await RefreshSupportedSourceAsync(
                    current,
                    source,
                    writeBack,
                    cancellationToken)
                .ConfigureAwait(false);
            result.Status = WorkspaceRefreshFromSourceStatuses.Refreshed;
            result.Workspace = refreshed;
            result.DiffLastImport = await DiffLastImportAsync(refreshed.Id, cancellationToken)
                .ConfigureAwait(false);
            result.Notes.Add("Workspace refreshed in place from its provider source.");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Status = WorkspaceRefreshFromSourceStatuses.SourceUnavailable;
            result.Notes.Add($"Source refresh failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Imports one supported provider source into the existing local workspace id.
    /// </summary>
    private async Task<DeckWorkspace> RefreshSupportedSourceAsync(
        DeckWorkspace current,
        ImportSource source,
        bool? writeBack,
        CancellationToken cancellationToken)
    {
        if (source.Provider.Equals(DeckImportProviders.Archidekt, StringComparison.OrdinalIgnoreCase))
        {
            return await OpenArchidektDeckAsync(
                    source.ExternalId,
                    writeBack ?? current.WriteBack,
                    current.Id,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (source.Provider.Equals(DeckImportProviders.Moxfield, StringComparison.OrdinalIgnoreCase))
        {
            DeckWorkspace workspace = await RequireMoxfieldGateway()
                .ImportDeckAsync(source.ExternalId, cancellationToken)
                .ConfigureAwait(false);
            await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken)
                .ConfigureAwait(false);
            workspace.Mode = WorkspaceMode.Local;
            workspace.WriteBack = false;
            return await SaveImportedWorkspaceAsync(workspace, current.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Provider '{source.Provider}' is not supported by workspace refresh.");
    }
}
