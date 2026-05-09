using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public sealed partial class ArchidektGateway
{
    public async Task<IReadOnlyList<ArchidektDeckSummary>> ListDecksAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string path = GetDeckListPath();
        using JsonDocument document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        List<ArchidektDeckSummary> decks = [];
        foreach (JsonElement item in EnumerateCollection(document.RootElement))
        {
            decks.Add(new ArchidektDeckSummary
            {
                Id = GetString(item, "id") ?? "",
                Name = GetString(item, "name") ?? "",
                Format = GetString(item, "deckFormat") ?? GetString(item, "format"),
                UpdatedAt = TryDate(GetString(item, "updatedAt") ?? GetString(item, "updated_at"))
            });
        }

        return decks;
    }

    public async Task<DeckWorkspace> ImportDeckAsync(string deckIdOrUrl, bool writeBack, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: false, cancellationToken).ConfigureAwait(false);
        string deckId = ExtractDeckId(deckIdOrUrl);
        using JsonDocument document = await GetJsonAsync($"api/decks/{deckId}/", cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;

        DeckWorkspace workspace = new()
        {
            Name = GetString(root, "name") ?? $"Archidekt Deck {deckId}",
            Format = GetString(root, "deckFormat") ?? GetString(root, "format") ?? "commander",
            Description = GetString(root, "description"),
            Mode = WorkspaceMode.Archidekt,
            WriteBack = writeBack,
            ArchidektDeckId = deckId,
            Categories = ParseCategories(root)
        };

        workspace.Cards = ParseCards(root, workspace.Categories);
        return workspace;
    }

    private string GetDeckListPath()
    {
        ArchidektCredentials loaded = LoadCredentials();
        return !string.IsNullOrWhiteSpace(loaded.UserId)
            ? $"api/users/{loaded.UserId}/decks/"
            : "api/decks/";
    }
}
