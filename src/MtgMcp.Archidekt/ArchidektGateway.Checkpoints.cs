using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Creates the checkpoint.
    /// </summary>
    public async Task<DeckCheckpoint> CreateCheckpointAsync(
        DeckWorkspace workspace,
        string name,
        string? description,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                $"api/decks/{deckId}/snapshots/",
                new { name, description },
                cancellationToken
            )
            .ConfigureAwait(false);

        return ParseCheckpoint(document.RootElement, deckId);
    }

    /// <summary>
    /// Lists the checkpoints.
    /// </summary>
    public async Task<IReadOnlyList<DeckCheckpoint>> ListCheckpointsAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        using JsonDocument document = await GetJsonAsync(
                $"api/decks/{deckId}/snapshots/",
                cancellationToken
            )
            .ConfigureAwait(false);
        List<DeckCheckpoint> checkpoints = [];
        foreach (JsonElement item in EnumerateCollection(document.RootElement))
        {
            checkpoints.Add(ParseCheckpoint(item, deckId));
        }

        return checkpoints;
    }

    /// <summary>
    /// Gets the checkpoint.
    /// </summary>
    public async Task<DeckCheckpoint> GetCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        using JsonDocument document = await GetJsonAsync(
                $"api/decks/snapshots/{checkpointId}/",
                cancellationToken
            )
            .ConfigureAwait(false);
        return ParseCheckpoint(document.RootElement, deckId);
    }

    /// <summary>
    /// Renames the checkpoint.
    /// </summary>
    public async Task<DeckCheckpoint> RenameCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        string name,
        string? description,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/snapshots/{checkpointId}/",
                new { name, description },
                cancellationToken
            )
            .ConfigureAwait(false);

        return ParseCheckpoint(document.RootElement, deckId);
    }

    /// <summary>
    /// Deletes the checkpoint.
    /// </summary>
    public async Task DeleteCheckpointAsync(
        DeckWorkspace workspace,
        string checkpointId,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"api/decks/snapshots/{checkpointId}/"
        );
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
