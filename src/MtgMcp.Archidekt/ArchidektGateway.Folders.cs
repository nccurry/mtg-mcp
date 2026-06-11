using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates Archidekt folder HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Lists folders visible to configured Archidekt credentials.
    /// </summary>
    public async Task<IReadOnlyList<ArchidektFolder>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await GetJsonAsync("api/decks/folderTree/", cancellationToken)
            .ConfigureAwait(false);
        List<ArchidektFolder> folders = [];
        foreach (JsonElement element in EnumerateFolderNodes(document.RootElement))
        {
            string? id = GetString(element, "id");
            string? name = GetString(element, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            folders.Add(new ArchidektFolder
            {
                Id = id,
                Name = name,
                ParentFolderId = GetString(element, "parent")
                    ?? GetString(element, "parentFolder")
                    ?? GetNestedString(element, "parent", "id")
                    ?? GetNestedString(element, "parentFolder", "id")
            });
        }

        return folders;
    }

    /// <summary>
    /// Creates a folder under an optional parent folder.
    /// </summary>
    public async Task<ArchidektFolder> CreateFolderAsync(
        string name,
        string? parentFolderId,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                "api/decks/folders/",
                new
                {
                    name = string.IsNullOrWhiteSpace(name) ? "Untitled Folder" : name.Trim(),
                    parent = ParseIntOrString(parentFolderId)
                },
                cancellationToken)
            .ConfigureAwait(false);

        JsonElement root = document.RootElement.TryGetProperty("folder", out JsonElement folder)
            && folder.ValueKind == JsonValueKind.Object
            ? folder
            : document.RootElement;
        return new ArchidektFolder
        {
            Id = GetString(root, "id") ?? "",
            Name = GetString(root, "name") ?? name.Trim(),
            ParentFolderId = GetString(root, "parent")
                ?? GetString(root, "parentFolder")
                ?? parentFolderId
        };
    }

    /// <summary>
    /// Moves decks into the requested folder.
    /// </summary>
    public async Task<ArchidektMoveDecksResult> MoveDecksAsync(
        IReadOnlyList<string> deckIds,
        string? folderId,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        List<object?> ids = deckIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(ParseIntOrString)
            .ToList();
        await SendJsonAsync(
                HttpMethod.Patch,
                "api/massUpdate/",
                new
                {
                    deckIds = ids,
                    parentFolder = ParseIntOrString(folderId)
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new ArchidektMoveDecksResult
        {
            FolderId = folderId,
            DeckIds = deckIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            Moved = ids.Count
        };
    }

    /// <summary>
    /// Enumerates folder-like nodes from Archidekt's recursive folder tree shapes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateFolderNodes(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                foreach (JsonElement nested in EnumerateFolderNodes(item))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("results", out JsonElement results)
            || root.TryGetProperty("data", out results))
        {
            foreach (JsonElement nested in EnumerateFolderNodes(results))
            {
                yield return nested;
            }
        }

        if (root.TryGetProperty("id", out _) && root.TryGetProperty("name", out _))
        {
            yield return root;
        }

        foreach (string propertyName in new[] { "children", "folders" })
        {
            if (root.TryGetProperty(propertyName, out JsonElement children))
            {
                foreach (JsonElement child in EnumerateFolderNodes(children))
                {
                    yield return child;
                }
            }
        }
    }
}
