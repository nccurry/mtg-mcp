using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Lists the decks.
    /// </summary>
    public async Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
        CancellationToken cancellationToken
    )
    {
        return await ListDecksAsync(new ArchidektDeckListRequest(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists decks with optional pagination and folder filters.
    /// </summary>
    public async Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(
        ArchidektDeckListRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string path = BuildDeckListPath(request);
        using JsonDocument document = await GetJsonAsync(path, cancellationToken)
            .ConfigureAwait(false);
        List<ArchidektDeckSummary> decks = [];
        foreach (JsonElement item in EnumerateCollection(document.RootElement, "results", "data", "decks"))
        {
            decks.Add(
                new ArchidektDeckSummary
                {
                    Id = GetString(item, "id") ?? "",
                    Name = GetString(item, "name") ?? "",
                    Format = NormalizeDeckFormat(GetString(item, "deckFormat") ?? GetString(item, "format")),
                    FolderId = GetString(item, "folderId")
                        ?? GetNestedString(item, "parentFolder", "id")
                        ?? GetNestedString(item, "folder", "id")
                        ?? GetString(item, "folder"),
                    FolderName = GetString(item, "folderName")
                        ?? GetNestedString(item, "parentFolder", "name")
                        ?? GetNestedString(item, "folder", "name"),
                    FolderPath = GetString(item, "folderPath")
                        ?? GetString(item, "path")
                        ?? GetNestedString(item, "parentFolder", "path")
                        ?? GetNestedString(item, "folder", "path"),
                    Visibility = ReadVisibility(item),
                    CardCount = GetInt(item, "cardCount")
                        ?? GetInt(item, "cardsCount")
                        ?? GetInt(item, "mainboardCount"),
                    UpdatedAt = TryDate(
                        GetString(item, "updatedAt") ?? GetString(item, "updated_at")
                    ),
                }
            );
        }

        if (!string.IsNullOrWhiteSpace(request.FolderName))
        {
            decks = decks
                .Where(deck => deck.FolderName?.Equals(request.FolderName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return decks;
    }

    /// <summary>
    /// Imports the deck.
    /// </summary>
    public async Task<DeckWorkspace> ImportDeckAsync(
        string deckIdOrUrl,
        bool writeBack,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: false, cancellationToken).ConfigureAwait(false);
        string deckId = ExtractDeckId(deckIdOrUrl);
        using JsonDocument document = await GetJsonAsync($"api/decks/{deckId}/", cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;

        DeckWorkspace workspace = new()
        {
            Name = GetString(root, "name") ?? $"Archidekt Deck {deckId}",
            Format = NormalizeDeckFormat(GetString(root, "deckFormat") ?? GetString(root, "format")),
            Description = GetString(root, "description"),
            Mode = WorkspaceMode.Archidekt,
            WriteBack = writeBack,
            ArchidektDeckId = deckId,
            ArchidektDeckFormatId = GetDeckFormatId(root),
            Categories = ParseCategories(root),
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Archidekt,
                    ExternalId = deckId,
                    Url = $"https://archidekt.com/decks/{deckId}",
                },
            ],
        };

        workspace.Cards = ParseCards(root, workspace.Categories);
        return workspace;
    }

    /// <summary>
    /// Gets the Archidekt deck list path for the available identity context.
    /// </summary>
    private string GetDeckListPath()
    {
        return !string.IsNullOrWhiteSpace(sessionUserId)
            ? $"api/users/{sessionUserId}/decks/"
            : "api/decks/";
    }

    /// <summary>
    /// Builds a deck list path with Archidekt pagination and folder query parameters.
    /// </summary>
    private string BuildDeckListPath(ArchidektDeckListRequest request)
    {
        List<string> query = [];
        if (request.Page is { } page && page > 0)
        {
            query.Add($"page={page.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (request.PageSize is { } pageSize && pageSize > 0)
        {
            query.Add($"pageSize={pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(request.FolderId))
        {
            query.Add($"folder={Uri.EscapeDataString(request.FolderId)}");
        }

        string path = GetDeckListPath();
        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }

    /// <summary>
    /// Reads Archidekt visibility fields without exposing credentials.
    /// </summary>
    private static string? ReadVisibility(JsonElement item)
    {
        bool? privateFlag = ReadOptionalBool(item, "private");
        bool? unlisted = ReadOptionalBool(item, "unlisted");
        if (unlisted == true)
        {
            return "unlisted";
        }

        if (privateFlag.HasValue)
        {
            return privateFlag.Value ? "private" : "public";
        }

        return GetString(item, "visibility");
    }

    /// <summary>
    /// Reads a nullable boolean property.
    /// </summary>
    private static bool? ReadOptionalBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            _ => null,
        };
    }
}
