using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Persists the metadata.
    /// </summary>
    public async Task PersistMetadataAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        object payload = new
        {
            name = workspace.Name,
            deckFormat = workspace.Format,
            description = workspace.Description,
        };

        await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/{deckId}/update/",
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
