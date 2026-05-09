using System.Text.Json;
using MtgMcp.Core;

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
        object payload = new
        {
            name = category.Name,
            deck = ParseIntOrString(deckId),
            includedInDeck = category.IncludedInDeck,
            includedInPrice = category.IncludedInPrice,
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
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"api/decks/category/{category.ArchidektCategoryId.Value}/"
        );
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
