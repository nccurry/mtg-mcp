namespace MtgMcp.Core;

/// <summary>
/// Persists deck workspaces as JSON files under the local data directory.
/// </summary>
public sealed class JsonDeckWorkspaceRepository : IDeckWorkspaceRepository
{
    /// <summary>
    /// Owns atomic JSON persistence and legacy filename migration for workspace files.
    /// </summary>
    private readonly JsonFileStore<DeckWorkspace> store;

    /// <summary>
    /// Creates a repository rooted under the mtg-mcp data directory.
    /// </summary>
    public JsonDeckWorkspaceRepository(string dataDirectory)
    {
        store = new JsonFileStore<DeckWorkspace>(
            Path.Combine(dataDirectory, "workspaces"),
            "Workspace",
            static workspace => workspace.Id);
    }

    /// <summary>
    /// Saves a workspace and refreshes its local update timestamp.
    /// </summary>
    public async Task<DeckWorkspace> SaveAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;

        return await store.SaveAsync(workspace.Id, workspace, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a workspace by id from disk.
    /// </summary>
    public async Task<DeckWorkspace?> GetAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        return await store.GetAsync(workspaceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists saved workspaces with the newest updates first.
    /// </summary>
    public async Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
    {
        List<DeckWorkspace> workspaces = [.. await store.ListAsync(cancellationToken).ConfigureAwait(false)];
        workspaces.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
        return workspaces;
    }
}
