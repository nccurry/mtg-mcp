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
    /// Persists the category.
    /// </summary>
    public async Task PersistCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        category.IsPremier = category.IsPremier || DeckDefaults.IsCommanderCategory(category.Name);
        object payload = new
        {
            name = category.Name,
            deck = ParseIntOrString(deckId),
            includedInDeck = category.IncludedInDeck,
            includedInPrice = category.IncludedInPrice,
            isPremier = category.IsPremier,
        };

        if (category.ArchidektCategoryId.HasValue)
        {
            await SendJsonAsync(
                    HttpMethod.Patch,
                    $"api/decks/category/{category.ArchidektCategoryId.Value}/",
                    payload,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return;
        }

        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                "api/decks/createCategory/",
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
        category.ArchidektCategoryId = GetInt(document.RootElement, "id");
    }

    /// <summary>
    /// Deletes the category.
    /// </summary>
    public async Task DeleteCategoryAsync(
        DeckWorkspace workspace,
        DeckCategory category,
        CancellationToken cancellationToken
    )
    {
        if (!category.ArchidektCategoryId.HasValue)
        {
            return;
        }

        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        await SendAsync(
                HttpMethod.Delete,
                $"api/decks/category/{category.ArchidektCategoryId.Value}/",
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
