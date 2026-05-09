using System.Text.Json;

namespace MtgMcp.Core;

public sealed class JsonDeckWorkspaceRepository : IDeckWorkspaceRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string workspaceDirectory;

    public JsonDeckWorkspaceRepository(string dataDirectory)
    {
        workspaceDirectory = Path.Combine(dataDirectory, "workspaces");
    }

    public async Task<DeckWorkspace> SaveAsync(DeckWorkspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Directory.CreateDirectory(workspaceDirectory);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;

        string path = GetWorkspacePath(workspace.Id);
        string tempPath = Path.Combine(workspaceDirectory, $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, workspace, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return workspace;
    }

    public async Task<DeckWorkspace?> GetAsync(string workspaceId, CancellationToken cancellationToken)
    {
        string path = GetWorkspacePath(workspaceId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DeckWorkspace>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeckWorkspace>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspaceDirectory))
        {
            return [];
        }

        List<DeckWorkspace> workspaces = [];
        foreach (string path in Directory.EnumerateFiles(workspaceDirectory, "*.json"))
        {
            await using FileStream stream = File.OpenRead(path);
            DeckWorkspace? workspace = await JsonSerializer.DeserializeAsync<DeckWorkspace>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (workspace is not null)
            {
                workspaces.Add(workspace);
            }
        }

        return workspaces
            .OrderByDescending(workspace => workspace.UpdatedAt)
            .ToList();
    }

    private string GetWorkspacePath(string workspaceId)
    {
        string safeId = string.Concat(workspaceId.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeId))
        {
            throw new ArgumentException("Workspace id must contain at least one alphanumeric character.", nameof(workspaceId));
        }

        return Path.Combine(workspaceDirectory, $"{safeId}.json");
    }
}
