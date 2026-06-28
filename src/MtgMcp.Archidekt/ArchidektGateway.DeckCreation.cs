using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates Archidekt deck creation.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Creates a new Archidekt deck and returns a writeback workspace bound to it.
    /// </summary>
    public async Task<DeckWorkspace> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string? parentFolderId = await ResolveCreateDeckFolderIdAsync(request, cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                "api/decks/v2/",
                BuildCreateDeckPayload(request, parentFolderId),
                cancellationToken
            )
            .ConfigureAwait(false);

        JsonElement deckElement = document.RootElement.TryGetProperty("deck", out JsonElement nestedDeck)
            && nestedDeck.ValueKind == JsonValueKind.Object
            ? nestedDeck
            : document.RootElement;
        string deckId =
            GetString(deckElement, "id")
            ?? GetNestedString(document.RootElement, "deck", "id")
            ?? throw new InvalidOperationException("Archidekt create deck response did not include a deck id.");

        DeckWorkspace workspace = new()
        {
            Name = GetString(deckElement, "name") ?? NormalizeDeckName(request.Name),
            Format = NormalizeDeckFormat(GetString(deckElement, "deckFormat") ?? request.Format),
            Description = GetString(deckElement, "description") ?? request.Description,
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = deckId,
            ArchidektDeckFormatId = GetDeckFormatId(deckElement) ?? ToDeckFormatId(request.Format),
            Categories = ParseCategories(deckElement),
        };
        workspace.Cards = ParseCards(deckElement, workspace.Categories);
        return workspace;
    }

    /// <summary>
    /// Resolves the folder id for deck creation without silently falling back to root.
    /// </summary>
    private async Task<string?> ResolveCreateDeckFolderIdAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ParentFolderId))
        {
            return request.ParentFolderId;
        }

        if (string.IsNullOrWhiteSpace(request.FolderName))
        {
            return null;
        }

        List<ArchidektFolder> matches = [];
        IReadOnlyList<ArchidektFolder> folders = await ListFoldersAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (ArchidektFolder folder in folders)
        {
            if (folder.Name.Equals(request.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(folder);
            }
        }

        if (matches.Count == 1)
        {
            return matches[0].Id;
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Archidekt folder '{request.FolderName}' was not found; pass parentFolderId to create a deck in a known folder.");
        }

        throw new InvalidOperationException(
            $"Archidekt folder name '{request.FolderName}' matched {matches.Count} folders; pass parentFolderId to disambiguate.");
    }

    /// <summary>
    /// Builds Archidekt's new-deck payload while defaulting to private visibility.
    /// </summary>
    private static Dictionary<string, object?> BuildCreateDeckPayload(
        ArchidektDeckCreateRequest request,
        string? parentFolderId)
    {
        string visibility = NormalizeVisibility(request.Visibility);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = NormalizeDeckName(request.Name),
            ["description"] = request.Description,
            ["deckFormat"] = ToDeckFormatId(request.Format),
            ["edhBracket"] = null,
            ["parentFolder"] = ParseIntOrString(parentFolderId),
            ["private"] = visibility != "public",
            ["unlisted"] = visibility == "unlisted",
            ["theorycrafted"] = false,
            ["game"] = null,
            ["cardPackage"] = null,
            ["extras"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["decksToInclude"] = Array.Empty<int>(),
                ["commandersToAdd"] = Array.Empty<int>(),
                ["forceCardsToSingleton"] = false,
                ["ignoreCardsOutOfCommanderIdentity"] = true,
            },
        };
    }

    /// <summary>
    /// Normalizes an empty deck name for create requests.
    /// </summary>
    private static string NormalizeDeckName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "Untitled Deck" : name.Trim();
    }

    /// <summary>
    /// Converts common format names to Archidekt's numeric deck format ids.
    /// </summary>
    private static int ToDeckFormatId(string? format)
    {
        return format?.Trim().ToLowerInvariant() switch
        {
            "" or null => 3,
            "commander" or "edh" => 3,
            "standard" => 1,
            "modern" => 2,
            "legacy" => 4,
            "vintage" => 5,
            "pauper" => 6,
            "pioneer" => 7,
            "brawl" => 8,
            "historic" => 9,
            "oathbreaker" => 10,
            _ when int.TryParse(
                format,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int id) => id,
            _ => 3,
        };
    }

    /// <summary>
    /// Normalizes public visibility choices.
    /// </summary>
    private static string NormalizeVisibility(string? visibility)
    {
        string normalized = visibility?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "public" => "public",
            "unlisted" => "unlisted",
            _ => "private",
        };
    }
}
